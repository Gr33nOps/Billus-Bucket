using System;

namespace BilluKiBalti
{
    public enum DropKind
    {
        Cash,
        Bomb
    }

    /// <summary>
    /// Moves a falling item over time.
    /// Bombs fall straight down; rupee notes sway and flutter in the wind.
    /// </summary>
    public sealed class DropMotion
    {
        private const int FrameCount = 4;
        private const float BombFrameRate = 7f;

        private readonly float startX;
        private readonly float startY;
        private readonly float speed;
        private readonly float leftBound;
        private readonly float rightBound;

        private readonly float phase;
        private readonly float frequency;
        private readonly float amplitude;
        private readonly float flutterRate;

        public DropKind Kind { get; }
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Age { get; private set; }
        public float Rotation { get; private set; }

        public int AnimationFrame
        {
            get
            {
                if (Kind == DropKind.Bomb)
                {
                    return (int)(Age * BombFrameRate) % FrameCount;
                }

                return (int)(Age * flutterRate + phase) % FrameCount;
            }
        }

        public DropMotion(
            DropKind kind,
            float x,
            float y,
            float speed,
            int? seed = null,
            float left = float.NegativeInfinity,
            float right = float.PositiveInfinity)
        {
            Kind = kind;
            startX = x;
            startY = y;
            X = x;
            Y = y;
            this.speed = speed;
            leftBound = left;
            rightBound = right;

            Random random = seed.HasValue ? new Random(seed.Value) : Random.Shared;
            phase = (float)random.NextDouble() * MathF.PI * 2f;
            frequency = 1.5f + (float)random.NextDouble() * 1.7f;
            amplitude = 10f + (float)random.NextDouble() * 5f;
            flutterRate = 4f + (float)random.NextDouble() * 6f;
        }

        public void Update(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds <= 0f)
            {
                return;
            }

            Age += seconds;

            if (Kind == DropKind.Bomb)
            {
                X = startX;
                Y = startY + speed * Age;
                return;
            }

            // Combine slow drift, quick flutter, and broader wind gusts so each note feels alive.
            float slowWave = MathF.Sin(Age * frequency * 0.65f + phase) - MathF.Sin(phase);
            float quickFlutter = MathF.Sin(Age * frequency * 1.7f + phase * 2f) - MathF.Sin(phase * 2f);

            float gustFrequency = 0.8f + frequency * 0.18f;
            float shiftedTime = Age + phase;
            float windGust = 28f * (MathF.Sin(shiftedTime * gustFrequency) - MathF.Sin(phase * gustFrequency));

            float rawX = startX + amplitude * slowWave + 3f * quickFlutter + windGust;
            X = ReflectWithinBounds(rawX, leftBound, rightBound);

            Rotation = 0.42f * MathF.Cos(shiftedTime * gustFrequency)
                     + 0.30f * MathF.Sin(Age * frequency * 1.7f + phase);

            // Compute Y directly from Age so fall distance stays identical at any frame rate.
            float verticalFlutter = (0.12f / frequency) * (MathF.Sin(Age * frequency + phase) - MathF.Sin(phase));
            Y = startY + speed * (Age + verticalFlutter);
        }

        private static float ReflectWithinBounds(float rawX, float left, float right)
        {
            if (!float.IsFinite(left) || !float.IsFinite(right))
            {
                return rawX;
            }

            if (right <= left)
            {
                return left;
            }

            // Bounce wind gusts off the invisible side margins instead of pinning notes flat against the edge.
            float width = right - left;
            float doubleWidth = 2f * width;
            float wrapped = ((rawX - left) % doubleWidth + doubleWidth) % doubleWidth;

            if (wrapped <= width)
            {
                return left + wrapped;
            }

            return left + (doubleWidth - wrapped);
        }
    }
}

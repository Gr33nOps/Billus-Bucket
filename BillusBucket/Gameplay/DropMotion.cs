using System;

namespace BillusBucket
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
        private readonly float driftPeriod;
        private readonly float driftLeft;
        private readonly float driftRight;
        private readonly uint windSeed;
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
            driftPeriod = 1.25f + (float)random.NextDouble() * .6f;
            windSeed = (uint)random.Next();
            // Leave space for the rotated 12x8 note at both edges. Wind targets
            // lie inside these limits, so the note never needs a wall bounce.
            driftLeft = Math.Max(x - 56f, left);
            driftRight = Math.Min(x + 56f, right);
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

            // Fresh seeded wind points, rather than a repeating oscillation.
            // Consecutive targets may continue left/right or reverse direction.
            float progress = Age / driftPeriod;
            int segment = (int)Math.Min((double)progress, int.MaxValue - 1);
            float t = Math.Clamp(progress - segment, 0f, 1f);
            float from = WindPoint(segment);
            float to = WindPoint(segment + 1);
            float blend = t * t * (3f - 2f * t);
            X = from + (to - from) * blend;

            float sidewaysSpeed = (to - from) * 6f * t * (1f - t) / driftPeriod;
            Rotation = Math.Clamp(sidewaysSpeed / 130f, -.45f, .45f)
                     + .18f * MathF.Sin(Age * frequency * 1.7f + phase);

            // Compute Y directly from Age so fall distance stays identical at any frame rate.
            float verticalFlutter = (0.12f / frequency) * (MathF.Sin(Age * frequency + phase) - MathF.Sin(phase));
            Y = startY + speed * (Age + verticalFlutter);
        }

        private float WindPoint(int index)
        {
            if (rightBound <= leftBound)
                return leftBound;
            if (index == 0)
                return Math.Clamp(startX, driftLeft, driftRight);

            // Stateless hash: identical motion at all frame rates, including
            // skipped frames; no per-frame randomness or integration drift.
            uint value;
            unchecked
            {
                value = windSeed + (uint)index * 0x9e3779b9u;
                value = (value ^ (value >> 16)) * 0x7feb352du;
                value = (value ^ (value >> 15)) * 0x846ca68bu;
                value ^= value >> 16;
            }
            float roll = (value >> 8) / 16777215f;
            return driftLeft + (driftRight - driftLeft) * roll;
        }
    }
}

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

        private readonly float phase;
        private readonly float frequency;
        private readonly float driftPeriod;
        private readonly float driftLeft;
        private readonly float driftRight;
        private readonly uint windSeed;
        private readonly float flutterRate;
        private double motionAge;
        private int windSteps;
        private float windX;
        private float windVelocity;
        private const float WindStep = 1f / 120f;

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

            Random random = seed.HasValue ? new Random(seed.Value) : Random.Shared;
            phase = (float)random.NextDouble() * MathF.PI * 2f;
            frequency = 1.5f + (float)random.NextDouble() * 1.7f;
            driftPeriod = .9f + (float)random.NextDouble() * .65f;
            windSeed = (uint)random.Next();
            // A soft wind corridor keeps paper within reach and inside the sprite margins.
            driftLeft = Math.Max(x - 88f, left);
            driftRight = Math.Min(x + 88f, right);
            if (driftRight < driftLeft) driftRight = driftLeft;
            windX = Math.Clamp(x, driftLeft, driftRight);
            windVelocity = WindVelocity(0);
            if (windX - driftLeft < 20f) windVelocity = Math.Abs(windVelocity);
            if (driftRight - windX < 20f) windVelocity = -Math.Abs(windVelocity);
            flutterRate = 4f + (float)random.NextDouble() * 6f;
        }

        public void Update(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds <= 0f)
            {
                return;
            }

            motionAge += seconds;
            Age = (float)motionAge;

            if (Kind == DropKind.Bomb)
            {
                X = startX;
                Y = startY + speed * Age;
                return;
            }

            // Paper carries sideways momentum. New breezes adjust its velocity
            // gradually, so passing a wind change never stops it at a waypoint.
            // Fixed small steps keep the same path at 30, 60 and 120 fps.
            double windAge = Math.Min(motionAge, 60d); // Drops leave the screen long before this.
            int targetSteps = (int)Math.Floor(windAge * 120d + .00001d);
            while (windSteps < targetSteps)
            {
                float time = windSteps * WindStep;
                float desired = WindVelocity((int)(time / driftPeriod));
                float leftPressure = Math.Clamp((30f - (windX - driftLeft)) / 30f, 0f, 1f);
                float rightPressure = Math.Clamp((30f - (driftRight - windX)) / 30f, 0f, 1f);
                desired += 85f * (leftPressure * leftPressure - rightPressure * rightPressure);
                windVelocity += (desired - windVelocity) * (2.6f * WindStep);
                windX += windVelocity * WindStep;
                windSteps++;
            }
            float remainder = Math.Max(0f, (float)(windAge - windSteps / 120d));
            X = Math.Clamp(windX + windVelocity * remainder, driftLeft, driftRight);
            Rotation = Math.Clamp(windVelocity / 95f, -.45f, .45f)
                     + .14f * MathF.Sin(Age * frequency * 1.7f + phase);

            // Compute Y directly from Age so fall distance stays identical at any frame rate.
            float verticalFlutter = (0.12f / frequency) * (MathF.Sin(Age * frequency + phase) - MathF.Sin(phase));
            Y = startY + speed * (Age + verticalFlutter);
        }

        private float WindVelocity(int index)
        {
            // Each breeze chooses a direction independently; it can keep
            // carrying the note the same way or ease it into a turn.
            uint value;
            unchecked
            {
                value = windSeed + (uint)index * 0x9e3779b9u;
                value = (value ^ (value >> 16)) * 0x7feb352du;
                value = (value ^ (value >> 15)) * 0x846ca68bu;
                value ^= value >> 16;
            }
            float roll = (value >> 8) / 16777215f;
            return (value % 2 == 0 ? -1f : 1f) * (24f + roll * 18f);
        }
    }
}

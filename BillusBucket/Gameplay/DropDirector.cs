using System;

namespace BillusBucket
{
    public readonly struct DropPlan
    {
        public DropKind Kind { get; }
        public float X { get; }
        public float Speed { get; }
        public float Delay { get; }

        public DropPlan(DropKind kind, float x, float speed, float delay)
        {
            Kind = kind;
            X = x;
            Speed = speed;
            Delay = delay;
        }
    }

    /// <summary>
    /// Decides when and where rupee notes and bombs spawn, gradually increasing
    /// the difficulty while giving short 3-second breathers every 16 seconds.
    /// </summary>
    public sealed class DropDirector
    {
        private const float WarmupSeconds = 4.5f;
        private const float RampSeconds = 32f;
        private const float PhraseSeconds = 16f;
        private const float BreatherStartSeconds = 13f;
        private const float BreatherDurationSeconds = 3f;
        private const float MinimumBombGap = 0.62f;

        private const double BaseBombChance = 0.30;
        private const double MaxExtraBombChance = 0.30;
        private const double DirectionFlipChance = 0.18;

        private const float MinCashStep = 12f;
        private const float CashStepRange = 22f;

        private const float BaseBombSpeed = 68f;
        private const float MaxExtraBombSpeed = 52f;
        private const float BaseCashSpeed = 32f;
        private const float MaxExtraCashSpeed = 22f;

        private readonly Random random;
        private float lastBombTime = -100f;
        private float cashX = float.NaN;
        private int cashDirection = 1;

        public DropDirector(int? seed = null)
        {
            random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public DropPlan Next(float elapsedSeconds, float leftBound, float rightBound, float? playerX = null)
        {
            if (rightBound < leftBound)
            {
                float swap = leftBound;
                leftBound = rightBound;
                rightBound = swap;
            }

            float activeSeconds = Math.Max(0f, elapsedSeconds - WarmupSeconds);
            float difficultyRamp = .78f * (1f - MathF.Exp(-activeSeconds / RampSeconds))
                + .22f * (1f - MathF.Exp(-activeSeconds / 150f));
            float phraseTime = activeSeconds % PhraseSeconds;

            // Every 16-second cycle ends with a 3-second breather so the pace stays fair.
            float restAmount = 0f;
            if (phraseTime > BreatherStartSeconds)
            {
                float breatherProgress = (phraseTime - BreatherStartSeconds) / BreatherDurationSeconds;
                restAmount = MathF.Sin(breatherProgress * MathF.PI);
            }

            float pressure = difficultyRamp * (1f - restAmount * 0.42f);

            bool canSpawnBomb =
                elapsedSeconds >= WarmupSeconds &&
                restAmount < 0.85f &&
                elapsedSeconds - lastBombTime >= 1.15f - difficultyRamp * (1.15f - MinimumBombGap);

            bool isBomb = canSpawnBomb &&
                random.NextDouble() < BaseBombChance + pressure * MaxExtraBombChance;

            float spawnX;
            if (isBomb)
            {
                lastBombTime = elapsedSeconds;
                spawnX = leftBound + (float)random.NextDouble() * (rightBound - leftBound);
                // Aim at the position at spawn time only; bombs never home in.
                if (playerX.HasValue && float.IsFinite(playerX.Value) && random.NextDouble() < .60)
                    spawnX = Math.Clamp(playerX.Value + ((float)random.NextDouble() * 2f - 1f) * 48f,
                        leftBound, rightBound);
            }
            else
            {
                if (float.IsNaN(cashX))
                {
                    cashX = (leftBound + rightBound) / 2f;
                }

                if (random.NextDouble() < DirectionFlipChance)
                {
                    cashDirection = -cashDirection;
                }

                float step = MinCashStep + (float)random.NextDouble() * CashStepRange;
                cashX += cashDirection * step;

                if (cashX < leftBound || cashX > rightBound)
                {
                    cashDirection = -cashDirection;
                    cashX = Math.Clamp(cashX, leftBound, rightBound);
                }

                spawnX = cashX;
            }

            float baseDelay = 0.59f - pressure * 0.32f + restAmount * 0.17f;
            float delayJitter = 0.94f + (float)random.NextDouble() * 0.12f;
            float nextDelay = baseDelay * delayJitter;

            DropKind kind = isBomb ? DropKind.Bomb : DropKind.Cash;
            float speed = isBomb
                ? BaseBombSpeed + pressure * MaxExtraBombSpeed
                : BaseCashSpeed + pressure * MaxExtraCashSpeed;

            return new DropPlan(kind, spawnX, speed, nextDelay);
        }
    }
}

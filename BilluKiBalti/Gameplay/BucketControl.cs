using System;

namespace BilluKiBalti
{
    /// <summary>
    /// Handles smooth horizontal movement for Billu and his bucket,
    /// supporting keyboard keys as well as mouse/touch dragging.
    /// </summary>
    public sealed class BucketControl
    {
        private const float MaxSpeed = 135f;
        private const float Acceleration = 750f;
        private const float TurnAcceleration = 1100f;
        private const float Braking = 950f;
        private const float MaxPhysicsStep = 1f / 60f;

        private readonly float minX;
        private readonly float maxX;

        public float X { get; private set; }
        public float VelocityX { get; private set; }
        public int Facing { get; private set; } = 1;
        public float RunAmount => Math.Min(1f, Math.Abs(VelocityX) / MaxSpeed);

        public BucketControl(float startX, float playfieldWidth, float bucketWidth, float playfieldLeft = 0f)
        {
            float leftLimit = playfieldLeft + bucketWidth / 2f;
            float rightLimit = playfieldLeft + playfieldWidth - bucketWidth / 2f;

            if (rightLimit < leftLimit)
            {
                float center = playfieldLeft + playfieldWidth / 2f;
                minX = center;
                maxX = center;
            }
            else
            {
                minX = leftLimit;
                maxX = rightLimit;
            }

            X = Math.Clamp(startX, minX, maxX);
        }

        public void Update(float seconds, int direction, float? pointerX)
        {
            if (!float.IsFinite(seconds) || seconds <= 0f)
            {
                return;
            }

            int clampedDirection = Math.Clamp(direction, -1, 1);
            float? targetX = null;
            if (pointerX.HasValue && float.IsFinite(pointerX.Value))
            {
                targetX = Math.Clamp(pointerX.Value, minX, maxX);
            }

            // Break larger frame times into small 60 FPS steps so movement stays consistent.
            float remaining = seconds;
            while (remaining > 0f)
            {
                float stepSeconds = Math.Min(remaining, MaxPhysicsStep);
                Step(stepSeconds, clampedDirection, targetX);
                remaining -= stepSeconds;
            }
        }

        private void Step(float seconds, int direction, float? targetX)
        {
            float desiredSpeed = direction * MaxSpeed;

            if (targetX.HasValue)
            {
                float distance = targetX.Value - X;
                if (Math.Abs(distance) < 0.5f && Math.Abs(VelocityX) < 20f)
                {
                    X = targetX.Value;
                    VelocityX = 0f;
                    return;
                }

                // Choose a speed that can brake smoothly right onto the pointer target.
                float brakingSpeed = MathF.Sqrt(2f * Braking * Math.Abs(distance));
                desiredSpeed = Math.Sign(distance) * Math.Min(MaxSpeed, brakingSpeed);
            }

            float accelerationRate;
            if (Math.Abs(desiredSpeed) < 0.01f)
            {
                accelerationRate = Braking;
            }
            else if (Math.Abs(VelocityX) > 1f && Math.Sign(desiredSpeed) != Math.Sign(VelocityX))
            {
                accelerationRate = TurnAcceleration;
            }
            else
            {
                accelerationRate = Acceleration;
            }

            float previousVelocity = VelocityX;
            VelocityX = MoveToward(VelocityX, desiredSpeed, accelerationRate * seconds);

            float previousX = X;
            float averageVelocity = (previousVelocity + VelocityX) * 0.5f;
            X = Math.Clamp(X + averageVelocity * seconds, minX, maxX);

            // Stop cleanly if we reached or crossed the pointer target during this step.
            if (targetX.HasValue && (targetX.Value - previousX) * (targetX.Value - X) <= 0f)
            {
                X = targetX.Value;
                VelocityX = 0f;
            }

            // Stop velocity when pressing against the left or right boundary.
            if ((X == minX && VelocityX < 0f) || (X == maxX && VelocityX > 0f))
            {
                VelocityX = 0f;
            }

            if (Math.Abs(VelocityX) > 1f)
            {
                Facing = Math.Sign(VelocityX);
            }
        }

        private static float MoveToward(float current, float target, float maxDelta)
        {
            if (current < target)
            {
                return Math.Min(current + maxDelta, target);
            }

            return Math.Max(current - maxDelta, target);
        }
    }
}

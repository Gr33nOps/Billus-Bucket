using System;

namespace BilluKiBalti
{
    public enum BilluAction
    {
        Straight,
        WalkRight,
        WalkLeft,
        Burned
    }

    /// <summary>
    /// Chooses Billu's current animation state and walk-cycle frame based on horizontal speed.
    /// </summary>
    public sealed class BilluAnimator
    {
        private const float WalkSpeedThreshold = 20f;
        private const float WalkFrameDuration = 0.11f;
        private const int WalkFrameCount = 4;

        private float stateTime;

        public BilluAction Action { get; private set; } = BilluAction.Straight;

        public int Frame
        {
            get
            {
                bool isWalking = Action == BilluAction.WalkRight || Action == BilluAction.WalkLeft;
                if (!isWalking)
                {
                    return 0;
                }

                return (int)(stateTime / WalkFrameDuration) % WalkFrameCount;
            }
        }

        public void Update(float seconds, float horizontalSpeed)
        {
            if (!float.IsFinite(seconds) || seconds <= 0f)
            {
                return;
            }

            // Once burned by a bomb, Billu stays burned until the run resets.
            if (Action == BilluAction.Burned)
            {
                stateTime += seconds;
                return;
            }

            BilluAction nextAction;
            if (Math.Abs(horizontalSpeed) <= WalkSpeedThreshold)
            {
                nextAction = BilluAction.Straight;
            }
            else if (horizontalSpeed > 0f)
            {
                nextAction = BilluAction.WalkRight;
            }
            else
            {
                nextAction = BilluAction.WalkLeft;
            }

            SetAction(nextAction);
            stateTime += seconds;
        }

        public void Burn()
        {
            SetAction(BilluAction.Burned);
        }

        public void Reset()
        {
            Action = BilluAction.Straight;
            stateTime = 0f;
        }

        private void SetAction(BilluAction nextAction)
        {
            if (Action == nextAction)
            {
                return;
            }

            Action = nextAction;
            stateTime = 0f;
        }
    }
}

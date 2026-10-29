using System;
using System.Numerics;

namespace BilluKiBalti
{
    /// <summary>
    /// Tracks the player's current cash, personal best, and game-over state for one session.
    /// Uses BigInteger so scores can grow without ever overflowing.
    /// </summary>
    public sealed class BilluKiBaltiRun
    {
        public BigInteger Cash { get; private set; }
        public BigInteger BestCash { get; private set; }
        public bool IsOver { get; private set; }

        public BilluKiBaltiRun(BigInteger initialBest = default)
        {
            BestCash = BigInteger.Max(BigInteger.Zero, initialBest);
        }

        public BigInteger CatchCash(int value = 10)
        {
            if (IsOver)
            {
                return BigInteger.Zero;
            }

            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Caught note value must be positive.");
            }

            Cash += value;
            if (Cash > BestCash)
            {
                BestCash = Cash;
            }

            return Cash;
        }

        /// <summary>
        /// Ends the run, clears current cash, and returns how much cash was earned in this run.
        /// </summary>
        public BigInteger HitBomb()
        {
            if (IsOver)
            {
                return BigInteger.Zero;
            }

            BigInteger earnedInRun = Cash;
            Cash = BigInteger.Zero;
            IsOver = true;
            return earnedInRun;
        }

        public void Restart()
        {
            Cash = BigInteger.Zero;
            IsOver = false;
        }
    }
}

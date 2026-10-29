using System;

namespace BillusBucket
{
    /// <summary>
    /// Stores all Pakistani Rupee note values and their spawn chances.
    /// The order matches the 7 note designs in Assets/rupee-notes.png.
    /// </summary>
    public static class NoteCatalog
    {
        public const int RollCount = 1000;
        public const int FramesPerNote = 4;

        // Values in Pakistani Rupees (Rs 10 up to Rs 5,000).
        private static readonly int[] Values = { 10, 20, 50, 100, 500, 1000, 5000 };

        // Spawn weights out of 1000 (25%, 23%, 20%, 17%, 9%, 5%, 1%).
        private static readonly int[] Weights = { 250, 230, 200, 170, 90, 50, 10 };

        public static int Count => Values.Length;

        public static int ValueAt(int index)
        {
            if (index < 0 || index >= Values.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return Values[index];
        }

        /// <summary>
        /// Picks a note index from a random roll between 0 and 999.
        /// Higher-value notes have smaller weights so they appear less often.
        /// </summary>
        public static int IndexForRoll(int roll)
        {
            if (roll < 0 || roll >= RollCount)
            {
                throw new ArgumentOutOfRangeException(nameof(roll));
            }

            int remainingRoll = roll;
            for (int index = 0; index < Weights.Length; index++)
            {
                if (remainingRoll < Weights[index])
                {
                    return index;
                }

                remainingRoll -= Weights[index];
            }

            throw new InvalidOperationException("Note weights must add up to RollCount.");
        }
    }
}

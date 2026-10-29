using System;
using System.Globalization;
using System.Numerics;

namespace BilluKiBalti
{
    /// <summary>
    /// Formats rupee amounts using compact Pakistani counting:
    /// K (thousand), L (lakh = 100,000), and Cr (crore = 10,000,000).
    /// Values are truncated rather than rounded up so we never show money not yet earned.
    /// </summary>
    public static class CashDisplay
    {
        public static string Compact(BigInteger amount)
        {
            if (amount < BigInteger.Zero)
            {
                return "-" + Compact(BigInteger.Abs(amount));
            }

            if (amount < 1_000)
            {
                return amount.ToString("N0", CultureInfo.InvariantCulture);
            }

            string digits = amount.ToString(CultureInfo.InvariantCulture);

            // For extremely huge numbers (10 billion+), switch to compact scientific notation.
            if (digits.Length > 10)
            {
                return FormatSignificantDigits(digits, wholeDigits: 1) + "E" + (digits.Length - 1);
            }

            int unitZeroCount;
            string unitSuffix;
            if (digits.Length >= 8)
            {
                // 1,00,00,000 and above -> Crores
                unitZeroCount = 7;
                unitSuffix = "Cr";
            }
            else if (digits.Length >= 6)
            {
                // 1,00,000 and above -> Lakhs
                unitZeroCount = 5;
                unitSuffix = "L";
            }
            else
            {
                // 1,000 and above -> Thousands
                unitZeroCount = 3;
                unitSuffix = "K";
            }

            int wholeDigits = digits.Length - unitZeroCount;
            return FormatSignificantDigits(digits, wholeDigits) + unitSuffix;
        }

        private static string FormatSignificantDigits(string digits, int wholeDigits)
        {
            string wholePart = digits.Substring(0, wholeDigits);
            int fractionalDigits = Math.Min(3 - wholeDigits, digits.Length - wholeDigits);
            if (fractionalDigits <= 0)
            {
                return wholePart;
            }

            string fractionPart = digits.Substring(wholeDigits, fractionalDigits).TrimEnd('0');
            if (fractionPart.Length == 0)
            {
                return wholePart;
            }

            return wholePart + "." + fractionPart;
        }
    }
}

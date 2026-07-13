// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
//
// - Sun Tsu,
// "The Art of War"

using System;
using System.Globalization;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Shared low-level scanner for the number/flag grammar used by SVG's <c>d</c> and <c>points</c>
    /// attributes: values may be separated by whitespace, a comma, or nothing at all (numbers can be
    /// glued together, e.g. "1.5.5" means "1.5 .5", and arc flags are exactly one digit and can be
    /// glued directly to the following number, e.g. "1 1 0 00 5 5" means flags "0" and "0" then "5 5").
    /// </summary>
    internal static class SvgNumberScanner
    {
        /// <summary>
        /// Advance past any run of whitespace and/or a single comma separator.
        /// </summary>
        public static void SkipSeparators(string s, ref int pos)
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos]))
                pos++;

            if (pos < s.Length && s[pos] == ',')
                pos++;

            while (pos < s.Length && char.IsWhiteSpace(s[pos]))
                pos++;
        }

        /// <summary>
        /// Try to read one floating point number starting at <paramref name="pos"/> (after skipping
        /// leading separators). On success, advances <paramref name="pos"/> past the number.
        /// </summary>
        public static bool TryReadNumber(string s, ref int pos, out double value)
        {
            SkipSeparators(s, ref pos);

            var start = pos;
            var i = pos;

            if (i < s.Length && (s[i] == '+' || s[i] == '-'))
                i++;

            var hasDigits = false;

            while (i < s.Length && char.IsAsciiDigit(s[i]))
            {
                i++;
                hasDigits = true;
            }

            if (i < s.Length && s[i] == '.')
            {
                i++;
                while (i < s.Length && char.IsAsciiDigit(s[i]))
                {
                    i++;
                    hasDigits = true;
                }
            }

            if (!hasDigits)
            {
                value = 0;
                pos = start;
                return false;
            }

            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                var expStart = i;
                var j = i + 1;

                if (j < s.Length && (s[j] == '+' || s[j] == '-'))
                    j++;

                var hasExpDigits = false;

                while (j < s.Length && char.IsAsciiDigit(s[j]))
                {
                    j++;
                    hasExpDigits = true;
                }

                if (hasExpDigits)
                    i = j;
                else
                    _ = expStart; // no valid exponent - leave i at the end of the mantissa
            }

            value = double.Parse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            pos = i;
            return true;
        }

        /// <summary>
        /// Try to read a single SVG path flag (exactly one character, '0' or '1') starting at
        /// <paramref name="pos"/> (after skipping leading separators).
        /// </summary>
        public static bool TryReadFlag(string s, ref int pos, out bool value)
        {
            SkipSeparators(s, ref pos);

            if (pos < s.Length && (s[pos] == '0' || s[pos] == '1'))
            {
                value = s[pos] == '1';
                pos++;
                return true;
            }

            value = false;
            return false;
        }
    }
}

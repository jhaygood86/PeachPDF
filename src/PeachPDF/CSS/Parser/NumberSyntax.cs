#nullable disable

using System;
using System.Globalization;

namespace PeachPDF.CSS
{
    /// <summary>
    /// A pure, allocation-free model of the &lt;number&gt; grammar this codebase's real <see cref="Lexer"/>
    /// actually implements via <c>NumberStart</c>/<c>NumberRest</c>/<c>NumberFraction</c> - deliberately
    /// <em>not</em> the full CSS Syntax Level 3 §4.3.13 grammar those methods are named after, since it
    /// leaves out the optional exponent (see remarks).
    /// </summary>
    /// <remarks>
    /// <see cref="Lexer"/>'s numeric-token state machine does correctly consume a trailing exponent
    /// ('e'/'E', optional sign, digits) via <c>NumberExponential</c>/<c>SciNotation</c> (issue #921).
    /// This fast path deliberately still doesn't: <c>CssValueParser.TryClassifyLengthFast</c>'s own
    /// post-number "is the remainder all letters" check already rejects any exponent shape (a digit
    /// follows the 'e'/'E', which isn't a letter) and falls through to its <c>Inconclusive</c> result -
    /// the real, full tokenizer - so duplicating exponent-consumption here would add complexity with no
    /// classification ever reaching it. Every exponent-bearing input was, and remains, classified
    /// <c>Inconclusive</c> before and after the Lexer fix; only the real tokenizer's own answer for
    /// those inputs changed.
    /// </remarks>
    internal static class NumberSyntax
    {
        /// <summary>
        /// Consumes a &lt;number&gt; - optional sign, then digits, optionally followed by a '.' and more
        /// digits - from the start of <paramref name="s"/>, matching exactly what
        /// <c>Lexer.NumberStart</c>/<c>NumberRest</c>/<c>NumberFraction</c> collect into their scratch
        /// buffer before ever looking at what follows (see the remarks above for why that excludes an
        /// exponent). On success, <paramref name="length"/> is how many UTF-16 code units were consumed
        /// and <paramref name="value"/> is the parsed value - parsed as <see langword="float"/> via
        /// <see cref="float.TryParse(ReadOnlySpan{char},NumberStyles,IFormatProvider,out float)"/>
        /// (invariant culture) and then widened to <see langword="double"/>, exactly how
        /// <see cref="UnitToken.Value"/> itself parses <c>Data</c> - not parsed directly as
        /// <see langword="double"/>, which can disagree with a widened <see langword="float"/> parse by a
        /// double-rounding ULP for some inputs. Returns <see langword="false"/> - leaving
        /// <paramref name="length"/>/<paramref name="value"/> at their defaults - when <paramref name="s"/>
        /// does not begin with a valid number at all (empty, or no digit anywhere in the mandatory
        /// integer-or-fraction part).
        /// </summary>
        public static bool TryConsumeNumber(ReadOnlySpan<char> s, out int length, out double value)
        {
            var i = 0;
            if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;

            var digitsStart = i;
            while (i < s.Length && s[i].IsDigit()) i++;
            var hasIntDigits = i > digitsStart;

            var hasFractionDigits = false;
            if (i < s.Length && s[i] == '.' && i + 1 < s.Length && s[i + 1].IsDigit())
            {
                i++;
                var fractionStart = i;
                while (i < s.Length && s[i].IsDigit()) i++;
                hasFractionDigits = i > fractionStart;
            }

            if (!hasIntDigits && !hasFractionDigits)
            {
                length = 0;
                value = 0;
                return false;
            }

            length = i;
            var parsed = float.TryParse(s[..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue);
            value = floatValue;
            return parsed;
        }
    }
}

#nullable disable

using System;
using System.Globalization;

namespace PeachPDF.CSS
{
    /// <summary>
    /// A pure, allocation-free model of the &lt;number&gt; grammar this codebase's real <see cref="Lexer"/>
    /// actually implements via <c>NumberStart</c>/<c>NumberRest</c>/<c>NumberFraction</c> - deliberately
    /// <em>not</em> the CSS Syntax Level 3 §4.3.13 grammar those methods are named after.
    /// </summary>
    /// <remarks>
    /// <see cref="Lexer"/>'s numeric-token state machine never actually reaches its own exponent-handling
    /// code (<c>NumberExponential</c>/<c>SciNotation</c>): <c>NumberRest</c>/<c>NumberFraction</c>'s main
    /// loop treats <em>any</em> <see cref="CharExtensions.IsNameStart"/> character - which includes 'e'/'E',
    /// indistinguishable from any other unit-starting letter - as the start of a unit-ident token
    /// <em>before</em> the switch statement housing the 'e'/'E' exponent cases is ever reached. So e.g.
    /// <c>"1e2"</c> tokenizes today as two tokens (a Dimension token "1e" followed by a separate Number
    /// token "2"), never as the single scientific-notation Number(100) CSS Syntax 3 describes. This is a
    /// pre-existing, real spec-compliance gap, unrelated to this method's purpose - it is modeled here
    /// exactly as-is (no exponent consumption) so this faster path always agrees with what callers
    /// already get from <c>CssValueParser.GetUnit</c>'s full-tokenizer path today, rather than "fixing"
    /// the number grammar as an incidental side effect.
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

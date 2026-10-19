#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeachBrackets.Css
{
    /// <summary>
    /// The arguments of the <c>linear()</c> easing function (CSS Easing 2 §2.1): a comma-separated list of at least two
    /// stops, each a <c>&lt;number&gt;</c> output with up to two <c>&lt;percentage&gt;</c> input positions, in either order.
    /// Used by both <see cref="Converters.LinearEasingConverter"/> (CSS-OM validation) and
    /// <c>PeachBrackets.Html.Animation.EasingFunction</c> (which turns the stops into the curve), so the two cannot disagree
    /// about what is a valid <c>linear()</c> - the same arrangement as <see cref="BackgroundPositionGrammar"/>.
    /// </summary>
    internal static class LinearEasingGrammar
    {
        /// <summary>One authored stop. The positions are fractions (<c>25%</c> is 0.25); both null when none was written.</summary>
        internal readonly struct Stop
        {
            public Stop(double output, double? firstPosition, double? secondPosition)
            {
                Output = output;
                FirstPosition = firstPosition;
                SecondPosition = secondPosition;
            }

            public double Output { get; }

            public double? FirstPosition { get; }

            /// <summary>Set only when the stop was written with two positions, which makes it two points of the same output.</summary>
            public double? SecondPosition { get; }
        }

        /// <summary>
        /// Reads the stops out of the tokens between the parentheses (whitespace tokens are ignored). Returns null when
        /// the arguments are not a valid stop list: fewer than two stops, a stop without a number or with a second one,
        /// more than two percentages, a dangling comma, or a number that is not finite.
        /// </summary>
        public static List<Stop>? TryParse(IReadOnlyList<Token> tokens)
        {
            var stops = new List<Stop>();
            double? output = null;
            double? first = null, second = null;
            var positions = 0;

            foreach (var token in tokens)
            {
                switch (token.Type)
                {
                    case TokenType.Whitespace:
                        break;

                    case TokenType.Number when output is null && TryRead(token, out var number):
                        output = number;
                        break;

                    case TokenType.Percentage when positions < 2 && TryRead(token, out var percent):
                        if (positions++ == 0) first = percent / 100; else second = percent / 100;
                        break;

                    case TokenType.Comma when output is not null:
                        stops.Add(new Stop(output.Value, first, second));
                        output = first = second = null;
                        positions = 0;
                        break;

                    default:
                        return null;
                }
            }

            // The last stop counts too (a trailing comma leaves nothing to add, which is the dangling-comma case).
            if (output is null) return null;
            stops.Add(new Stop(output.Value, first, second));

            return stops.Count >= 2 ? stops : null;
        }

        // Finite only: an overflowing "1e999" is a number token, but not one a curve can use.
        private static bool TryRead(Token token, out double value) =>
            double.TryParse(token.Data, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }
}

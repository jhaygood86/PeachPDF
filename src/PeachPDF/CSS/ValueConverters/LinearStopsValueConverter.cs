#nullable disable

using System.Collections.Generic;

namespace PeachPDF.CSS
{
    /// <summary>
    /// The arguments of the <c>linear()</c> easing function (CSS Easing 2 §2.2): a comma-separated list of stops, each a
    /// <c>&lt;number&gt;</c> optionally followed by one or two <c>&lt;percentage&gt;</c> input positions. Only the shape is
    /// checked; the stops themselves are not modelled (the renderer reads <c>linear()</c> as plain <c>linear</c>).
    /// </summary>
    internal sealed class LinearStopsValueConverter : IValueConverter
    {
        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            // <linear-easing-point> = <number> && <percentage>{0,2}: one number and up to two percentages, in any order.
            var stops = 0;
            var numbers = 0;
            var positions = 0;

            foreach (var token in value)
            {
                switch (token.Type)
                {
                    case TokenType.Whitespace:
                        break;
                    case TokenType.Number when ++numbers == 1:
                        break;
                    case TokenType.Percentage when ++positions <= 2:
                        break;
                    case TokenType.Comma when numbers == 1:
                        stops++;
                        numbers = positions = 0;
                        break;
                    default:
                        return null;
                }
            }

            // The last stop counts too (no dangling comma), and there are at least two.
            if (numbers != 1) return null;
            stops++;

            return stops >= 2 ? new StopsValue(value) : null;
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<StopsValue>();
        }

        private sealed class StopsValue : IPropertyValue
        {
            public StopsValue(IEnumerable<Token> tokens)
            {
                Original = new TokenValue(tokens);
            }

            public string CssText => Original.ToText();

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name)
            {
                return Original;
            }
        }
    }
}

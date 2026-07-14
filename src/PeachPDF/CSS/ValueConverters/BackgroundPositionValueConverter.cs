#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Validates and serializes a single-layer <c>background-position</c> value via the shared
    /// <see cref="BackgroundPositionGrammar"/> (see that class for why this isn't a hand-written
    /// combinator tree like most other converters in this file).
    /// </summary>
    internal sealed class BackgroundPositionValueConverter : IValueConverter
    {
        public IPropertyValue Convert(IEnumerable<Token> value)
        {
            var tokens = value.Where(t => t.Type != TokenType.Whitespace).ToArray();
            var parsed = BackgroundPositionGrammar.TryParse(tokens);
            return parsed != null ? new BackgroundPositionValue(parsed, value) : null;
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<BackgroundPositionValue>();
        }

        private sealed class BackgroundPositionValue : IPropertyValue
        {
            private readonly BackgroundPositionGrammar.ParsedPosition _parsed;

            public BackgroundPositionValue(BackgroundPositionGrammar.ParsedPosition parsed, IEnumerable<Token> tokens)
            {
                _parsed = parsed;
                Original = new TokenValue(tokens);
            }

            public string CssText => string.Join(" ", _parsed.AuthoredOrder.Select(ComponentText));

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;

            private static string ComponentText(BackgroundPositionGrammar.Component c)
            {
                if (c.Keyword == BackgroundPositionGrammar.AxisKeyword.None)
                    return LengthText(c.Offset);

                var keyword = KeywordText(c.Keyword);
                return c.Offset != null ? keyword + " " + LengthText(c.Offset) : keyword;
            }

            private static string KeywordText(BackgroundPositionGrammar.AxisKeyword keyword) => keyword switch
            {
                BackgroundPositionGrammar.AxisKeyword.Left => Keywords.Left,
                BackgroundPositionGrammar.AxisKeyword.Right => Keywords.Right,
                BackgroundPositionGrammar.AxisKeyword.Top => Keywords.Top,
                BackgroundPositionGrammar.AxisKeyword.Bottom => Keywords.Bottom,
                BackgroundPositionGrammar.AxisKeyword.Center => Keywords.Center,
                _ => string.Empty,
            };

            private static string LengthText(Token token) =>
                Converters.LengthOrPercentConverter.Convert(new[] { token })?.CssText ?? string.Empty;
        }
    }
}

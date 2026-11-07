#nullable disable

using System.Collections.Generic;

namespace PeachBrackets.Css
{
    /// <summary>
    /// The arguments of <c>light-dark(&lt;color&gt;, &lt;color&gt;)</c> (CSS Color 5 section 4): exactly two comma-separated colours. A declaration
    /// whose arms are not both colours is invalid, as it is in a browser, rather than a colour that resolves to nothing.
    /// </summary>
    internal sealed class ColorPairValueConverter : IValueConverter
    {
        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var arms = new List<List<Token>> { new() };
            foreach (var token in value)
            {
                if (token.Type == TokenType.Comma) arms.Add(new List<Token>());
                else arms[^1].Add(token);
            }

            if (arms.Count != 2) return null;

            foreach (var arm in arms)
            {
                arm.RemoveAll(static t => t.Type == TokenType.Whitespace);
                if (arm.Count == 0 || Converters.ColorConverter.Convert(arm) is null)
                    return null;
            }

            return new PairValue(value);
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<PairValue>();
        }

        private sealed class PairValue : IPropertyValue
        {
            public PairValue(IEnumerable<Token> tokens)
            {
                Original = new TokenValue(tokens);
            }

            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;
        }
    }
}

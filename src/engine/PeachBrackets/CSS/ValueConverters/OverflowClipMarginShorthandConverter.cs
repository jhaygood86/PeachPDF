#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>
    /// The <c>overflow-clip-margin</c>, <c>-inline</c> and <c>-block</c> shorthands (css-overflow-4 §3.2): one <c>&lt;visual-box&gt; || &lt;length&gt;</c>, which
    /// each longhand the shorthand stands for takes whole.
    /// </summary>
    internal sealed class OverflowClipMarginShorthandConverter(params string[] longhands) : IValueConverter
    {
        public IPropertyValue Convert(IReadOnlyList<Token> value) =>
            OverflowClipMarginGrammar.TryParse(value) ? new ShorthandValue(longhands, new TokenValue(value)) : null;

        public IPropertyValue Construct(Property[] properties)
        {
            if (properties.Length != longhands.Length) return null;

            var first = properties[0].DeclaredValue;
            if (first is null || properties.Any(p => p.DeclaredValue is null || p.DeclaredValue.CssText != first.CssText)) return null;

            return new ShorthandValue(longhands, new TokenValue(first.Original));
        }

        private sealed class ShorthandValue(string[] names, TokenValue original) : IPropertyValue
        {
            public string CssText => original.ToText();

            public TokenValue Original => original;

            public TokenValue ExtractFor(string name) => names.Any(n => name.Is(n)) ? original : TokenValue.Empty;
        }
    }
}

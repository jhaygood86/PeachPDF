#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>
    /// Validates <c>color-scheme</c> (<c>normal | [ light | dark | &lt;custom-ident&gt; ]+ &amp;&amp; only?</c>) through the one shared
    /// <see cref="ColorScheme.TryParse(IReadOnlyList{Token})"/> grammar the render layer also reads it with.
    /// </summary>
    internal sealed class ColorSchemeValueConverter : IValueConverter
    {
        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var tokens = value.Where(t => t.Type != TokenType.Whitespace).ToArray();
            return ColorScheme.TryParse(tokens) is not null ? new ColorSchemeValue(value) : null;
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<ColorSchemeValue>();
        }

        private sealed class ColorSchemeValue : IPropertyValue
        {
            public ColorSchemeValue(IEnumerable<Token> tokens)
            {
                Original = new TokenValue(tokens);
            }

            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;
        }
    }
}

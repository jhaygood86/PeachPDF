#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>
    /// Validates <c>image-resolution</c> (<c>[ from-image || &lt;resolution&gt; ] &amp;&amp; snap?</c>) through the one shared
    /// <see cref="ImageResolution.TryParse(IReadOnlyList{Token})"/> grammar the render layer also reads it with.
    /// </summary>
    internal sealed class ImageResolutionValueConverter : IValueConverter
    {
        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var tokens = value.Where(t => t.Type != TokenType.Whitespace).ToArray();
            return ImageResolution.TryParse(tokens) is not null ? new ImageResolutionValue(value) : null;
        }

        public IPropertyValue Construct(Property[] properties) => properties.Guard<ImageResolutionValue>();

        private sealed class ImageResolutionValue : IPropertyValue
        {
            public ImageResolutionValue(IEnumerable<Token> tokens)
            {
                Original = new TokenValue(tokens);
            }

            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;
        }
    }
}

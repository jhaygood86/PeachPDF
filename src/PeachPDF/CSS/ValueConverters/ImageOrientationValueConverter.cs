#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Validates <c>image-orientation</c> (<c>from-image | none | [ &lt;angle&gt; || flip ]</c>) through the one shared
    /// <see cref="ImageOrientation.TryParse(IReadOnlyList{Token})"/> grammar the render layer also reads it with.
    /// </summary>
    internal sealed class ImageOrientationValueConverter : IValueConverter
    {
        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var tokens = value.Where(t => t.Type != TokenType.Whitespace).ToArray();
            return ImageOrientation.TryParse(tokens) is not null ? new ImageOrientationValue(value) : null;
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<ImageOrientationValue>();
        }

        private sealed class ImageOrientationValue : IPropertyValue
        {
            public ImageOrientationValue(IEnumerable<Token> tokens)
            {
                Original = new TokenValue(tokens);
            }

            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;
        }
    }
}

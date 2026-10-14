#nullable disable

using System.Collections.Generic;

namespace PeachPDF.CSS
{
    /// <summary>
    /// The arguments of the <c>linear()</c> easing function (CSS Easing 2 §2.1). The grammar itself is
    /// <see cref="LinearEasingGrammar"/>, shared with the render layer's <c>EasingFunction</c>; this only validates against it.
    /// </summary>
    internal sealed class LinearStopsValueConverter : IValueConverter
    {
        public IPropertyValue Convert(IReadOnlyList<Token> value) =>
            LinearEasingGrammar.TryParse(value) is null ? null : new StopsValue(value);

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

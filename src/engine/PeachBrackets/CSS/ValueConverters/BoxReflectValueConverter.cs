#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>Validates a <c>-webkit-box-reflect</c> value through the shared <see cref="BoxReflectGrammar"/>, keeping the authored text.</summary>
    internal sealed class BoxReflectValueConverter : IValueConverter
    {
        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var tokens = value.ToArray();
            var text = new TokenValue(tokens).Text;
            return BoxReflectGrammar.TryParse(text, out _, out _, out _, out _) ? new BoxReflectValue(tokens) : null;
        }

        public IPropertyValue Construct(Property[] properties) => properties.Guard<BoxReflectValue>();

        private sealed class BoxReflectValue : IPropertyValue
        {
            public BoxReflectValue(IEnumerable<Token> tokens) => Original = new TokenValue(tokens);

            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;
        }
    }
}

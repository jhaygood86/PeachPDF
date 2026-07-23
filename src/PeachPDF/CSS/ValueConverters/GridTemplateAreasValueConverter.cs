#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Validates a <c>grid-template-areas</c> value: the keyword <c>none</c> or a rectangular grid of quoted
    /// strings accepted by the shared <see cref="GridTemplateAreasGrammar"/> (equal columns per row, each
    /// named area a single filled rectangle). The authored text is preserved so the grid layout engine
    /// re-parses the same value.
    /// </summary>
    internal sealed class GridTemplateAreasValueConverter : IValueConverter
    {
        public IPropertyValue Convert(IEnumerable<Token> value)
        {
            var tokens = value.Where(t => t.Type != TokenType.Whitespace).ToArray();

            var isNone = tokens is [{ Type: TokenType.Ident } ident] && ident.Data.Isi(Keywords.None);

            if (!isNone && GridTemplateAreasGrammar.TryParse(tokens) is null)
                return null;

            return new GridTemplateAreasValue(value);
        }

        public IPropertyValue Construct(Property[] properties) => properties.Guard<GridTemplateAreasValue>();

        private sealed class GridTemplateAreasValue : IPropertyValue
        {
            public GridTemplateAreasValue(IEnumerable<Token> tokens)
            {
                Original = new TokenValue(tokens);
            }

            public string CssText => Original.Text;
            public TokenValue Original { get; }
            public TokenValue ExtractFor(string name) => Original;
        }
    }
}

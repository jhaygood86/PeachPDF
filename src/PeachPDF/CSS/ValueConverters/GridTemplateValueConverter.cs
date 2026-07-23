#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Validates a <c>grid-template-columns</c>/<c>grid-template-rows</c> value: accepts the keyword
    /// <c>none</c> or any <c>&lt;track-list&gt;</c> the shared <see cref="GridTrackListGrammar"/> recognizes
    /// (lengths, %, <c>fr</c>, <c>auto</c>, <c>min-content</c>/<c>max-content</c>, <c>minmax()</c>,
    /// <c>fit-content()</c>, <c>repeat()</c>), rejecting everything else so an invalid template is dropped
    /// at parse time. The authored text is preserved verbatim so the grid layout engine (Layer B)
    /// re-parses the same value.
    /// </summary>
    internal sealed class GridTemplateValueConverter : IValueConverter
    {
        public IPropertyValue Convert(IEnumerable<Token> value)
        {
            var tokens = value.Where(t => t.Type != TokenType.Whitespace).ToArray();

            var isNone = tokens is [{ Type: TokenType.Ident } ident] && ident.Data.Isi(Keywords.None);

            if (!isNone && GridTrackListGrammar.TryParse(tokens) is null)
                return null;

            return new GridTemplateValue(value);
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<GridTemplateValue>();
        }

        private sealed class GridTemplateValue : IPropertyValue
        {
            public GridTemplateValue(IEnumerable<Token> tokens)
            {
                Original = new TokenValue(tokens);
            }

            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;
        }
    }
}

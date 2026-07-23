#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Validates a <c>grid-template-columns</c>/<c>grid-template-rows</c> value: accepts the keyword
    /// <c>none</c>, the <c>subgrid</c> keyword, or any <c>&lt;track-list&gt;</c> the shared
    /// <see cref="GridTrackListGrammar"/> recognizes (lengths, %, <c>fr</c>, <c>auto</c>,
    /// <c>min-content</c>/<c>max-content</c>, <c>minmax()</c>, <c>fit-content()</c>, <c>repeat()</c>),
    /// rejecting everything else so an invalid template is dropped at parse time. The parse produced here to
    /// validate is <b>kept</b> and exposed as a <see cref="CssProperty{T}"/> via
    /// <see cref="ITypedPropertyValue{T}"/>, so the grid layout engine (Layer B) reads the already-parsed
    /// template instead of re-parsing the same string.
    /// </summary>
    internal sealed class GridTemplateValueConverter : IValueConverter
    {
        public IPropertyValue Convert(IEnumerable<Token> value)
        {
            var tokens = value.Where(t => t.Type != TokenType.Whitespace).ToArray();

            var isNone = tokens is [{ Type: TokenType.Ident } ident] && ident.Data.Isi(Keywords.None);

            GridTemplate template = null;
            if (!isNone)
            {
                template = GridTrackListGrammar.TryParse(tokens);
                if (template is null) return null;
            }

            return new GridTemplateValue(value, template);
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<GridTemplateValue>();
        }

        private sealed class GridTemplateValue : IPropertyValue, ITypedPropertyValue<GridTemplate>
        {
            private readonly CssProperty<GridTemplate> _typed;

            public GridTemplateValue(IEnumerable<Token> tokens, GridTemplate template)
            {
                Original = new TokenValue(tokens);
                // template is null for the `none` keyword — a resolved value with a null GridTemplate, which
                // the engine treats as an empty (no explicit tracks) grid, exactly like the string path.
                _typed = CssProperty<GridTemplate>.FromValue(Original.Text, template);
            }

            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;

            public CssProperty<GridTemplate> GetTypedValue() => _typed;
        }
    }
}

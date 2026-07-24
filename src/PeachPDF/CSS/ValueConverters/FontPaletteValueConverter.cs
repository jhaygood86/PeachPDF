#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Validates a <c>font-palette</c> value (CSS Fonts Module Level 4): the keywords <c>normal</c>/
    /// <c>light</c>/<c>dark</c>, a <c>&lt;dashed-ident&gt;</c> naming an <c>@font-palette-values</c> rule, or a
    /// <c>palette-mix()</c> function (validated through the shared <see cref="PaletteMixGrammar"/>). Anything
    /// else is dropped at parse time. The authored value text is preserved verbatim (Layer B re-tokenizes and
    /// resolves it in <c>ActualFontPalette</c>), mirroring <see cref="ClipPathValueConverter"/>.
    /// </summary>
    internal sealed class FontPaletteValueConverter : IValueConverter
    {
        public IPropertyValue Convert(IEnumerable<Token> value)
        {
            var tokens = value.Where(t => t.Type != TokenType.Whitespace).ToArray();

            bool valid = tokens switch
            {
                [{ Type: TokenType.Ident } ident] => IsKeywordOrDashedIdent(ident.Data),
                _ => PaletteMixGrammar.TryParse(tokens) is not null,
            };

            return valid ? new FontPaletteValue(value) : null;
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<FontPaletteValue>();
        }

        private static bool IsKeywordOrDashedIdent(string ident) =>
            ident.Isi(Keywords.Normal) || ident.Isi("light") || ident.Isi("dark") ||
            ident.StartsWith("--", System.StringComparison.Ordinal);

        private sealed class FontPaletteValue : IPropertyValue
        {
            public FontPaletteValue(IEnumerable<Token> tokens)
            {
                Original = new TokenValue(tokens);
            }

            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;
        }
    }
}

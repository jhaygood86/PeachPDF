#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// CSS Fonts 4 §6.6 <c>font-language-override</c>: <c>normal | &lt;string&gt;</c>. Inherited. The authored text is kept;
    /// <see cref="FontLanguageOverrideGrammar.Resolve"/> turns it into an OpenType language-system tag where the text is shaped.
    /// </summary>
    internal sealed class FontLanguageOverrideProperty : Property
    {
        // Exposed for css-properties.json's "cssom-grammar" validator.
        internal static readonly IValueConverter ValueGrammar = new LanguageOverrideValueConverter();

        private static readonly IValueConverter StyleConverter = ValueGrammar.OrDefault();

        internal FontLanguageOverrideProperty()
            : base(PropertyNames.FontLanguageOverride, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;

        private sealed class LanguageOverrideValueConverter : IValueConverter
        {
            public IPropertyValue Convert(IReadOnlyList<Token> value)
            {
                var tokens = value.ToArray();
                return FontLanguageOverrideGrammar.TryParse(tokens, out _) ? new OverrideValue(tokens) : null;
            }

            public IPropertyValue Construct(Property[] properties) => properties.Guard<OverrideValue>();

            private sealed class OverrideValue : IPropertyValue
            {
                public OverrideValue(IEnumerable<Token> tokens) => Original = new TokenValue(tokens);

                public string CssText => Original.Text;

                public TokenValue Original { get; }

                public TokenValue ExtractFor(string name) => Original;
            }
        }
    }

    /// <summary>
    /// The shared grammar for <c>font-language-override</c>: validates at parse time (CSS-OM) and resolves the authored text to the
    /// OpenType language-system tag at shaping time, so the two layers cannot disagree.
    /// </summary>
    internal static class FontLanguageOverrideGrammar
    {
        /// <summary>
        /// Parses a value. Returns false when invalid. On success <paramref name="tag"/> is null for <c>normal</c>; otherwise the
        /// string's one to four printable ASCII characters, padded with spaces to the four characters of an OpenType tag.
        /// </summary>
        internal static bool TryParse(IReadOnlyList<Token> tokens, out string tag)
        {
            tag = null;
            var toks = tokens.Where(t => t.Type != TokenType.Whitespace).ToArray();
            if (toks.Length != 1) return false;

            if (toks[0] is { Type: TokenType.Ident } ident)
                return ident.Data.Isi(Keywords.Normal);

            if (toks[0] is not { Type: TokenType.String } text) return false;

            var data = text.Data.ToString();
            if (data.Length is < 1 or > 4 || data.Any(c => c < 0x20 || c > 0x7E)) return false;

            tag = data.PadRight(4);
            return true;
        }

        /// <summary>The language-system tag an authored <c>font-language-override</c> asks for, or null for <c>normal</c>, nothing, or an invalid value.</summary>
        internal static string Resolve(string authored)
        {
            if (string.IsNullOrWhiteSpace(authored)) return null;

            using var pooled = Html.Core.Parse.CssValueParser.GetCssTokensPooled(authored.Trim());
            List<Token> tokens = pooled;
            return TryParse(tokens, out var tag) ? tag : null;
        }
    }
}

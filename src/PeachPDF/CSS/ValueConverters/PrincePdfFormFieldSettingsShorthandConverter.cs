#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Hidden compat parser for Prince's own <c>-prince-pdf-form-field-settings</c> extension, which
    /// this repo has no formal published grammar reference for (see
    /// -prince-pdf-form-field-settings's own css-properties.json entry) - reconstructed from the
    /// issue thread's prose description: <c>none | [ &lt;field-type&gt; || auto-font-size ||
    /// comb(&lt;integer&gt;) || do-not-scroll ]</c>, where &lt;field-type&gt; is any of Prince's own
    /// HTML input-type-ish keywords, each collapsed to the nearest PeachPDF field kind (every
    /// text-like keyword becomes "text" - PeachPDF only distinguishes text/checkbox/radio/select).
    /// Expands into the four -peachpdf-pdf-form-field* longhands via <see cref="ShorthandProperty.Export"/>:
    /// a sub-option this declaration didn't mention extracts as an empty <see cref="TokenValue"/>,
    /// which <see cref="ShorthandProperty.Export"/> already treats as "omitted" and resets to that
    /// longhand's own initial value, so this converter only needs to emit tokens for what was
    /// actually written.
    /// </summary>
    internal sealed class PrincePdfFormFieldSettingsShorthandConverter : IValueConverter
    {
        public IPropertyValue Convert(IEnumerable<Token> value)
        {
            var tokens = value as Token[] ?? value.ToArray();
            var items = tokens.ToItems();

            if (items.Count == 1 && items[0] is [{ Type: TokenType.Ident } soleIdent] && soleIdent.Data.Is(Keywords.None))
            {
                return new PrincePdfFormFieldSettingsValue(Keywords.None, false, null, false, new TokenValue(tokens));
            }

            string kind = null;
            var autoFontSize = false;
            var doNotScroll = false;
            int? comb = null;

            foreach (var item in items)
            {
                if (item.Count == 0) continue;

                if (item is [{ Type: TokenType.Ident } ident])
                {
                    if (ident.Data.Is(Keywords.AutoFontSize))
                    {
                        if (autoFontSize) return null;
                        autoFontSize = true;
                        continue;
                    }

                    if (ident.Data.Is(Keywords.DoNotScroll))
                    {
                        if (doNotScroll) return null;
                        doNotScroll = true;
                        continue;
                    }

                    var resolved = ResolveFieldTypeKeyword(ident.Data);
                    if (resolved is null || kind != null) return null;
                    kind = resolved;
                    continue;
                }

                if (item is [FunctionToken fn] && fn.Data.Is(Keywords.Comb))
                {
                    if (comb != null) return null;

                    var args = fn.ArgumentTokens.Where(t => t.Type != TokenType.Whitespace).ToArray();
                    if (args is not [{ Type: TokenType.Number } number] || !int.TryParse(number.Data, out var cells) || cells <= 0)
                        return null;

                    comb = cells;
                    continue;
                }

                return null;
            }

            // auto-font-size/comb/do-not-scroll are all text-field-only concepts - their presence
            // with no explicit field-type keyword still names a text field, same as Prince's own
            // shorthand implies.
            kind ??= Keywords.Text;

            return new PrincePdfFormFieldSettingsValue(kind, autoFontSize, comb, doNotScroll, new TokenValue(tokens));
        }

        /// <summary>
        /// Prince's own field-type keyword set is richer than PeachPDF's (text/checkbox/radio/select) -
        /// every text-like HTML input type collapses to "text", matching FormFieldMapper's own "closest
        /// equivalent" collapsing for auto-inferred &lt;input type=&gt; values.
        /// </summary>
        static string ResolveFieldTypeKeyword(string keyword) => keyword switch
        {
            _ when keyword.Is(Keywords.Auto) => Keywords.Auto,
            _ when keyword.Is(Keywords.Checkbox) => Keywords.Checkbox,
            _ when keyword.Is(Keywords.Radio) => Keywords.Radio,
            _ when keyword.Is(Keywords.Select) => Keywords.Select,
            _ when keyword.Is(Keywords.Text) => Keywords.Text,
            _ when keyword.Is(Keywords.Password) => Keywords.Text,
            _ when keyword.Is(Keywords.Email) => Keywords.Text,
            _ => null
        };

        public IPropertyValue Construct(Property[] properties) => null;

        sealed class PrincePdfFormFieldSettingsValue(string kind, bool autoFontSize, int? comb, bool doNotScroll, TokenValue original) : IPropertyValue
        {
            public TokenValue Original { get; } = original;

            public string CssText => Original.Text;

            public TokenValue ExtractFor(string name)
            {
                if (name.Is(PropertyNames.PdfFormField))
                    return new TokenValue([new Token(TokenType.Ident, kind, TextPosition.Empty)]);

                if (name.Is(PropertyNames.PdfFormFieldAutoFontSize))
                    return autoFontSize ? new TokenValue([new Token(TokenType.Ident, Keywords.Auto, TextPosition.Empty)]) : TokenValue.Empty;

                if (name.Is(PropertyNames.PdfFormFieldDoNotScroll))
                    return doNotScroll ? new TokenValue([new Token(TokenType.Ident, Keywords.Auto, TextPosition.Empty)]) : TokenValue.Empty;

                if (name.Is(PropertyNames.PdfFormFieldComb))
                    return comb is { } cells ? new TokenValue([new NumberToken(cells.ToString(), TextPosition.Empty)]) : TokenValue.Empty;

                return TokenValue.Empty;
            }
        }
    }
}

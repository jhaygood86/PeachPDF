#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    using static Converters;

    /// <summary>
    /// Parses the argument list of Prince's proprietary <c>prince-opentype(tag[, tag ...])</c>
    /// function - a bare-identifier-tagged variant of the standard <c>font-feature-settings</c>
    /// grammar - and decomposes it at parse time into whichever of this codebase's real standard
    /// longhands the tags correspond to (<c>font-variant-caps</c>, <c>font-variant-ligatures</c>,
    /// <c>font-variant-numeric</c>, <c>font-variant-east-asian</c>, <c>font-feature-settings</c>).
    /// Nothing downstream of this parse ever sees <c>prince-opentype</c> again - the computed/
    /// serialized value of every longhand it can touch is always one of the standard forms. This is
    /// an accepted, deliberately undocumented authoring-compatibility shim (see
    /// .claude/accepted-gaps/prince-opentype-partial-decomposition.md) - never described in any
    /// user-facing documentation.
    /// </summary>
    internal sealed class PrinceOpenTypeConverter : IValueConverter
    {
        // Deliberately not built on the generic FunctionValueConverter: that wrapper's own
        // IPropertyValue.ExtractFor(name) always returns the whole raw token stream regardless of
        // `name` (correct for FunctionValueConverter's usual case - a function that feeds exactly one
        // property, e.g. cubic-bezier() for transition-timing-function), which would discard this
        // converter's per-longhand decomposition and make every one of the 5 covered longhands fail to
        // parse the raw "prince-opentype(...)" tokens it can't itself understand. The function-name
        // match is done directly here instead, so the returned PrinceOpenTypeValue - whose own
        // ExtractFor already knows how to answer per longhand - is never re-wrapped.
        private static readonly IValueConverter ItemConverter =
            WithOrder(IdentifierConverter.Required(), FontFeatureTagListGrammar.FeatureValueConverter.Option());

        private static readonly IValueConverter ListConverter = ItemConverter.FromList();

        // The 6 real font-variant-caps keyword <-> exact-tag-set correspondences (see
        // GsubShaper.GetFeatureTags, which this deliberately doesn't reference - CSS-OM stays
        // independent of the PeachPDF.Text shaping layer, so this small, spec-fixed table is
        // duplicated rather than shared across that layering boundary).
        private static readonly (string Keyword, string[] Tags)[] CapsCombinations =
        [
            (Keywords.AllSmallCaps, ["smcp", "c2sc"]),
            (Keywords.AllPetiteCaps, ["pcap", "c2pc"]),
            (Keywords.SmallCaps, ["smcp"]),
            (Keywords.PetiteCaps, ["pcap"]),
            (Keywords.Unicase, ["unic"]),
            (Keywords.TitlingCaps, ["titl"])
        ];

        private static readonly HashSet<string> CapsTags = new(StringComparer.OrdinalIgnoreCase)
            { "smcp", "c2sc", "pcap", "c2pc", "unic", "titl" };

        private static readonly IReadOnlyDictionary<string, string> LigatureTagToKeyword =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["liga"] = Keywords.CommonLigatures,
                ["clig"] = Keywords.CommonLigatures,
                ["dlig"] = Keywords.DiscretionaryLigatures,
                ["hlig"] = Keywords.HistoricalLigatures
            };

        private static readonly IReadOnlyDictionary<string, string> NumericTagToKeyword =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["lnum"] = Keywords.LiningNums,
                ["onum"] = Keywords.OldstyleNums,
                ["pnum"] = Keywords.ProportionalNums,
                ["tnum"] = Keywords.TabularNums,
                ["frac"] = Keywords.DiagonalFractions,
                ["afrc"] = Keywords.StackedFractions,
                ["ordn"] = Keywords.Ordinal,
                ["zero"] = Keywords.SlashedZero
            };

        private static readonly IReadOnlyDictionary<string, string> EastAsianTagToKeyword =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["jp78"] = Keywords.Jis78Forms,
                ["jp83"] = Keywords.Jis83Forms,
                ["jp90"] = Keywords.Jis90Forms,
                ["jp04"] = Keywords.Jis04Forms,
                ["smpl"] = Keywords.Simplified,
                ["trad"] = Keywords.Traditional,
                ["fwid"] = Keywords.FullWidth,
                ["pwid"] = Keywords.ProportionalWidth,
                ["ruby"] = Keywords.Ruby
            };

        public IPropertyValue Convert(IEnumerable<Token> value)
        {
            if (value.OnlyOrDefault() is not FunctionToken function
                || !function.Data.Equals(FunctionNames.PrinceOpenType, StringComparison.OrdinalIgnoreCase))
                return null;

            var parsed = ListConverter.Convert(function.ArgumentTokens);
            if (parsed is null) return null;

            // Re-split the converter's own normalized ", "-joined text into "tag" / "tag value" items
            // - the same re-parse-your-own-CssText technique FontVariantLigaturesValueConverter.IsValid
            // already uses, rather than re-deriving raw-token traversal independently here.
            var items = parsed.CssText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var capsTagsPresent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ligatureKeywords = new List<string>();
            var numericKeywords = new List<string>();
            var eastAsianKeywords = new List<string>();
            var explicitFeatures = new List<(string Tag, string Value)>();

            foreach (var item in items)
            {
                var parts = item.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                var tag = parts[0];
                var rawValue = parts.Length > 1 ? parts[1] : null;

                if (CapsTags.Contains(tag)) { capsTagsPresent.Add(tag.ToLowerInvariant()); continue; }
                if (LigatureTagToKeyword.TryGetValue(tag, out var ligKeyword)) { ligatureKeywords.Add(ligKeyword); continue; }
                if (NumericTagToKeyword.TryGetValue(tag, out var numKeyword)) { numericKeywords.Add(numKeyword); continue; }
                if (EastAsianTagToKeyword.TryGetValue(tag, out var eaKeyword)) { eastAsianKeywords.Add(eaKeyword); continue; }

                explicitFeatures.Add((tag, rawValue));
            }

            string capsKeyword = null;
            foreach (var (keyword, tags) in CapsCombinations)
            {
                if (capsTagsPresent.SetEquals(tags)) { capsKeyword = keyword; break; }
            }
            // A caps tag set matching none of the 6 defined combinations (e.g. "c2sc" alone, or a
            // cross-family mix) has no CSS representation - those tags are simply dropped, not passed
            // through to font-feature-settings either, since they're recognized-but-unmappable.

            return new PrinceOpenTypeValue(capsKeyword, ligatureKeywords, numericKeywords, eastAsianKeywords, explicitFeatures, value);
        }

        public IPropertyValue Construct(Property[] properties) => null;

        private sealed class PrinceOpenTypeValue : IPropertyValue
        {
            private readonly string _capsKeyword;
            private readonly IReadOnlyList<string> _ligatureKeywords;
            private readonly IReadOnlyList<string> _numericKeywords;
            private readonly IReadOnlyList<string> _eastAsianKeywords;
            private readonly IReadOnlyList<(string Tag, string Value)> _explicitFeatures;

            public PrinceOpenTypeValue(string capsKeyword, IReadOnlyList<string> ligatureKeywords,
                IReadOnlyList<string> numericKeywords, IReadOnlyList<string> eastAsianKeywords,
                IReadOnlyList<(string Tag, string Value)> explicitFeatures, IEnumerable<Token> tokens)
            {
                _capsKeyword = capsKeyword;
                _ligatureKeywords = ligatureKeywords;
                _numericKeywords = numericKeywords;
                _eastAsianKeywords = eastAsianKeywords;
                _explicitFeatures = explicitFeatures;
                Original = new TokenValue(tokens);
            }

            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name)
            {
                if (name == PropertyNames.FontVariantCaps)
                    return KeywordToken(_capsKeyword);
                if (name == PropertyNames.FontVariantLigatures)
                    return KeywordListTokens(_ligatureKeywords);
                if (name == PropertyNames.FontVariantNumeric)
                    return KeywordListTokens(_numericKeywords);
                if (name == PropertyNames.FontVariantEastAsian)
                    return KeywordListTokens(_eastAsianKeywords);
                if (name == PropertyNames.FontFeatureSettings)
                    return FeatureSettingsTokens(_explicitFeatures);

                return null;
            }

            private static TokenValue KeywordToken(string keyword) => keyword is null
                ? new TokenValue(Enumerable.Empty<Token>())
                : new TokenValue([new Token(TokenType.Ident, keyword, TextPosition.Empty)]);

            private static TokenValue KeywordListTokens(IReadOnlyList<string> keywords)
            {
                if (keywords.Count == 0) return new TokenValue(Enumerable.Empty<Token>());

                var tokens = new List<Token>();
                foreach (var keyword in keywords)
                {
                    if (tokens.Count > 0) tokens.Add(Token.Whitespace);
                    tokens.Add(new Token(TokenType.Ident, keyword, TextPosition.Empty));
                }

                return new TokenValue(tokens);
            }

            private static TokenValue FeatureSettingsTokens(IReadOnlyList<(string Tag, string Value)> entries)
            {
                if (entries.Count == 0) return new TokenValue(Enumerable.Empty<Token>());

                var tokens = new List<Token>();
                foreach (var (tag, value) in entries)
                {
                    if (tokens.Count > 0)
                    {
                        tokens.Add(Token.Comma);
                        tokens.Add(Token.Whitespace);
                    }

                    tokens.Add(new StringToken(tag, true, '"', TextPosition.Empty));

                    if (value is not null)
                    {
                        tokens.Add(Token.Whitespace);
                        tokens.Add(value.Equals(Keywords.On, StringComparison.OrdinalIgnoreCase)
                                   || value.Equals(Keywords.Off, StringComparison.OrdinalIgnoreCase)
                            ? new Token(TokenType.Ident, value, TextPosition.Empty)
                            : new NumberToken(value, TextPosition.Empty));
                    }
                }

                return new TokenValue(tokens);
            }
        }
    }
}

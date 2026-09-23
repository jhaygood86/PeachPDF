#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// The value grammar for <c>font-variant-alternates</c> (CSS Fonts Module Level 4 §6.8):
    /// <c>normal | [ stylistic(&lt;ident&gt;) || historical-forms || styleset(&lt;ident&gt;#) ||
    /// character-variant(&lt;ident&gt;#) || swash(&lt;ident&gt;) || ornaments(&lt;ident&gt;) ||
    /// annotation(&lt;ident&gt;) ]</c> - each of the seven clauses may appear at most once (CSS Values'
    /// <c>||</c> combinator), in any order. Exposed for both
    /// <see cref="FontVariantAlternatesProperty"/>'s real <c>Converter</c> and
    /// <c>css-properties.json</c>'s <c>cssom-grammar</c> validator, per this repo's "one parser per
    /// grammar" convention (see CLAUDE.md) - the same shape <c>FontFeatureSettingsProperty.ValueGrammar</c>
    /// already uses. Only grammar is validated here; cross-referencing a clause's ident(s) against an
    /// <c>@font-feature-values</c> registration is a later, per-element, resolve-time operation (see
    /// <see cref="PeachPDF.Html.Core.FontVariantAlternatesResolver"/>) - an unmatched name is not a parse
    /// error, mirroring <c>font-palette</c>'s own precedent.
    /// </summary>
    internal sealed class FontVariantAlternatesGrammar : IValueConverter
    {
        private static readonly IReadOnlySet<string> SingleIdentFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            FunctionNames.Stylistic, FunctionNames.Swash, FunctionNames.Ornaments, FunctionNames.Annotation
        };

        private static readonly IReadOnlySet<string> IdentListFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            FunctionNames.Styleset, FunctionNames.CharacterVariant
        };

        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            if (value.Is(Keywords.Normal)) return new FontVariantAlternatesValue(Keywords.Normal, value);

            var seenSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var clauses = new List<string>();

            foreach (var item in value.ToItems())
            {
                if (item.Count == 0) continue;

                var clauseText = ParseClause(item, seenSlots);
                if (clauseText is null) return null;

                clauses.Add(clauseText);
            }

            return clauses.Count == 0 ? null : new FontVariantAlternatesValue(string.Join(" ", clauses), value);
        }

        private static string ParseClause(List<Token> item, HashSet<string> seenSlots)
        {
            if (item is [{ Type: TokenType.Ident } identToken])
            {
                if (!identToken.Data.Isi(Keywords.HistoricalForms)) return null;
                return seenSlots.Add(Keywords.HistoricalForms) ? Keywords.HistoricalForms : null;
            }

            if (item is not [{ Type: TokenType.Function } fn]) return null;

            var functionName = fn.Data.ToString();
            if (!seenSlots.Add(functionName)) return null;

            if (SingleIdentFunctions.Contains(functionName))
            {
                var name = ParseSingleIdentArgument(fn.ArgumentTokens);
                return name is null ? null : $"{functionName}({name})";
            }

            if (IdentListFunctions.Contains(functionName))
            {
                var names = ParseIdentListArgument(fn.ArgumentTokens);
                return names is null ? null : $"{functionName}({string.Join(", ", names)})";
            }

            return null;
        }

        // <ident> - exactly one identifier, no trailing content.
        private static string ParseSingleIdentArgument(IReadOnlyList<Token> argumentTokens)
        {
            var args = argumentTokens.Where(t => t.Type != TokenType.Whitespace).ToArray();
            return args is [{ Type: TokenType.Ident } nameToken] ? nameToken.Data.ToString() : null;
        }

        // <ident># - one or more comma-separated identifiers.
        private static List<string> ParseIdentListArgument(IReadOnlyList<Token> argumentTokens)
        {
            var names = new List<string>();

            foreach (var group in argumentTokens.ToList())
            {
                var trimmed = group.Where(t => t.Type != TokenType.Whitespace).ToArray();
                if (trimmed is not [{ Type: TokenType.Ident } nameToken]) return null;

                names.Add(nameToken.Data.ToString());
            }

            return names.Count == 0 ? null : names;
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<FontVariantAlternatesValue>();
        }

        private sealed class FontVariantAlternatesValue : IPropertyValue
        {
            private readonly string _text;

            public FontVariantAlternatesValue(string text, IEnumerable<Token> tokens)
            {
                _text = text;
                Original = new TokenValue(tokens);
            }

            public string CssText => _text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;
        }
    }
}

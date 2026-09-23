#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Html.Core
{
    /// <summary>
    /// A named OpenType feature-value alias declared with a <c>@font-feature-values</c> at-rule (CSS
    /// Fonts Module Level 4), keyed by the font family it applies to, which nested block declared it, and
    /// its name - mirrors <see cref="RegisteredFontPalette"/>. <c>font-variant-alternates</c> functions
    /// (<c>styleset()</c>, <c>character-variant()</c>, etc.) resolve their ident arguments against this
    /// registry (see <see cref="FontVariantAlternatesResolver"/>) to recover the raw integer(s) a
    /// <c>@styleset</c>/etc. block declared for that name.
    /// </summary>
    internal sealed class RegisteredFontFeatureValues
    {
        /// <summary>The (unquoted) font family this registration applies to.</summary>
        public string Family { get; }

        public FontFeatureValueBlockKind Kind { get; }

        /// <summary>The feature-value name (a custom-ident, e.g. <c>nice-style</c>).</summary>
        public string Name { get; }

        /// <summary>The one or more integers the block declared for this name (e.g. <c>styleset:
        /// 1 7;</c> yields <c>[1, 7]</c>).</summary>
        public IReadOnlyList<int> Values { get; }

        private RegisteredFontFeatureValues(string family, FontFeatureValueBlockKind kind, string name,
            IReadOnlyList<int> values)
        {
            Family = family;
            Kind = kind;
            Name = name;
            Values = values;
        }

        /// <summary>
        /// Harvests every valid <c>@font-feature-values</c> rule into a registry keyed by
        /// <c>(family, kind, name)</c>. A rule's family-list prelude expands into one registry entry per
        /// family (unlike <c>@font-palette-values</c>'s single <c>font-family</c> descriptor); the family
        /// is matched case-insensitively, the name case-sensitively (a custom-ident). Later duplicate
        /// registrations win (cascade order); a block declaration whose value isn't one-or-more integers
        /// is dropped.
        /// </summary>
        public static Dictionary<(string Family, FontFeatureValueBlockKind Kind, string Name), RegisteredFontFeatureValues> BuildRegistry(CssData cssData)
        {
            var registry = new Dictionary<(string, FontFeatureValueBlockKind, string), RegisteredFontFeatureValues>();

            foreach (var rule in cssData.EnumerateRulesRecursive().OfType<FontFeatureValuesRule>())
            {
                var families = ParseFamilyList(rule.FamilyList);
                if (families.Count == 0) continue;

                foreach (var blockRule in rule.Rules.OfType<FontFeatureValueSetRule>())
                {
                    if (!FontFeatureValueSetRule.TryGetBlockKind(blockRule.BlockName, out var kind)) continue;

                    foreach (var declaration in blockRule.Declarations)
                    {
                        if (!declaration.HasValue || !IsValidFeatureValueName(declaration.Name)) continue;

                        var values = ParseIntegerList(declaration.Value, kind);
                        if (values.Count == 0) continue;

                        foreach (var family in families)
                        {
                            var registered = new RegisteredFontFeatureValues(family, kind, declaration.Name, values);
                            registry[MakeKey(family, kind, declaration.Name)] = registered;
                        }
                    }
                }
            }

            return registry;
        }

        /// <summary>The registry key for a <c>(family, kind, name)</c> triple: family case-folded (font
        /// families match case-insensitively), kind and name verbatim (a custom-ident is case-sensitive).</summary>
        public static (string Family, FontFeatureValueBlockKind Kind, string Name) MakeKey(string family, FontFeatureValueBlockKind kind, string name) =>
            (family.ToLowerInvariant(), kind, name);

        // A declaration's name is a <custom-ident> (CSS Syntax), which excludes the five CSS-wide
        // keywords and the "default" keyword some grammars additionally reserve - "@styleset { unset:
        // 1; }" followed by "styleset(unset)" must not resolve, the same way it wouldn't in a real UA.
        private static bool IsValidFeatureValueName(string name) =>
            !name.Isi(Keywords.Initial) && !name.Isi(Keywords.Inherit) && !name.Isi(Keywords.Unset) &&
            !name.Isi(Keywords.Revert) && !name.Isi(Keywords.RevertLayer) && !name.Isi("default");

        // <family-name># : a comma-separated list of font family names, each possibly a quoted string or
        // a sequence of idents (an unquoted multi-word name). Reuses the same top-level-comma-aware split
        // RegisteredFontPalette.SplitTopLevelComma already implements, and the same per-family
        // normalization (quote stripping/whitespace collapsing) CssValueParser.GetFontFaceFamilyName
        // already applies for @font-palette-values's own font-family descriptor.
        private static List<string> ParseFamilyList(string? raw)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return result;

            foreach (var segment in RegisteredFontPalette.SplitTopLevelComma(raw))
            {
                var trimmedSegment = segment.Trim();
                var family = CssValueParser.GetFontFaceFamilyName(trimmedSegment)?.Trim();
                if (string.IsNullOrEmpty(family)) continue;

                // An unquoted multi-word family name comes back from GetFontFaceFamilyName with whatever
                // exact internal whitespace the prelude was authored with (it only special-cases a single
                // quoted-string token) - collapsed here to match how the *used* family value is
                // normalized (ValueExtensions.ToLiterals joins idents with exactly one space), so a
                // double space/tab in "@font-feature-values Times  New  Roman { ... }" still registers
                // under the same key "font-family: Times New Roman" resolves against. A quoted family
                // name is exempt: its content is already exact as authored, which is meaningful.
                var isQuoted = trimmedSegment.Length > 0 && (trimmedSegment[0] == '"' || trimmedSegment[0] == '\'');
                if (!isQuoted)
                    family = string.Join(" ", family.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

                result.Add(family);
            }

            return result;
        }

        // <integer>+ (or a narrower per-kind arity - CSS Fonts 4 §6.8): a bare UnknownProperty value is
        // one or more whitespace-separated integers, but @stylistic/@swash/@ornaments/@annotation each
        // take exactly one, and @character-variant takes one or two; only @styleset is open-ended. A
        // declaration whose value doesn't match its own block kind's arity is invalid in its entirety
        // (CSS Syntax's "an invalid declaration is dropped, not truncated" rule), not silently truncated
        // to however many integers a caller happens to read.
        private static List<int> ParseIntegerList(string raw, FontFeatureValueBlockKind kind)
        {
            var result = new List<int>();
            foreach (var token in raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(token, out var value)) return [];
                result.Add(value);
            }

            var (min, max) = kind switch
            {
                FontFeatureValueBlockKind.Styleset => (1, int.MaxValue),
                FontFeatureValueBlockKind.CharacterVariant => (1, 2),
                _ => (1, 1),
            };

            return result.Count >= min && result.Count <= max ? result : [];
        }
    }
}

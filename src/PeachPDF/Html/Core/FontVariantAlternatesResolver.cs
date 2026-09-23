#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Html.Core
{
    /// <summary>
    /// Resolves a computed <c>font-variant-alternates</c> value against the document's
    /// <c>@font-feature-values</c> registry (CSS Fonts Module Level 4 §6.8): each clause's ident
    /// argument(s) are looked up against the used font family and turned into the OpenType GSUB
    /// <c>(tag, value)</c> pair(s) that feature already means for <c>font-feature-settings</c> - the
    /// same "value selects the Nth alternate" convention <c>GsubShaper</c> already implements. Mirrors
    /// <see cref="FontPaletteResolver"/> (needs the registry + used family, unlike the registry-free
    /// pure-string resolvers in <c>TextShapingFeatureResolver</c>). An unmatched name contributes
    /// nothing, the same "unknown name is inert" rule <see cref="FontPaletteResolver"/> already applies.
    /// </summary>
    internal static class FontVariantAlternatesResolver
    {
        public static IReadOnlyList<(string Tag, int Value)> Resolve(string? fontVariantAlternates,
            string? usedFamily,
            IReadOnlyDictionary<(string Family, FontFeatureValueBlockKind Kind, string Name), RegisteredFontFeatureValues>? registry)
        {
            var value = fontVariantAlternates?.Trim();
            if (string.IsNullOrEmpty(value) || value == Keywords.Normal)
                return [];

            var family = usedFamily ?? string.Empty;
            var result = new List<(string, int)>();

            foreach (var clause in CssValueParser.SplitTopLevelWhitespace(value))
            {
                if (clause.Is(Keywords.HistoricalForms))
                {
                    result.Add(("hist", 1));
                    continue;
                }

                var openParen = clause.IndexOf('(');
                if (openParen <= 0 || clause[^1] != ')') continue;

                var functionName = clause[..openParen];
                var argsText = clause[(openParen + 1)..^1];
                var names = CssValueParser.SplitTopLevelCommas(argsText).Select(s => s.Trim()).ToArray();

                if (functionName.Is(FunctionNames.Stylistic)) ResolveSingle(names, family, FontFeatureValueBlockKind.Stylistic, "salt", registry, result);
                else if (functionName.Is(FunctionNames.Swash)) ResolveSingle(names, family, FontFeatureValueBlockKind.Swash, "swsh", registry, result);
                else if (functionName.Is(FunctionNames.Ornaments)) ResolveSingle(names, family, FontFeatureValueBlockKind.Ornaments, "ornm", registry, result);
                else if (functionName.Is(FunctionNames.Annotation)) ResolveSingle(names, family, FontFeatureValueBlockKind.Annotation, "nalt", registry, result);
                else if (functionName.Is(FunctionNames.Styleset)) ResolveStyleset(names, family, registry, result);
                else if (functionName.Is(FunctionNames.CharacterVariant)) ResolveCharacterVariant(names, family, registry, result);
            }

            return result;
        }

        // stylistic()/swash()/ornaments()/annotation(): one name, one feature tag, the block's single
        // declared integer is the value selecting the Nth alternate (same convention font-feature-settings
        // already uses).
        private static void ResolveSingle(IReadOnlyList<string> names, string family, FontFeatureValueBlockKind kind,
            string tag, IReadOnlyDictionary<(string, FontFeatureValueBlockKind, string), RegisteredFontFeatureValues>? registry,
            List<(string, int)> result)
        {
            if (names.Count != 1) return;
            if (!TryLookup(registry, family, kind, names[0], out var registered)) return;
            if (registered.Values.Count == 0) return;

            result.Add((tag, registered.Values[0]));
        }

        // styleset(): each name's declared integer LIST turns on that many distinct ssNN features
        // (01-20), each at value 1 - CSS Fonts 4 allows turning on several numbered features at once this
        // way, unlike the single-feature-with-a-value shape the other functions use.
        private static void ResolveStyleset(IReadOnlyList<string> names, string family,
            IReadOnlyDictionary<(string, FontFeatureValueBlockKind, string), RegisteredFontFeatureValues>? registry,
            List<(string, int)> result)
        {
            foreach (var name in names)
            {
                if (!TryLookup(registry, family, FontFeatureValueBlockKind.Styleset, name, out var registered)) continue;

                foreach (var featureNumber in registered.Values)
                {
                    if (featureNumber is < 1 or > 20) continue;
                    result.Add(($"ss{featureNumber:D2}", 1));
                }
            }
        }

        // character-variant(): each name's first integer (1-99) selects cvNN; an optional second integer
        // selects the value passed to it (defaulting to 1), the same shape font-feature-settings uses for
        // an Alternate Substitution feature.
        private static void ResolveCharacterVariant(IReadOnlyList<string> names, string family,
            IReadOnlyDictionary<(string, FontFeatureValueBlockKind, string), RegisteredFontFeatureValues>? registry,
            List<(string, int)> result)
        {
            foreach (var name in names)
            {
                if (!TryLookup(registry, family, FontFeatureValueBlockKind.CharacterVariant, name, out var registered)) continue;
                if (registered.Values.Count == 0) continue;

                var featureNumber = registered.Values[0];
                if (featureNumber is < 1 or > 99) continue;

                var value = registered.Values.Count > 1 ? registered.Values[1] : 1;
                result.Add(($"cv{featureNumber:D2}", value));
            }
        }

        private static bool TryLookup(
            IReadOnlyDictionary<(string, FontFeatureValueBlockKind, string), RegisteredFontFeatureValues>? registry,
            string family, FontFeatureValueBlockKind kind, string name, out RegisteredFontFeatureValues registered)
        {
            registered = null!;
            return registry is not null &&
                   registry.TryGetValue(RegisteredFontFeatureValues.MakeKey(family, kind, name), out registered!);
        }
    }
}

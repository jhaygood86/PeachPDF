#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Html.Core
{
    /// <summary>How a <c>@font-palette-values</c> rule's <c>base-palette</c> selects a CPAL palette.</summary>
    internal enum FontPaletteBaseKind
    {
        /// <summary>An explicit palette index (<see cref="RegisteredFontPalette.BaseIndex"/>).</summary>
        Index,

        /// <summary>The font's first palette flagged usable with a light background.</summary>
        Light,

        /// <summary>The font's first palette flagged usable with a dark background.</summary>
        Dark
    }

    /// <summary>
    /// A named palette override declared with a <c>@font-palette-values</c> at-rule (CSS Fonts Module Level 4),
    /// keyed by its dashed-ident name and the font family it applies to. Captures the resolved
    /// <c>base-palette</c> selection and the parsed <c>override-colors</c>. Mirrors <see cref="RegisteredProperty"/>.
    /// </summary>
    internal sealed class RegisteredFontPalette
    {
        /// <summary>The palette name, a dashed-ident (e.g. <c>--my-palette</c>).</summary>
        public string Name { get; }

        /// <summary>The (unquoted) font family this palette applies to.</summary>
        public string Family { get; }

        public FontPaletteBaseKind BaseKind { get; }

        /// <summary>The explicit base-palette index, meaningful only when <see cref="BaseKind"/> is <see cref="FontPaletteBaseKind.Index"/>.</summary>
        public int BaseIndex { get; }

        /// <summary>Per-entry color overrides (CPAL entry index → color); empty when the rule declares none.</summary>
        public IReadOnlyList<KeyValuePair<int, RColor>> Overrides { get; }

        private RegisteredFontPalette(string name, string family, FontPaletteBaseKind baseKind, int baseIndex,
            IReadOnlyList<KeyValuePair<int, RColor>> overrides)
        {
            Name = name;
            Family = family;
            BaseKind = baseKind;
            BaseIndex = baseIndex;
            Overrides = overrides;
        }

        /// <summary>
        /// Builds a <see cref="RegisteredFontPalette"/> from a parsed <c>@font-palette-values</c> rule, or returns
        /// null if the rule is invalid and must be ignored: a name that isn't a dashed-ident, or a missing
        /// <c>font-family</c> (the family is what the palette attaches to).
        /// </summary>
        public static RegisteredFontPalette? FromRule(IFontPaletteValuesRule rule, CssValueParser valueParser)
        {
            if (rule.Name is null || !rule.Name.StartsWith("--", StringComparison.Ordinal))
                return null;

            var family = CssValueParser.GetFontFaceFamilyName(rule.Family ?? string.Empty)?.Trim();
            if (string.IsNullOrEmpty(family))
                return null;

            ParseBasePalette(rule.BasePalette, out var baseKind, out var baseIndex);
            var overrides = ParseOverrideColors(rule.OverrideColors, valueParser);

            return new RegisteredFontPalette(rule.Name, family!, baseKind, baseIndex, overrides);
        }

        /// <summary>
        /// Harvests every valid <c>@font-palette-values</c> rule into a registry keyed by
        /// <c>(name, normalized-family)</c>. The name is case-sensitive (a dashed-ident); the family is matched
        /// case-insensitively. Later duplicate registrations win (cascade order); invalid rules are dropped.
        /// </summary>
        public static Dictionary<(string Name, string Family), RegisteredFontPalette> BuildRegistry(CssData cssData, CssValueParser valueParser)
        {
            var registry = new Dictionary<(string, string), RegisteredFontPalette>();
            foreach (var rule in cssData.EnumerateRulesRecursive().OfType<FontPaletteValuesRule>())
            {
                var registered = FromRule(rule, valueParser);
                if (registered is not null)
                    registry[MakeKey(registered.Name, registered.Family)] = registered;
            }

            return registry;
        }

        /// <summary>The registry key for a <c>(name, family)</c> pair: name verbatim (dashed-idents are
        /// case-sensitive), family case-folded (font families match case-insensitively).</summary>
        public static (string Name, string Family) MakeKey(string name, string family) =>
            (name, family.ToLowerInvariant());

        private static void ParseBasePalette(string? raw, out FontPaletteBaseKind kind, out int index)
        {
            kind = FontPaletteBaseKind.Index;
            index = 0;

            var value = raw?.Trim();
            if (string.IsNullOrEmpty(value))
                return;

            if (value.Equals("light", StringComparison.OrdinalIgnoreCase))
                kind = FontPaletteBaseKind.Light;
            else if (value.Equals("dark", StringComparison.OrdinalIgnoreCase))
                kind = FontPaletteBaseKind.Dark;
            else if (int.TryParse(value, out var parsed) && parsed >= 0)
                index = parsed;
        }

        // override-colors: [ <index> <color> ]#  e.g. "0 #ff0000, 1 rgb(0, 255, 0)"
        private static IReadOnlyList<KeyValuePair<int, RColor>> ParseOverrideColors(string? raw, CssValueParser valueParser)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return [];

            var result = new List<KeyValuePair<int, RColor>>();
            foreach (var item in SplitTopLevelComma(raw))
            {
                var trimmed = item.Trim();
                int space = IndexOfWhitespace(trimmed);
                if (space <= 0)
                    continue;

                var indexPart = trimmed[..space];
                var colorPart = trimmed[(space + 1)..].Trim();
                if (!int.TryParse(indexPart, out var entryIndex) || entryIndex < 0 || colorPart.Length == 0)
                    continue;

                if (valueParser.TryGetColor(colorPart, 0, colorPart.Length, out var color))
                    result.Add(new KeyValuePair<int, RColor>(entryIndex, color));
            }

            return result;
        }

        private static int IndexOfWhitespace(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (char.IsWhiteSpace(s[i]))
                    return i;
            return -1;
        }

        // Splits on commas that are not inside a function's parentheses, so rgb(0, 255, 0) stays intact.
        private static IEnumerable<string> SplitTopLevelComma(string value)
        {
            int depth = 0, start = 0;
            for (int i = 0; i < value.Length; i++)
            {
                switch (value[i])
                {
                    case '(':
                        depth++;
                        break;
                    case ')':
                        if (depth > 0) depth--;
                        break;
                    case ',' when depth == 0:
                        yield return value[start..i];
                        start = i + 1;
                        break;
                }
            }

            yield return value[start..];
        }
    }
}

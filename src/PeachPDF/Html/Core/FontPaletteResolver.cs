#nullable enable

using System;
using System.Collections.Generic;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Html.Core
{
    /// <summary>
    /// Resolves a computed <c>font-palette</c> value against the used font (CSS Fonts Module Level 4): the
    /// <c>light</c>/<c>dark</c> keywords (via the font's CPAL type flags), an <c>@font-palette-values</c> rule
    /// (its <c>base-palette</c> + <c>override-colors</c>), and <c>palette-mix()</c> (blended per CPAL entry) are
    /// all reduced to a concrete <see cref="RFontPalette"/> — a palette index plus per-entry overrides — so the
    /// PDF backend only looks up an index and applies overrides. Returns <c>null</c> for the default (palette 0,
    /// no overrides), keeping the unchanged paint path for the common case and any non-color font.
    /// </summary>
    internal static class FontPaletteResolver
    {
        private static readonly IReadOnlyList<KeyValuePair<int, RColor>> NoOverrides = [];

        public static RFontPalette? Resolve(string? fontPalette, RFont font, string? usedFamily,
            IReadOnlyDictionary<(string Name, string Family), RegisteredFontPalette>? registry)
        {
            // Only a COLR/CPAL color font has palettes to select among; nothing to do otherwise.
            if (font.PaletteCount == 0)
                return null;

            var value = string.IsNullOrWhiteSpace(fontPalette) ? Keywords.Normal : fontPalette!.Trim();
            var family = usedFamily ?? string.Empty;

            var tokens = CssValueParser.GetCssTokens(value);
            if (PaletteMixGrammar.TryParse(tokens) is { } mix)
                return ResolvePaletteMix(mix, font, family, registry);

            var (index, overrides) = ResolveSimple(value, font, family, registry);
            // Collapse the default selection to null so the byte-identical legacy path is used.
            return index == 0 && overrides.Count == 0 ? null : new RFontPalette(index, overrides);
        }

        // Resolves a non-mix palette reference to a concrete (palette index, overrides).
        private static (int Index, IReadOnlyList<KeyValuePair<int, RColor>> Overrides) ResolveSimple(
            string value, RFont font, string family,
            IReadOnlyDictionary<(string, string), RegisteredFontPalette>? registry)
        {
            if (value.Equals(Keywords.Normal, StringComparison.OrdinalIgnoreCase))
                return (0, NoOverrides);
            if (value.Equals("light", StringComparison.OrdinalIgnoreCase))
                return (font.FirstLightPalette() ?? 0, NoOverrides);
            if (value.Equals("dark", StringComparison.OrdinalIgnoreCase))
                return (font.FirstDarkPalette() ?? 0, NoOverrides);

            if (value.StartsWith("--", StringComparison.Ordinal) && registry is not null &&
                registry.TryGetValue(RegisteredFontPalette.MakeKey(value, family), out var registered))
            {
                return (ResolveBaseIndex(registered.BaseKind, registered.BaseIndex, font), registered.Overrides);
            }

            // An unknown/unmatched name resolves as `normal` (CSS Fonts 4).
            return (0, NoOverrides);
        }

        private static int ResolveBaseIndex(FontPaletteBaseKind kind, int index, RFont font) => kind switch
        {
            FontPaletteBaseKind.Light => font.FirstLightPalette() ?? 0,
            FontPaletteBaseKind.Dark => font.FirstDarkPalette() ?? 0,
            // An out-of-range base-palette index falls back to 0 (CSS Fonts 4).
            _ => index >= 0 && index < font.PaletteCount ? index : 0
        };

        private static RFontPalette? ResolvePaletteMix(ParsedPaletteMix mix, RFont font, string family,
            IReadOnlyDictionary<(string, string), RegisteredFontPalette>? registry)
        {
            int entryCount = font.PaletteEntryCount;
            if (entryCount == 0)
                return null;

            var (index1, over1) = ResolveSimple(mix.First.Palette, font, family, registry);
            var (index2, over2) = ResolveSimple(mix.Second.Palette, font, family, registry);
            var dict1 = ToDictionary(over1);
            var dict2 = ToDictionary(over2);

            var overrides = new List<KeyValuePair<int, RColor>>(entryCount);
            for (int entry = 0; entry < entryCount; entry++)
            {
                var c1 = EntryColor(font, index1, entry, dict1);
                var c2 = EntryColor(font, index2, entry, dict2);
                var mixed = ColorFunctionExtensions.MixPaletteColors(ToColor(c1), ToColor(c2),
                    mix.ColorSpace, mix.HueMethod ?? string.Empty, mix.First.Percentage, mix.Second.Percentage);
                overrides.Add(new KeyValuePair<int, RColor>(entry, ToRColor(mixed)));
            }

            return new RFontPalette(0, overrides);
        }

        private static RColor EntryColor(RFont font, int paletteIndex, int entry, Dictionary<int, RColor> overrides)
        {
            if (overrides.TryGetValue(entry, out var color))
                return color;
            return font.TryGetPaletteColor(paletteIndex, entry, out var cpal) ? cpal : RColor.Black;
        }

        private static Dictionary<int, RColor> ToDictionary(IReadOnlyList<KeyValuePair<int, RColor>> overrides)
        {
            var dict = new Dictionary<int, RColor>(overrides.Count);
            foreach (var (entry, color) in overrides)
                dict[entry] = color;
            return dict;
        }

        private static Color ToColor(RColor c) => new(c.R, c.G, c.B, c.A);

        private static RColor ToRColor(Color c) => RColor.FromArgb(c.A, c.R, c.G, c.B);
    }
}

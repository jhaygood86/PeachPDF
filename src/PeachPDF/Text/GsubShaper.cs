using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using PeachPDF.Fonts.OpenType;

namespace PeachPDF.Text
{
    /// <summary>
    /// One shaped glyph: <see cref="ClusterStart"/>/<see cref="ClusterLength"/> are the UTF-16
    /// offsets, into the text that was shaped, of the source character(s) this glyph represents -
    /// a single character for an ordinary glyph, or the whole matched span for a ligature.
    /// </summary>
    internal readonly record struct ShapedGlyph(int GlyphIndex, int ClusterStart, int ClusterLength);

    /// <summary>Which GSUB ligature features <see cref="GsubShaper.Shape"/> should apply.</summary>
    [Flags]
    internal enum LigatureFeatures
    {
        None = 0,

        /// <summary>The `liga`/`clig` ("common ligatures") features - what CSS
        /// <c>font-variant-ligatures: common-ligatures</c> (and the initial <c>normal</c>) mean.</summary>
        Common = 1,

        /// <summary>The `rlig` ("required ligatures") feature - applied whenever the font defines
        /// it, independent of <c>font-variant-ligatures</c> (mirrors browser behavior).</summary>
        Required = 2,

        Default = Common | Required,
    }

    /// <summary>
    /// Turns text into a shaped glyph run: a 1:1 codepoint-to-glyph <c>cmap</c> mapping (via
    /// <see cref="OpenTypeDescriptor.CharCodeToGlyphIndex"/>), followed by GSUB ligature
    /// substitution when the font has a <c>GSUB</c> table and the requested <see cref="LigatureFeatures"/>
    /// call for it. This is general text-processing logic built from <c>PeachPDF.Fonts.OpenType</c>'s table
    /// data, hence its own <c>PeachPDF.Text</c> namespace - see <see cref="HyphenationEngine"/> for
    /// the same reasoning applied to hyphenation.
    ///
    /// This is the single glyph-walk every text-drawing/measuring call site shares
    /// (<c>FontHelper.MeasureString</c>, <c>XGraphicsPdfRenderer.DrawString</c>,
    /// <c>ColorGlyphPainter.Paint</c>, <c>GraphicsAdapter.GetTextOutline</c>, <c>CMapInfo</c>) -
    /// previously each re-derived "codepoint to glyph" independently.
    /// </summary>
    internal static class GsubShaper
    {
        // Real per-language script/language-system selection is out of scope (see
        // .claude/accepted-gaps/no-text-shaping.md) - "latn" covers the common case, "DFLT" the
        // fallback GsubTable itself falls back to the font's first script if neither is present.
        private static readonly IReadOnlyList<string> ScriptPreference = ["latn", "DFLT"];

        // GsubTable instances are cached and shared process-wide (owned by the cached
        // OpenTypeFontface), so the lookup-index sets computed from them are cached the same way -
        // otherwise every shaped word would re-walk ScriptList/FeatureList from scratch.
        private static readonly ConditionalWeakTable<GsubTable, ConcurrentDictionary<LigatureFeatures, SortedSet<int>>> LookupIndexCache = new();

        public static IReadOnlyList<ShapedGlyph> Shape(OpenTypeDescriptor descriptor, string text, LigatureFeatures features)
        {
            List<ShapedGlyph> glyphs = MapToGlyphs(descriptor, text);
            if (glyphs.Count < 2 || features == LigatureFeatures.None)
                return glyphs;

            // Ligatures aren't a realistic case for symbol-encoded fonts (Wingdings et al.), and
            // their remapped codepoints would need separate coverage-matching logic - skip rather
            // than risk mismatched substitutions.
            if (descriptor.FontFace.cmap.symbol)
                return glyphs;

            GsubTable? gsub = descriptor.FontFace.gsub?.Table;
            if (gsub is null)
                return glyphs;

            SortedSet<int> lookupIndices = GetActiveLookupIndices(gsub, features);
            if (lookupIndices.Count == 0)
                return glyphs;

            foreach (int lookupIndex in lookupIndices)
            {
                GsubLigatureLookup? lookup = gsub.GetLigatureLookup(lookupIndex);
                if (lookup is not null)
                    ApplyLigatureLookup(lookup, glyphs);
            }

            return glyphs;
        }

        private static List<ShapedGlyph> MapToGlyphs(OpenTypeDescriptor descriptor, string text)
        {
            var result = new List<ShapedGlyph>(text.Length);
            bool symbol = descriptor.FontFace.cmap.symbol;
            int clusterStart = 0;

            foreach (Rune rune in text.EnumerateRunes())
            {
                int utf16Length = rune.Utf16SequenceLength;
                Rune lookup = rune;
                if (symbol && rune.Value <= 0xFFFF)
                    lookup = new Rune(rune.Value | (descriptor.FontFace.os2.usFirstCharIndex & 0xFF00));

                int glyphIndex = descriptor.CharCodeToGlyphIndex(lookup);
                result.Add(new ShapedGlyph(glyphIndex, clusterStart, utf16Length));
                clusterStart += utf16Length;
            }

            return result;
        }

        private static SortedSet<int> GetActiveLookupIndices(GsubTable gsub, LigatureFeatures features)
        {
            var perTableCache = LookupIndexCache.GetOrCreateValue(gsub);
            return perTableCache.GetOrAdd(features, f =>
            {
                var tags = new HashSet<string>();
                if ((f & LigatureFeatures.Common) != 0)
                {
                    tags.Add("liga");
                    tags.Add("clig");
                }
                if ((f & LigatureFeatures.Required) != 0)
                    tags.Add("rlig");

                return tags.Count == 0 ? [] : gsub.GetActiveLookupIndices(ScriptPreference, tags);
            });
        }

        /// <summary>
        /// Applies one ligature lookup as a single left-to-right pass: at each position, the first
        /// matching ligature (in the font's own authored order) replaces the matched glyph span,
        /// and scanning resumes immediately after the substituted glyph - matching how OpenType
        /// Lookup Type 4 is specified to behave.
        /// </summary>
        private static void ApplyLigatureLookup(GsubLigatureLookup lookup, List<ShapedGlyph> glyphs)
        {
            int i = 0;
            while (i < glyphs.Count)
            {
                if (TryMatchLigature(lookup, glyphs, i, out ShapedGlyph merged, out int consumed))
                {
                    glyphs.RemoveRange(i, consumed);
                    glyphs.Insert(i, merged);
                }
                i++;
            }
        }

        private static bool TryMatchLigature(GsubLigatureLookup lookup, List<ShapedGlyph> glyphs, int index, out ShapedGlyph merged, out int consumed)
        {
            merged = default;
            consumed = 0;
            var firstGlyph = (ushort)glyphs[index].GlyphIndex;

            foreach (GsubLigatureSubtable subtable in lookup.Subtables)
            {
                int coverageIndex = subtable.Coverage.IndexOfGlyph(firstGlyph);
                if (coverageIndex < 0 || coverageIndex >= subtable.LigatureSets.Length)
                    continue;

                foreach (GsubLigature ligature in subtable.LigatureSets[coverageIndex])
                {
                    int componentsNeeded = ligature.ComponentGlyphIds.Length;
                    if (index + 1 + componentsNeeded > glyphs.Count)
                        continue;

                    bool allMatch = true;
                    for (int j = 0; j < componentsNeeded; j++)
                    {
                        if (glyphs[index + 1 + j].GlyphIndex != ligature.ComponentGlyphIds[j])
                        {
                            allMatch = false;
                            break;
                        }
                    }
                    if (!allMatch)
                        continue;

                    consumed = componentsNeeded + 1;
                    ShapedGlyph first = glyphs[index];
                    ShapedGlyph last = glyphs[index + consumed - 1];
                    merged = new ShapedGlyph(ligature.LigatureGlyph, first.ClusterStart, last.ClusterStart + last.ClusterLength - first.ClusterStart);
                    return true;
                }
            }

            return false;
        }
    }
}

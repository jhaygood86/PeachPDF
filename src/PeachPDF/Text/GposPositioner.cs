using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PeachPDF.Fonts.OpenType;

namespace PeachPDF.Text
{
    /// <summary>
    /// Applies GPOS positioning (kerning via Lookup Types 1/2, mark-to-base/mark-to-mark attachment
    /// via Lookup Types 4/6) to a glyph run <see cref="GsubShaper.Shape"/> has already substituted -
    /// GPOS always runs after GSUB, since its coverage tables are authored against post-substitution
    /// glyph ids, matching universal real-shaper pipeline order. Like <see cref="GsubShaper"/>, this
    /// is general text-processing logic built from <c>PeachPDF.Fonts.OpenType</c>'s table data.
    /// </summary>
    internal static class GposPositioner
    {
        // Same script preference GsubShaper uses - GPOS's own ScriptList is a separate table from
        // GSUB's, but the "which script this document's glyphs belong to" question is the same one.
        private static readonly IReadOnlyList<string> ScriptPreference = GsubShaper.ScriptPreference;

        // GposTable instances are cached and shared process-wide, same rationale as GsubShaper's own
        // LookupIndexCache.
        private static readonly ConditionalWeakTable<GposTable, ConcurrentDictionary<TextShapingFeatures, SortedSet<int>>> LookupIndexCache = new();

        public static void Apply(OpenTypeDescriptor descriptor, List<ShapedGlyph> glyphs, TextShapingFeatures features)
        {
            if (glyphs.Count == 0)
                return;

            // Positioning isn't a realistic case for symbol-encoded fonts, same rationale as
            // GsubShaper.Shape's own early-out.
            if (descriptor.FontFace.cmap.symbol)
                return;

            GposTable? gpos = descriptor.FontFace.gpos?.Table;
            if (gpos is null)
                return;

            GdefTable? gdef = descriptor.FontFace.gdef?.Table;

            SortedSet<int> lookupIndices = GetActiveLookupIndices(gpos, features);
            if (lookupIndices.Count == 0)
                return;

            foreach (int lookupIndex in lookupIndices)
            {
                switch (gpos.GetResolvedLookupType(lookupIndex))
                {
                    case 1:
                        if (gpos.GetSingleAdjustmentLookup(lookupIndex) is { } single)
                            ApplySingleAdjustment(single, glyphs);
                        break;
                    case 2:
                        if (gpos.GetPairAdjustmentLookup(lookupIndex) is { } pair)
                            ApplyPairAdjustment(pair, glyphs);
                        break;
                    case 4:
                        if (gpos.GetMarkToBaseLookup(lookupIndex) is { } markToBase)
                            ApplyMarkToBase(descriptor, markToBase, glyphs, gdef);
                        break;
                    case 6:
                        if (gpos.GetMarkToMarkLookup(lookupIndex) is { } markToMark)
                            ApplyMarkToMark(descriptor, markToMark, glyphs);
                        break;
                    // 3 (Cursive Attachment), 5 (MarkToLigature), 7/8 (Context/Chained Context
                    // Positioning), and any unresolved type: not supported, left unmodified - see
                    // GposTable's own file-header gap note.
                }
            }
        }

        private static SortedSet<int> GetActiveLookupIndices(GposTable gpos, TextShapingFeatures features)
        {
            var perTableCache = LookupIndexCache.GetOrCreateValue(gpos);
            return perTableCache.GetOrAdd(features, key =>
            {
                // `font-kerning: none` gates `kern` off entirely; mark-positioning tags are requested
                // unconditionally - combining-mark attachment isn't a stylistic opt-out the way
                // kerning is (a diacritic would paint in the wrong place entirely without it).
                var tags = new HashSet<string> { "mark", "mkmk" };
                if (key.Kerning)
                    tags.Add("kern");

                return gpos.GetActiveLookupIndices(ScriptPreference, tags);
            });
        }

        // internal rather than private: lets tests exercise the positioning math directly against a
        // synthetic GposTable + hand-built glyph list, without needing a font whose table directory
        // actually lists a GPOS entry (see GposPositionerSyntheticTests's own remarks).
        internal static void ApplySingleAdjustment(GposSingleAdjustmentLookup lookup, List<ShapedGlyph> glyphs)
        {
            for (int i = 0; i < glyphs.Count; i++)
            {
                ushort glyphId = (ushort)glyphs[i].GlyphIndex;
                foreach (GposSingleAdjustmentSubtable subtable in lookup.Subtables)
                {
                    if (subtable.TryGetValue(glyphId, out GposValueRecord value))
                    {
                        glyphs[i] = AddValue(glyphs[i], value);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Walks the glyph run one position at a time (not skipping ahead by 2 after a match) so an
        /// overlapping pair chain (e.g. "AVA" needs both the "AV" and "VA" pairs checked) is handled
        /// correctly - matching how real kerning application works.
        /// </summary>
        // internal rather than private - see ApplySingleAdjustment's identical rationale.
        internal static void ApplyPairAdjustment(GposPairAdjustmentLookup lookup, List<ShapedGlyph> glyphs)
        {
            for (int i = 0; i < glyphs.Count - 1; i++)
            {
                ushort first = (ushort)glyphs[i].GlyphIndex;
                ushort second = (ushort)glyphs[i + 1].GlyphIndex;

                foreach (GposPairAdjustmentSubtable subtable in lookup.Subtables)
                {
                    if (subtable.TryGetValues(first, second, out GposValueRecord value1, out GposValueRecord value2))
                    {
                        glyphs[i] = AddValue(glyphs[i], value1);
                        glyphs[i + 1] = AddValue(glyphs[i + 1], value2);
                        break;
                    }
                }
            }
        }

        private static ShapedGlyph AddValue(ShapedGlyph glyph, GposValueRecord value) => glyph with
        {
            XAdvanceDelta = glyph.XAdvanceDelta + value.XAdvance,
            YAdvanceDelta = glyph.YAdvanceDelta + value.YAdvance,
            XOffset = glyph.XOffset + value.XPlacement,
            YOffset = glyph.YOffset + value.YPlacement,
        };

        /// <summary>
        /// For each glyph a Type 4 lookup's `MarkCoverage` covers, finds the nearest preceding glyph
        /// that "participates" per the lookup's own `lookupFlag` (honoring GDEF's glyph
        /// classification - `IGNORE_MARKS` conventionally excludes other marks from being treated as
        /// a base, so this naturally skips past an earlier mark already attached to the same base)
        /// and, if that glyph is covered by `BaseCoverage`, positions the mark against it.
        /// </summary>
        // internal rather than private - see ApplySingleAdjustment's identical rationale.
        internal static void ApplyMarkToBase(OpenTypeDescriptor descriptor, GposMarkToBaseLookup lookup, List<ShapedGlyph> glyphs, GdefTable? gdef)
        {
            for (int i = 0; i < glyphs.Count; i++)
            {
                ushort markGlyph = (ushort)glyphs[i].GlyphIndex;

                foreach (GposMarkAttachmentSubtable subtable in lookup.Subtables)
                {
                    int markIndex = subtable.MarkCoverage.IndexOfGlyph(markGlyph);
                    if (markIndex < 0 || markIndex >= subtable.Marks.Length)
                        continue;

                    int baseIndex = FindParticipatingPredecessor(glyphs, i, lookup.LookupFlag, gdef);
                    if (baseIndex < 0)
                        continue;

                    if (TryGetAnchor(subtable, (ushort)glyphs[baseIndex].GlyphIndex, markIndex, out GposAnchor markAnchor, out GposAnchor baseAnchor))
                    {
                        ApplyMarkAnchor(descriptor, glyphs, baseIndex, i, markAnchor, baseAnchor);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// A Type 6 lookup always targets the immediately preceding glyph (mark-to-mark chains are,
        /// by construction, contiguous runs of marks) - no backward-skip search is needed.
        /// </summary>
        // internal rather than private - see ApplySingleAdjustment's identical rationale.
        internal static void ApplyMarkToMark(OpenTypeDescriptor descriptor, GposMarkToMarkLookup lookup, List<ShapedGlyph> glyphs)
        {
            for (int i = 1; i < glyphs.Count; i++)
            {
                ushort markGlyph = (ushort)glyphs[i].GlyphIndex;
                int mark2Index = i - 1;
                ushort mark2Glyph = (ushort)glyphs[mark2Index].GlyphIndex;

                foreach (GposMarkAttachmentSubtable subtable in lookup.Subtables)
                {
                    int markIndex = subtable.MarkCoverage.IndexOfGlyph(markGlyph);
                    if (markIndex < 0 || markIndex >= subtable.Marks.Length)
                        continue;

                    if (TryGetAnchor(subtable, mark2Glyph, markIndex, out GposAnchor markAnchor, out GposAnchor mark2Anchor))
                    {
                        ApplyMarkAnchor(descriptor, glyphs, mark2Index, i, markAnchor, mark2Anchor);
                        break;
                    }
                }
            }
        }

        private static bool TryGetAnchor(GposMarkAttachmentSubtable subtable, ushort baseGlyph, int markIndex, out GposAnchor markAnchor, out GposAnchor baseAnchor)
        {
            markAnchor = default;
            baseAnchor = default;

            int baseCoverageIndex = subtable.BaseCoverage.IndexOfGlyph(baseGlyph);
            if (baseCoverageIndex < 0 || baseCoverageIndex >= subtable.BaseAnchorsByClass.Length)
                return false;

            (int markClass, GposAnchor anchor) = subtable.Marks[markIndex];
            GposAnchor?[] baseAnchorsForClass = subtable.BaseAnchorsByClass[baseCoverageIndex];
            if (markClass < 0 || markClass >= baseAnchorsForClass.Length || baseAnchorsForClass[markClass] is not { } resolvedBaseAnchor)
                return false;

            markAnchor = anchor;
            baseAnchor = resolvedBaseAnchor;
            return true;
        }

        private static int FindParticipatingPredecessor(List<ShapedGlyph> glyphs, int fromIndex, ushort lookupFlag, GdefTable? gdef)
        {
            for (int j = fromIndex - 1; j >= 0; j--)
            {
                if (GlyphSequenceFilter.Participates((ushort)glyphs[j].GlyphIndex, lookupFlag, gdef, null))
                    return j;
            }
            return -1;
        }

        /// <summary>
        /// Positions the glyph at <paramref name="markIndex"/> so its own <paramref name="markAnchor"/>
        /// aligns with the base/mark2 glyph's <paramref name="baseAnchor"/>, both expressed relative to
        /// each glyph's own default (pre-this-adjustment) pen position - the running sum of every
        /// glyph's natural advance (from <paramref name="descriptor"/>) plus any already-applied
        /// XAdvanceDelta between the base and the mark accounts for the pen movement between their two
        /// default origins (typically zero, since <paramref name="baseIndex"/> is always
        /// <paramref name="markIndex"/>'s nearest participating predecessor). Only the mark's own
        /// placement changes here - never its advance, which is left exactly as `hmtx` (or an earlier
        /// GSUB/GPOS lookup) already resolved it, matching the spec's mark-attachment model.
        /// </summary>
        private static void ApplyMarkAnchor(OpenTypeDescriptor descriptor, List<ShapedGlyph> glyphs, int baseIndex, int markIndex, GposAnchor markAnchor, GposAnchor baseAnchor)
        {
            double intermediateAdvance = 0;
            for (int k = baseIndex; k < markIndex; k++)
                intermediateAdvance += descriptor.GlyphIndexToWidth(glyphs[k].GlyphIndex) + glyphs[k].XAdvanceDelta;

            ShapedGlyph baseGlyph = glyphs[baseIndex];
            glyphs[markIndex] = glyphs[markIndex] with
            {
                XOffset = baseAnchor.X - markAnchor.X - intermediateAdvance + baseGlyph.XOffset,
                YOffset = baseAnchor.Y - markAnchor.Y + baseGlyph.YOffset,
            };
        }
    }
}

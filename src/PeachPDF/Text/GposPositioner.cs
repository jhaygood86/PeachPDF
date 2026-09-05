using System;
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
        // Guards ApplySequenceContextLookup's own nested-lookup application against a pathological/
        // adversarial font nesting indefinitely - same rationale as GsubShaper.MaxNestedContextDepth.
        private const int MaxNestedContextDepth = 8;

        // lookupFlag's RIGHT_TO_LEFT bit - GPOS-specific (GSUB/GDEF's lookupFlag has no such bit; not
        // part of GlyphSequenceFilter's own mark-filtering-only bit set), so it stays local to the one
        // lookup type (Cursive Attachment) that reads it.
        private const ushort RightToLeftLookupFlag = 0x0001;

        // Same script preference GsubShaper uses - GPOS's own ScriptList is a separate table from
        // GSUB's, but the "which script this document's glyphs belong to" question is the same one, so
        // the resolved-tag-first-then-fallback logic is identical too (see GsubShaper.ResolveScriptPreference,
        // duplicated here per this codebase's own GSUB/GPOS convention rather than shared cross-class).
        private static readonly IReadOnlyList<string> ScriptPreference = GsubShaper.ScriptPreference;

        private static IReadOnlyList<string> ResolveScriptPreference(string? scriptTag) =>
            scriptTag is null ? ScriptPreference : [scriptTag, .. ScriptPreference];

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
                    case 3:
                        if (gpos.GetCursiveAttachmentLookup(lookupIndex) is { } cursive)
                            ApplyCursiveAttachment(descriptor, cursive, glyphs, gdef);
                        break;
                    case 4:
                        if (gpos.GetMarkToBaseLookup(lookupIndex) is { } markToBase)
                            ApplyMarkToBase(descriptor, markToBase, glyphs, gdef);
                        break;
                    case 5:
                        if (gpos.GetMarkToLigatureLookup(lookupIndex) is { } markToLigature)
                            ApplyMarkToLigature(descriptor, markToLigature, glyphs, gdef);
                        break;
                    case 6:
                        if (gpos.GetMarkToMarkLookup(lookupIndex) is { } markToMark)
                            ApplyMarkToMark(descriptor, markToMark, glyphs);
                        break;
                    case 7:
                        if (gpos.GetContextualLookup(lookupIndex) is { } contextual)
                        {
                            CoverageTable? contextualMfs = contextual.MarkFilteringSetIndex is { } cMfs ? gdef?.GetMarkGlyphSet(cMfs) : null;
                            ApplySequenceContextLookup(descriptor, gpos, contextual.Subtables, glyphs, gdef, contextual.LookupFlag, contextualMfs);
                        }
                        break;
                    case 8:
                        if (gpos.GetChainingContextLookup(lookupIndex) is { } chaining)
                        {
                            CoverageTable? chainingMfs = chaining.MarkFilteringSetIndex is { } chMfs ? gdef?.GetMarkGlyphSet(chMfs) : null;
                            ApplySequenceContextLookup(descriptor, gpos, chaining.Subtables, glyphs, gdef, chaining.LookupFlag, chainingMfs);
                        }
                        break;
                    // Any unresolved type: not supported, left unmodified - see GposTable's own
                    // file-header gap note.
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
                // kerning is (a diacritic would paint in the wrong place entirely without it). `curs`
                // (cursive attachment - the mechanism that visually connects two joining-form glyphs
                // GsubShaper.ApplyArabicJoiningFeatures already substituted) is requested exactly when
                // this run carries resolved joining forms - it has no meaning, and nothing to attach to
                // correctly, for a run that never requested joining forms in the first place.
                var tags = new HashSet<string> { "mark", "mkmk" };
                if (key.Kerning)
                    tags.Add("kern");
                if (key.JoiningForms is { Count: > 0 })
                    tags.Add("curs");

                return gpos.GetActiveLookupIndices(ResolveScriptPreference(key.ScriptTag), tags);
            });
        }

        // internal rather than private: lets tests exercise the positioning math directly against a
        // synthetic GposTable + hand-built glyph list, without needing a font whose table directory
        // actually lists a GPOS entry (see GposPositionerSyntheticTests's own remarks).
        internal static void ApplySingleAdjustment(GposSingleAdjustmentLookup lookup, List<ShapedGlyph> glyphs)
        {
            for (int i = 0; i < glyphs.Count; i++)
                ApplySingleAdjustmentAt(lookup, glyphs, i);
        }

        private static void ApplySingleAdjustmentAt(GposSingleAdjustmentLookup lookup, List<ShapedGlyph> glyphs, int i)
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

        /// <summary>
        /// Walks the glyph run one position at a time (not skipping ahead by 2 after a match) so an
        /// overlapping pair chain (e.g. "AVA" needs both the "AV" and "VA" pairs checked) is handled
        /// correctly - matching how real kerning application works.
        /// </summary>
        // internal rather than private - see ApplySingleAdjustment's identical rationale.
        internal static void ApplyPairAdjustment(GposPairAdjustmentLookup lookup, List<ShapedGlyph> glyphs)
        {
            for (int i = 0; i < glyphs.Count - 1; i++)
                ApplyPairAdjustmentAt(lookup, glyphs, i);
        }

        private static void ApplyPairAdjustmentAt(GposPairAdjustmentLookup lookup, List<ShapedGlyph> glyphs, int i)
        {
            if (i + 1 >= glyphs.Count)
                return;

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
                ApplyMarkToBaseAt(descriptor, lookup, glyphs, i, gdef);
        }

        private static void ApplyMarkToBaseAt(OpenTypeDescriptor descriptor, GposMarkToBaseLookup lookup, List<ShapedGlyph> glyphs, int i, GdefTable? gdef)
        {
            ushort markGlyph = (ushort)glyphs[i].GlyphIndex;
            CoverageTable? markFilteringSet = lookup.MarkFilteringSetIndex is { } mfsIndex ? gdef?.GetMarkGlyphSet(mfsIndex) : null;

            foreach (GposMarkAttachmentSubtable subtable in lookup.Subtables)
            {
                int markIndex = subtable.MarkCoverage.IndexOfGlyph(markGlyph);
                if (markIndex < 0 || markIndex >= subtable.Marks.Length)
                    continue;

                int baseIndex = FindParticipatingPredecessor(glyphs, i, lookup.LookupFlag, gdef, markFilteringSet);
                if (baseIndex < 0)
                    continue;

                if (TryGetAnchor(subtable, (ushort)glyphs[baseIndex].GlyphIndex, markIndex, out GposAnchor markAnchor, out GposAnchor baseAnchor))
                {
                    ApplyMarkAnchor(descriptor, glyphs, baseIndex, i, markAnchor, baseAnchor);
                    break;
                }
            }
        }

        /// <summary>
        /// Same base-search as <see cref="ApplyMarkToBase"/>, but the found glyph is a (possibly
        /// GSUB-merged) ligature glyph whose anchors are keyed by *component*, not just by glyph id -
        /// <see cref="ResolveLigatureComponent"/> picks the right one using the ligature glyph's own
        /// <see cref="ShapedGlyph.LigatureComponentClusterStarts"/> bookkeeping (falling back to
        /// component 0 for a font-native precomposed ligature glyph GSUB never merged, which carries
        /// no such bookkeeping).
        /// </summary>
        // internal rather than private - see ApplySingleAdjustment's identical rationale.
        internal static void ApplyMarkToLigature(OpenTypeDescriptor descriptor, GposMarkToLigatureLookup lookup, List<ShapedGlyph> glyphs, GdefTable? gdef)
        {
            for (int i = 0; i < glyphs.Count; i++)
                ApplyMarkToLigatureAt(descriptor, lookup, glyphs, i, gdef);
        }

        private static void ApplyMarkToLigatureAt(OpenTypeDescriptor descriptor, GposMarkToLigatureLookup lookup, List<ShapedGlyph> glyphs, int i, GdefTable? gdef)
        {
            ushort markGlyph = (ushort)glyphs[i].GlyphIndex;
            CoverageTable? markFilteringSet = lookup.MarkFilteringSetIndex is { } mfsIndex ? gdef?.GetMarkGlyphSet(mfsIndex) : null;

            foreach (GposMarkToLigatureSubtable subtable in lookup.Subtables)
            {
                int markIndex = subtable.MarkCoverage.IndexOfGlyph(markGlyph);
                if (markIndex < 0 || markIndex >= subtable.Marks.Length)
                    continue;

                int ligIndex = FindParticipatingPredecessor(glyphs, i, lookup.LookupFlag, gdef, markFilteringSet);
                if (ligIndex < 0)
                    continue;

                ShapedGlyph ligGlyph = glyphs[ligIndex];
                int ligCoverageIndex = subtable.LigatureCoverage.IndexOfGlyph((ushort)ligGlyph.GlyphIndex);
                if (ligCoverageIndex < 0 || ligCoverageIndex >= subtable.LigatureAttachments.Length)
                    continue;

                GposLigatureAttach attach = subtable.LigatureAttachments[ligCoverageIndex];
                int componentIndex = ResolveLigatureComponent(ligGlyph, glyphs[i].ClusterStart);
                if (componentIndex < 0 || componentIndex >= attach.AnchorsByComponent.Length)
                    continue;

                (int markClass, GposAnchor markAnchor) = subtable.Marks[markIndex];
                GposAnchor?[] anchorsForComponent = attach.AnchorsByComponent[componentIndex];
                if (markClass < 0 || markClass >= anchorsForComponent.Length || anchorsForComponent[markClass] is not { } ligAnchor)
                    continue;

                ApplyMarkAnchor(descriptor, glyphs, ligIndex, i, markAnchor, ligAnchor);
                break;
            }
        }

        /// <summary>
        /// Picks which ligature component <paramref name="markClusterStart"/> (the mark's own source
        /// text position) belongs to: the component whose own <see cref="ShapedGlyph.ClusterStart"/>
        /// is the closest one at-or-before it - i.e. the last component the mark's source character
        /// could plausibly be attached to in reading order. Falls back to component 0 when
        /// <paramref name="ligatureGlyph"/> carries no bookkeeping at all (never went through a GSUB
        /// ligature merge - e.g. a font's own precomposed ligature glyph, reached directly via cmap).
        /// This "nearest at-or-before cluster start" rule is this codebase's own design for adapting
        /// real shapers' `lig_id`/`lig_component` glyph properties to its cluster-tracking model - the
        /// spec doesn't mandate a specific algorithm here, so treat this as a scoped heuristic that may
        /// need revisiting once complex-script joining (Arabic/Indic, tracked separately) starts
        /// producing richer ligature-merge scenarios than today's simple component-adjacent-mark case.
        /// </summary>
        private static int ResolveLigatureComponent(ShapedGlyph ligatureGlyph, int markClusterStart)
        {
            if (ligatureGlyph.LigatureComponentClusterStarts is not { } starts || starts.Length == 0)
                return 0;

            int best = 0;
            for (int c = 0; c < starts.Length; c++)
            {
                if (starts[c] <= markClusterStart)
                    best = c;
                else
                    break;
            }
            return best;
        }

        /// <summary>
        /// A Type 6 lookup always targets the immediately preceding glyph (mark-to-mark chains are,
        /// by construction, contiguous runs of marks) - no backward-skip search is needed.
        /// </summary>
        // internal rather than private - see ApplySingleAdjustment's identical rationale.
        internal static void ApplyMarkToMark(OpenTypeDescriptor descriptor, GposMarkToMarkLookup lookup, List<ShapedGlyph> glyphs)
        {
            for (int i = 1; i < glyphs.Count; i++)
                ApplyMarkToMarkAt(descriptor, lookup, glyphs, i);
        }

        private static void ApplyMarkToMarkAt(OpenTypeDescriptor descriptor, GposMarkToMarkLookup lookup, List<ShapedGlyph> glyphs, int i)
        {
            if (i < 1)
                return;

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

        /// <summary>
        /// Applies one Lookup Type 3 (Cursive Attachment) lookup: for each participating glyph pair
        /// (i, j) where i has an exit anchor and j - the nearest subsequent participating glyph, per
        /// <paramref name="lookup"/>'s own `lookupFlag`/GDEF filtering - has an entry anchor, connects
        /// them (see <see cref="TryApplyCursivePair"/> for the real, HarfBuzz-derived math), and the
        /// cross-stream (Y) offset goes on whichever glyph the lookup's `lookupFlag` RIGHT_TO_LEFT bit
        /// selects.
        ///
        /// LTR (bit clear): pairs are resolved left-to-right, and Y goes on the entry glyph (j) - each
        /// glyph's exit connects forward to the next glyph's entry, matching normal reading order.
        /// RTL (bit set): pairs are resolved <em>right-to-left</em> instead (iterating from the end of
        /// the run backward) and Y goes on the exit glyph (i) - per spec, corrections cascade backward
        /// from the last glyph in a connected chain, which this walk order achieves without a separate
        /// connected-component pass, since by the time a pair (i, j) is resolved, j (further right) has
        /// already had any of its own corrections (as the "i" of a later-in-iteration-order pair)
        /// finalized. This is the lookup's own `lookupFlag` bit, a separate concept from the *buffer's*
        /// own text direction that <see cref="TryApplyCursivePair"/>'s main-direction (X) formula is
        /// hardcoded for - see that method's own remarks on why RTL-only is the correct scope here (the
        /// only caller of this whole lookup type is Arabic-family joining, which is always RTL-treated).
        /// Validated against a real font (Aref Ruqaa) whose cursive baseline connections are its primary
        /// joining mechanism, cross-checked against real HarfBuzz's own output for the same text+font -
        /// see this fix's own recent-fixes entry.
        /// </summary>
        internal static void ApplyCursiveAttachment(OpenTypeDescriptor descriptor, GposCursiveAttachmentLookup lookup, List<ShapedGlyph> glyphs, GdefTable? gdef)
        {
            bool rightToLeft = (lookup.LookupFlag & RightToLeftLookupFlag) != 0;
            if (!rightToLeft)
            {
                for (int i = 0; i < glyphs.Count - 1; i++)
                    TryApplyCursivePair(descriptor, lookup, glyphs, i, gdef, rightToLeft: false);
            }
            else
            {
                for (int i = glyphs.Count - 2; i >= 0; i--)
                    TryApplyCursivePair(descriptor, lookup, glyphs, i, gdef, rightToLeft: true);
            }
        }

        // Ported from HarfBuzz's src/OT/Layout/GPOS/CursivePosFormat1.hh (CursivePosFormat1::apply,
        // the HB_DIRECTION_RTL branch of its main-direction adjustment), retrieved 2026-09-04 from
        // https://github.com/harfbuzz/harfbuzz/blob/main/src/OT/Layout/GPOS/CursivePosFormat1.hh -
        // Copyright © 2010-2022 Google, Inc. ("Old MIT" license - see HarfBuzz's own COPYING file;
        // functionally MIT-equivalent - see THIRD-PARTY-LICENSES.md for the full notice and how this
        // fits into PeachPDF's own licensing).
        //
        // A first version of this method derived its formula directly from the OpenType spec's own
        // ("adjusts the x-coordinate so the two points coincide") prose, computing one combined delta
        // from the pen-distance between i and j and adding it to i's own advance. That formula is
        // spec-plausible but is NOT what real fonts are authored/tested against: it produced wildly
        // wrong (often deeply negative, collapsing an entire word's measured width to ~0) advances
        // against a real font (Aref Ruqaa) that actually relies on cursive attachment - caught by
        // rasterizing real output and finding whole words render blank, then confirmed as a genuine
        // divergence from real HarfBuzz's own output for the same text+font (not just a PeachPDF
        // idiosyncrasy) before rewriting this method to match HarfBuzz's real algorithm instead of a
        // second re-derivation from spec text. See this fix's own recent-fixes entry.
        //
        // The real formula treats i and j's own corrections as fully independent: each depends only on
        // that glyph's own exit/entry anchor (an intrinsic font constant), never on the other glyph's
        // position or on the pen-distance between them - which is also, as a side effect, what makes
        // both corrections safe under a later glyph-list reversal (OpenTypeDescriptor.ReverseGlyphsForDisplay's
        // plain interval-mirror) with no special-casing needed, unlike mark attachment's XOffset (see
        // ShapedGlyph.AttachedToIndex's own remarks).
        private static void TryApplyCursivePair(OpenTypeDescriptor descriptor, GposCursiveAttachmentLookup lookup, List<ShapedGlyph> glyphs, int i, GdefTable? gdef, bool rightToLeft)
        {
            if (!TryGetExitAnchor(lookup, (ushort)glyphs[i].GlyphIndex, out GposAnchor exitAnchor))
                return;

            CoverageTable? markFilteringSet = lookup.MarkFilteringSetIndex is { } mfsIndex ? gdef?.GetMarkGlyphSet(mfsIndex) : null;
            int j = FindParticipatingSuccessor(glyphs, i, lookup.LookupFlag, gdef, markFilteringSet);
            if (j < 0 || !TryGetEntryAnchor(lookup, (ushort)glyphs[j].GlyphIndex, out GposAnchor entryAnchor))
                return;

            // i's own exit point is pulled back to the run's current end: its advance (and its own
            // offset, if any earlier lookup already gave it one) both shrink by the same amount, so its
            // painted origin is unchanged but its exit anchor now sits exactly at the pen position the
            // *next* glyph will start from.
            double d = exitAnchor.X + glyphs[i].XOffset;
            glyphs[i] = glyphs[i] with
            {
                XAdvanceDelta = glyphs[i].XAdvanceDelta - d,
                XOffset = glyphs[i].XOffset - d,
            };

            // j's own advance becomes exactly its entry anchor's own X (plus whatever offset it already
            // carries) - not an adjustment to its natural hmtx width, a replacement of it (matching
            // HarfBuzz's own plain assignment, not addition) - since what should extend past j, for a
            // cursively-connected run, is measured from j's own entry point outward, not from its own
            // full nominal glyph box.
            double jNominalWidth = descriptor.GlyphIndexToWidth(glyphs[j].GlyphIndex);
            glyphs[j] = glyphs[j] with { XAdvanceDelta = entryAnchor.X + glyphs[j].XOffset - jNominalWidth };

            if (!rightToLeft)
                glyphs[j] = glyphs[j] with { YOffset = exitAnchor.Y - entryAnchor.Y + glyphs[i].YOffset };
            else
                glyphs[i] = glyphs[i] with { YOffset = entryAnchor.Y - exitAnchor.Y + glyphs[j].YOffset };
        }

        private static bool TryGetExitAnchor(GposCursiveAttachmentLookup lookup, ushort glyphId, out GposAnchor anchor)
        {
            foreach (GposCursiveAttachmentSubtable subtable in lookup.Subtables)
            {
                int index = subtable.Coverage.IndexOfGlyph(glyphId);
                if (index < 0 || index >= subtable.EntryExitRecords.Length)
                    continue;
                if (subtable.EntryExitRecords[index].ExitAnchor is { } exit)
                {
                    anchor = exit;
                    return true;
                }
            }
            anchor = default;
            return false;
        }

        private static bool TryGetEntryAnchor(GposCursiveAttachmentLookup lookup, ushort glyphId, out GposAnchor anchor)
        {
            foreach (GposCursiveAttachmentSubtable subtable in lookup.Subtables)
            {
                int index = subtable.Coverage.IndexOfGlyph(glyphId);
                if (index < 0 || index >= subtable.EntryExitRecords.Length)
                    continue;
                if (subtable.EntryExitRecords[index].EntryAnchor is { } entry)
                {
                    anchor = entry;
                    return true;
                }
            }
            anchor = default;
            return false;
        }

        /// <summary>The next participating glyph after <paramref name="fromIndex"/> (per
        /// <see cref="GlyphSequenceFilter.Participates"/>), or -1 if none - a single-result use of the
        /// shared <see cref="GsubShaper.FindParticipatingIndices"/> walk, the same primitive Types 7/8's
        /// own matcher above uses, rather than a second hand-written forward scan.</summary>
        private static int FindParticipatingSuccessor(List<ShapedGlyph> glyphs, int fromIndex, ushort lookupFlag, GdefTable? gdef, CoverageTable? markFilteringSet = null) =>
            GsubShaper.FindParticipatingIndices(glyphs, fromIndex + 1, +1, 1, lookupFlag, gdef, markFilteringSet) is { } indices ? indices[0] : -1;

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

        /// <summary>The nearest participating glyph before <paramref name="fromIndex"/>, mirroring
        /// <see cref="FindParticipatingSuccessor"/>.</summary>
        private static int FindParticipatingPredecessor(List<ShapedGlyph> glyphs, int fromIndex, ushort lookupFlag, GdefTable? gdef, CoverageTable? markFilteringSet = null) =>
            GsubShaper.FindParticipatingIndices(glyphs, fromIndex - 1, -1, 1, lookupFlag, gdef, markFilteringSet) is { } indices ? indices[0] : -1;

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
        /// Also records <see cref="ShapedGlyph.AttachedToIndex"/> = <paramref name="baseIndex"/> - the
        /// computed <see cref="ShapedGlyph.XOffset"/> bakes in the pen-distance to the base under
        /// *this* walk order, so a caller that later reorders the glyph list (see
        /// <see cref="OpenTypeDescriptor.Shape"/>'s <c>ReverseForDisplay</c> handling) needs this
        /// back-reference to recompute the offset for the new order rather than silently reusing a
        /// value baked in for the old one.
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
                AttachedToIndex = baseIndex,
            };
        }

        /// <summary>
        /// Applies one contextual (Lookup Type 7) or chaining-context (Lookup Type 8) lookup's
        /// subtables as a single left-to-right pass over every glyph position - the GPOS mirror of
        /// <see cref="GsubShaper.ApplySequenceContextLookup"/>, sharing its skip-aware backtrack/
        /// input/lookahead walk (<see cref="GsubShaper.FindParticipatingIndices"/>) since that walk
        /// is pure <see cref="ShapedGlyph"/>/`lookupFlag`/GDEF logic with nothing GSUB-specific about
        /// it. Unlike GSUB's version, a nested GPOS lookup (Types 1/2/4/6) only ever adjusts
        /// positioning - it never changes <paramref name="glyphs"/>'s count - so there is no
        /// glyph-count-delta bookkeeping to track between records; each input position's real
        /// glyph-list index, once found by the skip-aware match, stays valid for every subsequent
        /// record in the same match.
        /// </summary>
        internal static void ApplySequenceContextLookup(OpenTypeDescriptor descriptor, GposTable gpos,
            IReadOnlyList<GposSequenceContextSubtable> subtables, List<ShapedGlyph> glyphs, GdefTable? gdef,
            ushort lookupFlag, CoverageTable? markFilteringSet)
        {
            // Resumes past the whole matched span, same as GsubShaper's own outer walk (see its doc
            // comment) - a plain per-position `for` loop would let an already-matched-and-repositioned
            // glyph be tried again as a fresh anchor by a second, shorter/differently-shaped rule in
            // the same lookup, double-applying a nested lookup's correction to it.
            int i = 0;
            while (i < glyphs.Count)
            {
                int consumed = TryApplySequenceContextAt(descriptor, gpos, subtables, glyphs, i, gdef, lookupFlag, markFilteringSet);
                i += consumed > 0 ? consumed : 1;
            }
        }

        private static int TryApplySequenceContextAt(OpenTypeDescriptor descriptor, GposTable gpos,
            IReadOnlyList<GposSequenceContextSubtable> subtables, List<ShapedGlyph> glyphs, int pos, GdefTable? gdef,
            ushort lookupFlag, CoverageTable? markFilteringSet)
        {
            foreach (GposSequenceContextSubtable subtable in subtables)
            {
                if (TryMatchSequenceContext(subtable, glyphs, pos, lookupFlag, gdef, markFilteringSet) is not
                    (int[] inputIndices, GposSequenceLookupRecord[] records))
                    continue;

                ApplyMatchedLookups(descriptor, gpos, glyphs, inputIndices, records, depth: 0, gdef);

                // No delta bookkeeping is needed here (unlike GSUB - see ApplyMatchedLookups' own doc
                // comment): a nested GPOS lookup never changes glyph count, so inputIndices' own real
                // indices are still accurate after the nested application, and the last one tells us
                // exactly how far to resume scanning past this match.
                int lastIndex = inputIndices.Length > 0 ? inputIndices[^1] : pos;
                return Math.Max(1, lastIndex - pos + 1);
            }

            return 0;
        }

        private static (int[] InputIndices, GposSequenceLookupRecord[] Records)? TryMatchSequenceContext(
            GposSequenceContextSubtable subtable, List<ShapedGlyph> glyphs, int pos, ushort lookupFlag, GdefTable? gdef, CoverageTable? markFilteringSet)
        {
            switch (subtable.Format)
            {
                case GposSequenceContextFormat.Glyph:
                {
                    if (subtable.Coverage is not { } coverage || subtable.RuleSets is not { } ruleSets)
                        return null;
                    int coverageIndex = coverage.IndexOfGlyph((ushort)glyphs[pos].GlyphIndex);
                    if (coverageIndex < 0 || coverageIndex >= ruleSets.Length)
                        return null;
                    foreach (GposSequenceRule rule in ruleSets[coverageIndex])
                    {
                        if (TryMatchRule(rule, glyphs, pos, matchGlyph: true, null, null, null, lookupFlag, gdef, markFilteringSet) is { } indices)
                            return (indices, rule.SeqLookupRecords);
                    }
                    return null;
                }

                case GposSequenceContextFormat.Class:
                {
                    if (subtable.Coverage is not { } coverage2 || subtable.InputClassDef is not { } inputClassDef
                        || subtable.RuleSets is not { } classRuleSets)
                        return null;
                    if (coverage2.IndexOfGlyph((ushort)glyphs[pos].GlyphIndex) < 0)
                        return null;
                    int classValue = inputClassDef.GetClass((ushort)glyphs[pos].GlyphIndex);
                    if (classValue < 0 || classValue >= classRuleSets.Length)
                        return null;
                    foreach (GposSequenceRule rule in classRuleSets[classValue])
                    {
                        if (TryMatchRule(rule, glyphs, pos, matchGlyph: false, inputClassDef, subtable.BacktrackClassDef, subtable.LookaheadClassDef,
                                lookupFlag, gdef, markFilteringSet) is { } indices)
                            return (indices, rule.SeqLookupRecords);
                    }
                    return null;
                }

                case GposSequenceContextFormat.Coverage:
                {
                    if (subtable.InputCoverages is not { } inputCoverages || subtable.SeqLookupRecords is not { } records)
                        return null;
                    if (TryMatchCoverageSequence(subtable.BacktrackCoverages, inputCoverages, subtable.LookaheadCoverages, glyphs, pos,
                            lookupFlag, gdef, markFilteringSet) is not { } coverageIndices)
                        return null;
                    return (coverageIndices, records);
                }

                default:
                    return null;
            }
        }

        /// <summary>Same matching approach as <see cref="GsubShaper"/>'s equivalent (see its own doc
        /// comment) - shares <see cref="GsubShaper.FindParticipatingIndices"/> rather than
        /// duplicating the skip-aware walk itself, since only the surrounding table-reading/dispatch
        /// code is GSUB/GPOS-specific here.</summary>
        private static int[]? TryMatchRule(GposSequenceRule rule, List<ShapedGlyph> glyphs, int pos, bool matchGlyph,
            ClassDefTable? inputClassDef, ClassDefTable? backtrackClassDef, ClassDefTable? lookaheadClassDef,
            ushort lookupFlag, GdefTable? gdef, CoverageTable? markFilteringSet)
        {
            if (GsubShaper.FindParticipatingIndices(glyphs, pos - 1, -1, rule.Backtrack.Length, lookupFlag, gdef, markFilteringSet) is not { } backtrackIndices)
                return null;
            for (int k = 0; k < rule.Backtrack.Length; k++)
            {
                if (!MatchesRulePosition(glyphs[backtrackIndices[k]].GlyphIndex, rule.Backtrack[k], matchGlyph, backtrackClassDef))
                    return null;
            }

            var inputIndices = new int[rule.Input.Length + 1];
            inputIndices[0] = pos;
            if (GsubShaper.FindParticipatingIndices(glyphs, pos + 1, +1, rule.Input.Length, lookupFlag, gdef, markFilteringSet) is not { } restInput)
                return null;
            for (int k = 0; k < rule.Input.Length; k++)
            {
                if (!MatchesRulePosition(glyphs[restInput[k]].GlyphIndex, rule.Input[k], matchGlyph, inputClassDef))
                    return null;
                inputIndices[k + 1] = restInput[k];
            }

            int lookaheadStart = inputIndices[^1] + 1;
            if (GsubShaper.FindParticipatingIndices(glyphs, lookaheadStart, +1, rule.Lookahead.Length, lookupFlag, gdef, markFilteringSet) is not { } lookaheadIndices)
                return null;
            for (int k = 0; k < rule.Lookahead.Length; k++)
            {
                if (!MatchesRulePosition(glyphs[lookaheadIndices[k]].GlyphIndex, rule.Lookahead[k], matchGlyph, lookaheadClassDef))
                    return null;
            }

            return inputIndices;
        }

        private static bool MatchesRulePosition(int glyphIndex, ushort expected, bool matchGlyph, ClassDefTable? classDef)
            => matchGlyph ? glyphIndex == expected : classDef is not null && classDef.GetClass((ushort)glyphIndex) == expected;

        private static int[]? TryMatchCoverageSequence(
            CoverageTable[]? backtrack, CoverageTable[] input, CoverageTable[]? lookahead, List<ShapedGlyph> glyphs, int pos,
            ushort lookupFlag, GdefTable? gdef, CoverageTable? markFilteringSet)
        {
            backtrack ??= [];
            lookahead ??= [];

            if (GsubShaper.FindParticipatingIndices(glyphs, pos - 1, -1, backtrack.Length, lookupFlag, gdef, markFilteringSet) is not { } backtrackIndices)
                return null;
            for (int k = 0; k < backtrack.Length; k++)
            {
                if (backtrack[k].IndexOfGlyph((ushort)glyphs[backtrackIndices[k]].GlyphIndex) < 0)
                    return null;
            }

            if (input.Length == 0 || pos >= glyphs.Count || input[0].IndexOfGlyph((ushort)glyphs[pos].GlyphIndex) < 0)
                return null;

            var inputIndices = new int[input.Length];
            inputIndices[0] = pos;
            if (input.Length > 1)
            {
                if (GsubShaper.FindParticipatingIndices(glyphs, pos + 1, +1, input.Length - 1, lookupFlag, gdef, markFilteringSet) is not { } restInput)
                    return null;
                for (int k = 1; k < input.Length; k++)
                {
                    if (input[k].IndexOfGlyph((ushort)glyphs[restInput[k - 1]].GlyphIndex) < 0)
                        return null;
                    inputIndices[k] = restInput[k - 1];
                }
            }

            int lookaheadStart = inputIndices[^1] + 1;
            if (GsubShaper.FindParticipatingIndices(glyphs, lookaheadStart, +1, lookahead.Length, lookupFlag, gdef, markFilteringSet) is not { } lookaheadIndices)
                return null;
            for (int k = 0; k < lookahead.Length; k++)
            {
                if (lookahead[k].IndexOfGlyph((ushort)glyphs[lookaheadIndices[k]].GlyphIndex) < 0)
                    return null;
            }

            return inputIndices;
        }

        /// <summary>
        /// Applies <paramref name="records"/> (in the order given) to the glyph sequence whose
        /// matched input positions' real glyph-list indices are <paramref name="inputIndices"/> - no
        /// delta bookkeeping is needed (unlike GSUB's own <c>ApplyMatchedLookups</c>) since a nested
        /// GPOS lookup never changes <paramref name="glyphs"/>'s count, only its positioning deltas.
        /// Nested lookup types 1/2/3/4/5/6 are supported, matching GSUB's own "unsupported nested
        /// type is silently skipped" convention (a nested Type 7/8 is the one kind actually excluded,
        /// same rationale as GSUB never nesting a contextual/chaining lookup inside another one);
        /// <paramref name="depth"/> guards against a pathological/adversarial font nesting indefinitely
        /// (mirroring the same concern GSUB's version guards against, even though GPOS's own nested
        /// lookups can't recurse into more Type 7/8 lookups - see <see cref="ApplyNestedLookup"/>).
        /// </summary>
        private static void ApplyMatchedLookups(OpenTypeDescriptor descriptor, GposTable gpos, List<ShapedGlyph> glyphs,
            int[] inputIndices, GposSequenceLookupRecord[] records, int depth, GdefTable? gdef)
        {
            if (depth >= MaxNestedContextDepth || inputIndices.Length == 0)
                return;

            foreach (GposSequenceLookupRecord record in records)
            {
                if (record.SequenceIndex < 0 || record.SequenceIndex >= inputIndices.Length)
                    continue;

                int realIndex = inputIndices[record.SequenceIndex];
                if (realIndex < 0 || realIndex >= glyphs.Count)
                    continue;

                ApplyNestedLookup(descriptor, gpos, glyphs, realIndex, record.LookupListIndex, gdef);
            }
        }

        private static void ApplyNestedLookup(OpenTypeDescriptor descriptor, GposTable gpos, List<ShapedGlyph> glyphs, int position, int lookupListIndex, GdefTable? gdef)
        {
            switch (gpos.GetResolvedLookupType(lookupListIndex))
            {
                case 1:
                    if (gpos.GetSingleAdjustmentLookup(lookupListIndex) is { } single)
                        ApplySingleAdjustmentAt(single, glyphs, position);
                    break;
                case 2:
                    if (gpos.GetPairAdjustmentLookup(lookupListIndex) is { } pair)
                        ApplyPairAdjustmentAt(pair, glyphs, position);
                    break;
                case 3:
                    if (gpos.GetCursiveAttachmentLookup(lookupListIndex) is { } cursive)
                        TryApplyCursivePair(descriptor, cursive, glyphs, position, gdef, (cursive.LookupFlag & RightToLeftLookupFlag) != 0);
                    break;
                case 4:
                    if (gpos.GetMarkToBaseLookup(lookupListIndex) is { } markToBase)
                        ApplyMarkToBaseAt(descriptor, markToBase, glyphs, position, gdef);
                    break;
                case 5:
                    if (gpos.GetMarkToLigatureLookup(lookupListIndex) is { } markToLigature)
                        ApplyMarkToLigatureAt(descriptor, markToLigature, glyphs, position, gdef);
                    break;
                case 6:
                    if (gpos.GetMarkToMarkLookup(lookupListIndex) is { } markToMark)
                        ApplyMarkToMarkAt(descriptor, markToMark, glyphs, position);
                    break;
                // 7, 8, unresolved: a nested contextual/chaining lookup is not supported (see
                // ApplyMatchedLookups' own doc comment) - left unmodified.
            }
        }
    }
}

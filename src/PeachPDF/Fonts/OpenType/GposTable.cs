#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Reader for the OpenType `GPOS` (Glyph Positioning) table: its own ScriptList/FeatureList/LookupList
// common tables (independent of GSUB's - GPOS has its own tag registry, e.g. `kern`/`mark`/`mkmk`),
// Lookup Type 1 (Single Adjustment, formats 1/2), Lookup Type 2 (Pair Adjustment, formats 1/2),
// Lookup Type 4 (MarkToBase Attachment), Lookup Type 6 (MarkToMark Attachment), and Lookup Types 7/8
// (Context/Chained Context Positioning, formats 1/2/3 - byte-for-byte the same common tables GSUB's
// own Lookup Types 5/6 use, just referencing GPOS lookups instead of GSUB ones) subtables - Type 9
// (Extension Positioning) is unwrapped down to the real subtable it wraps when present (GPOS's own
// extension type; GSUB's is type 7 - see `GsubTable`, and don't confuse the two: this is the one
// place in this codebase where "9" is the correct Extension type, deliberately). This is the subset
// needed for kerning (`kern`, via Types 1/2), mark-to-base/mark-to-mark positioning (`mark`/`mkmk`,
// via Types 4/6), and contextual positioning - see `PeachPDF.Text.GposPositioner`.
//
// Also implemented: Lookup Type 3 (Cursive Attachment, `CursivePosFormat1`) - requested via the `curs`
// feature tag whenever a run carries resolved Arabic-family joining forms (see
// `GposPositioner.GetActiveLookupIndices`/`TryApplyCursivePair`, the latter a direct port of real
// HarfBuzz's own main-direction formula, verified against a real cursive-attachment font - see
// .claude/accepted-gaps/no-text-shaping.md); and Lookup Type 5 (MarkToLigature Attachment,
// `MarkLigPosFormat1`) - identifying which ligature *component* a
// mark attaches to relies on `PeachPDF.Text.ShapedGlyph.LigatureComponentClusterStarts`, bookkeeping
// GSUB's own ligature-merge logic (`GsubShaper.TryMatchLigature`) now carries forward for exactly this.
//
// `lookupFlag`'s GDEF-based mark filtering (the plain ignore-bits) is honored everywhere via GDEF glyph
// classification. The extra, more targeted `markFilteringSet` field (present only when lookupFlag's
// USE_MARK_FILTERING_SET bit is set - see `ReadLookupHeader`) narrows that further to one specific
// GDEF `MarkGlyphSetsDef` set; it is read for every lookup type whose own application actually does a
// backward/forward participant *search* to skip past non-participating glyphs - Type 3's cursive
// successor search and Type 4/5's own base/ligature-predecessor search (see
// `PeachPDF.Text.GposPositioner`'s `TryApplyCursivePair`/`ApplyMarkToBaseAt`/`ApplyMarkToLigatureAt`),
// plus Types 7/8's own skip-aware matching. Type 6 (MarkToMark) always targets the immediately
// preceding glyph with no search at all, so it has nothing for a mark filtering set to narrow.
//
// https://learn.microsoft.com/en-us/typography/opentype/spec/gpos
// https://learn.microsoft.com/en-us/typography/opentype/spec/chapter2
//
#endregion

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace PeachPDF.Fonts.OpenType
{
    /// <summary>One GPOS `ValueRecord`, resolved to its four positioning fields - device-table
    /// (hinting) offsets are read (for correct cursor alignment) but never resolved, since they're
    /// ppem-grid pixel adjustments meaningless to a vector PDF renderer at arbitrary scale.</summary>
    internal readonly record struct GposValueRecord(short XPlacement, short YPlacement, short XAdvance, short YAdvance);

    /// <summary>One GPOS `Anchor` table (formats 1/2/3), resolved to its x/y coordinate - format 2's
    /// contour-point index and format 3's device offsets are hinting-only refinements, not read.</summary>
    internal readonly record struct GposAnchor(short X, short Y);

    /// <summary>One Single Adjustment subtable: a <see cref="Coverage"/> table over adjusted glyphs,
    /// plus either one shared <see cref="GposValueRecord"/> (format 1, <see cref="IsUniform"/>) or
    /// one per covered glyph (format 2).</summary>
    internal sealed class GposSingleAdjustmentSubtable
    {
        public required CoverageTable Coverage { get; init; }
        public required GposValueRecord[] Values { get; init; }
        public required bool IsUniform { get; init; }

        public bool TryGetValue(ushort glyphId, out GposValueRecord value)
        {
            int index = Coverage.IndexOfGlyph(glyphId);
            if (index < 0)
            {
                value = default;
                return false;
            }

            if (IsUniform)
            {
                value = Values[0];
                return true;
            }

            if (index < Values.Length)
            {
                value = Values[index];
                return true;
            }

            value = default;
            return false;
        }
    }

    /// <summary>A GPOS lookup of type 1 (or type 9 wrapping type 1), as one or more subtables.</summary>
    internal sealed class GposSingleAdjustmentLookup
    {
        public required IReadOnlyList<GposSingleAdjustmentSubtable> Subtables { get; init; }
        public ushort LookupFlag { get; init; }
    }

    /// <summary>One <c>EntryExitRecord</c>: either anchor may be absent (a glyph can have only an
    /// entry, only an exit, both, or - if it's covered but has neither - effectively no cursive
    /// attachment at all).</summary>
    internal readonly record struct GposEntryExitRecord(GposAnchor? EntryAnchor, GposAnchor? ExitAnchor);

    /// <summary>One `CursivePosFormat1` subtable: a <see cref="Coverage"/> table over every glyph this
    /// subtable defines cursive attachment behavior for, each (by coverage index) naming its own
    /// entry/exit anchor pair.</summary>
    internal sealed class GposCursiveAttachmentSubtable
    {
        public required CoverageTable Coverage { get; init; }
        public required GposEntryExitRecord[] EntryExitRecords { get; init; }
    }

    /// <summary>A GPOS lookup of type 3 (or type 9 wrapping type 3), as one or more subtables.</summary>
    internal sealed class GposCursiveAttachmentLookup
    {
        public required IReadOnlyList<GposCursiveAttachmentSubtable> Subtables { get; init; }
        public ushort LookupFlag { get; init; }
        public int? MarkFilteringSetIndex { get; init; }
    }

    internal readonly record struct GposPairValueRecord(ushort SecondGlyph, GposValueRecord Value1, GposValueRecord Value2);

    /// <summary>One Pair Adjustment subtable - either format 1 (<see cref="PairSets"/>, exact glyph
    /// pairs keyed by <see cref="Coverage"/> index) or format 2 (<see cref="ClassDef1"/>/
    /// <see cref="ClassDef2"/>, a flat class-pair value table; the first glyph must still pass
    /// <see cref="Coverage"/> even in format 2 - a ClassDef alone doesn't gate applicability).</summary>
    internal sealed class GposPairAdjustmentSubtable
    {
        public required CoverageTable Coverage { get; init; }
        public GposPairValueRecord[][]? PairSets { get; init; }
        public ClassDefTable? ClassDef1 { get; init; }
        public ClassDefTable? ClassDef2 { get; init; }
        public int Class2Count { get; init; }
        public (GposValueRecord Value1, GposValueRecord Value2)[]? ClassValues { get; init; }

        private bool IsFormat2 => ClassDef1 is not null;

        public bool TryGetValues(ushort first, ushort second, out GposValueRecord value1, out GposValueRecord value2)
        {
            value1 = default;
            value2 = default;

            int coverageIndex = Coverage.IndexOfGlyph(first);
            if (coverageIndex < 0)
                return false;

            if (!IsFormat2)
            {
                if (PairSets is null || coverageIndex >= PairSets.Length)
                    return false;

                foreach (GposPairValueRecord pair in PairSets[coverageIndex])
                {
                    if (pair.SecondGlyph == second)
                    {
                        value1 = pair.Value1;
                        value2 = pair.Value2;
                        return true;
                    }
                }
                return false;
            }

            int class1 = ClassDef1!.GetClass(first);
            int class2 = ClassDef2!.GetClass(second);
            int index = class1 * Class2Count + class2;
            if (ClassValues is null || index < 0 || index >= ClassValues.Length)
                return false;

            (value1, value2) = ClassValues[index];
            return true;
        }
    }

    /// <summary>A GPOS lookup of type 2 (or type 9 wrapping type 2), as one or more subtables.</summary>
    internal sealed class GposPairAdjustmentLookup
    {
        public required IReadOnlyList<GposPairAdjustmentSubtable> Subtables { get; init; }
        public ushort LookupFlag { get; init; }
    }

    /// <summary>
    /// One MarkToBase (Lookup Type 4, `MarkBasePosFormat1`) or MarkToMark (Lookup Type 6,
    /// `MarkMarkPosFormat1`) subtable - both formats are byte-identical (mark coverage, base/mark2
    /// coverage, mark-class count, `MarkArray`, `BaseArray`/`Mark2Array`), so one reader and one
    /// model serve both. A lookup's own <see cref="Marks"/> `MarkClass` is a value purely local to
    /// this subtable's own <see cref="Marks"/>/<see cref="BaseAnchorsByClass"/> pairing (which of a
    /// base's several anchors a given mark aligns to) - a different concept from GDEF's
    /// `MarkAttachClassDef` (a font-wide classification used for `lookupFlag`'s mark-attachment-class
    /// filter, see <see cref="PeachPDF.Text.GlyphSequenceFilter"/>); don't conflate the two.
    /// </summary>
    internal sealed class GposMarkAttachmentSubtable
    {
        public required CoverageTable MarkCoverage { get; init; }
        public required CoverageTable BaseCoverage { get; init; }
        public required int MarkClassCount { get; init; }
        public required (int MarkClass, GposAnchor Anchor)[] Marks { get; init; }
        public required GposAnchor?[][] BaseAnchorsByClass { get; init; }
    }

    /// <summary>A GPOS lookup of type 4 (or type 9 wrapping type 4), as one or more subtables.</summary>
    internal sealed class GposMarkToBaseLookup
    {
        public required IReadOnlyList<GposMarkAttachmentSubtable> Subtables { get; init; }
        public ushort LookupFlag { get; init; }
        public int? MarkFilteringSetIndex { get; init; }
    }

    /// <summary>One ligature glyph's `LigatureAttach` table: one anchor-table-set per ligature
    /// *component* (not per ligature glyph as a whole) - <see cref="AnchorsByComponent"/>[component][markClass].</summary>
    internal sealed class GposLigatureAttach
    {
        public required GposAnchor?[][] AnchorsByComponent { get; init; }
    }

    /// <summary>One MarkToLigature (Lookup Type 5, `MarkLigPosFormat1`) subtable - `MarkArray` is the
    /// same shared structure Types 4/6 use (see <see cref="GposMarkAttachmentSubtable.Marks"/>'s own
    /// remarks), reused directly via <c>GposTable.ReadMarkArray</c>; `LigatureArray` is the one piece
    /// genuinely specific to this lookup type.</summary>
    internal sealed class GposMarkToLigatureSubtable
    {
        public required CoverageTable MarkCoverage { get; init; }
        public required CoverageTable LigatureCoverage { get; init; }
        public required int MarkClassCount { get; init; }
        public required (int MarkClass, GposAnchor Anchor)[] Marks { get; init; }

        /// <summary>By <see cref="LigatureCoverage"/> index.</summary>
        public required GposLigatureAttach[] LigatureAttachments { get; init; }
    }

    /// <summary>A GPOS lookup of type 5 (or type 9 wrapping type 5), as one or more subtables.</summary>
    internal sealed class GposMarkToLigatureLookup
    {
        public required IReadOnlyList<GposMarkToLigatureSubtable> Subtables { get; init; }
        public ushort LookupFlag { get; init; }
        public int? MarkFilteringSetIndex { get; init; }
    }

    /// <summary>A GPOS lookup of type 6 (or type 9 wrapping type 6), as one or more subtables.</summary>
    internal sealed class GposMarkToMarkLookup
    {
        public required IReadOnlyList<GposMarkAttachmentSubtable> Subtables { get; init; }
        // No MarkFilteringSetIndex: Type 6 always targets the immediately preceding glyph with no
        // participant search to narrow - see this file's own header note.
        public ushort LookupFlag { get; init; }
    }

    /// <summary>One <c>SequenceLookupRecord</c>/<c>ChainedSequenceLookupRecord</c> - byte-identical to
    /// <see cref="PeachPDF.Text.GsubShaper"/>'s GSUB equivalent, except <see cref="LookupListIndex"/>
    /// here indexes GPOS's own <c>LookupList</c> (positioning lookups), not GSUB's.</summary>
    internal readonly record struct GposSequenceLookupRecord(int SequenceIndex, int LookupListIndex);

    /// <summary>One contextual/chaining rule - same shape as GSUB's <c>GsubSequenceRule</c> (see its
    /// own doc comment for the field semantics), duplicated rather than shared per this file's own
    /// header note on GSUB/GPOS table-reading duplication.</summary>
    internal sealed class GposSequenceRule
    {
        public required ushort[] Backtrack { get; init; }
        public required ushort[] Input { get; init; }
        public required ushort[] Lookahead { get; init; }
        public required GposSequenceLookupRecord[] SeqLookupRecords { get; init; }
    }

    internal enum GposSequenceContextFormat
    {
        Glyph = 1,
        Class = 2,
        Coverage = 3,
    }

    /// <summary>One Contextual (Lookup Type 7) or Chained Context (Lookup Type 8) subtable - same
    /// shape as GSUB's <c>GsubSequenceContextSubtable</c> (see its own doc comment).</summary>
    internal sealed class GposSequenceContextSubtable
    {
        public required GposSequenceContextFormat Format { get; init; }
        public CoverageTable? Coverage { get; init; }
        public ClassDefTable? InputClassDef { get; init; }
        public ClassDefTable? BacktrackClassDef { get; init; }
        public ClassDefTable? LookaheadClassDef { get; init; }
        public GposSequenceRule[][]? RuleSets { get; init; }
        public CoverageTable[]? BacktrackCoverages { get; init; }
        public CoverageTable[]? InputCoverages { get; init; }
        public CoverageTable[]? LookaheadCoverages { get; init; }
        public GposSequenceLookupRecord[]? SeqLookupRecords { get; init; }
    }

    /// <summary>A GPOS lookup of type 7 (or type 9 wrapping type 7), as one or more subtables.</summary>
    internal sealed class GposContextualLookup
    {
        public required IReadOnlyList<GposSequenceContextSubtable> Subtables { get; init; }
        public ushort LookupFlag { get; init; }
        public int? MarkFilteringSetIndex { get; init; }
    }

    /// <summary>A GPOS lookup of type 8 (or type 9 wrapping type 8), as one or more subtables.</summary>
    internal sealed class GposChainingContextLookup
    {
        public required IReadOnlyList<GposSequenceContextSubtable> Subtables { get; init; }
        public ushort LookupFlag { get; init; }
        public int? MarkFilteringSetIndex { get; init; }
    }

    internal sealed class GposTable
    {
        private readonly OpenTypeFontface _face;
        private readonly int _scriptListOffset;
        private readonly int _featureListOffset;
        private readonly int _lookupListOffset;

        // Same process-wide-shared-instance rationale as GsubTable's own cache fields (issue #543) -
        // a GposTable instance is cached and shared across concurrently-rendering PdfGenerator
        // instances, and every sequential _face.Position read below is locked on the same shared
        // _face GsubTable already locks on, so GSUB- and GPOS-table reads against one cached font
        // stay correctly serialized against each other too.
        private readonly ConcurrentDictionary<int, GposSingleAdjustmentLookup?> _singleAdjustmentCache = new();
        private readonly ConcurrentDictionary<int, GposCursiveAttachmentLookup?> _cursiveAttachmentCache = new();
        private readonly ConcurrentDictionary<int, GposPairAdjustmentLookup?> _pairAdjustmentCache = new();
        private readonly ConcurrentDictionary<int, GposMarkToBaseLookup?> _markToBaseCache = new();
        private readonly ConcurrentDictionary<int, GposMarkToMarkLookup?> _markToMarkCache = new();
        private readonly ConcurrentDictionary<int, GposMarkToLigatureLookup?> _markToLigatureCache = new();
        private readonly ConcurrentDictionary<int, GposContextualLookup?> _contextualLookupCache = new();
        private readonly ConcurrentDictionary<int, GposChainingContextLookup?> _chainingContextLookupCache = new();
        private readonly ConcurrentDictionary<int, int> _resolvedLookupTypeCache = new();

        public GposTable(OpenTypeFontface face, int tableStart)
        {
            _face = face;

            face.Position = tableStart;
            face.ReadUShort(); // majorVersion
            face.ReadUShort(); // minorVersion
            _scriptListOffset = tableStart + face.ReadUShort();
            _featureListOffset = tableStart + face.ReadUShort();
            _lookupListOffset = tableStart + face.ReadUShort();
            // featureVariationsOffset (minorVersion 1 only) - ignored, variable fonts aren't instanced.
        }

        /// <summary>
        /// Collects the lookup-list indices of every feature in <paramref name="featureTags"/> under
        /// the first script in <paramref name="scriptTagPreference"/> that this font defines (falling
        /// back to the first script record if none match), using that script's default language
        /// system - a font-defined `required` feature is always included alongside the requested tags.
        /// Deliberately duplicates <see cref="GsubTable"/>'s own near-identical walker rather than
        /// sharing it (see this file's own header note) - kept intentionally parameterized by this
        /// instance's own offsets so a future extraction, if wanted, is a mechanical lift.
        /// </summary>
        public SortedSet<int> GetActiveLookupIndices(IReadOnlyList<string> scriptTagPreference, IReadOnlySet<string> featureTags)
        {
            lock (_face)
            {
                var lookupIndices = new SortedSet<int>();

                int scriptOffset = FindScript(scriptTagPreference);
                if (scriptOffset < 0)
                    return lookupIndices;

                _face.Position = scriptOffset;
                int defaultLangSysOffset = _face.ReadUShort();
                if (defaultLangSysOffset == 0)
                    return lookupIndices;

                _face.Position = scriptOffset + defaultLangSysOffset;
                _face.ReadUShort(); // lookupOrder - reserved, always 0
                int requiredFeatureIndex = _face.ReadUShort();
                int featureIndexCount = _face.ReadUShort();
                var featureIndices = new int[featureIndexCount];
                for (int i = 0; i < featureIndexCount; i++)
                    featureIndices[i] = _face.ReadUShort();

                var featureRecords = ReadFeatureRecords();

                void CollectFeature(int featureIndex)
                {
                    if (featureIndex < 0 || featureIndex >= featureRecords.Length)
                        return;
                    var (tag, offset) = featureRecords[featureIndex];
                    if (!featureTags.Contains(tag))
                        return;

                    _face.Position = offset;
                    _face.ReadUShort(); // featureParams offset - ignored
                    int lookupIndexCount = _face.ReadUShort();
                    for (int i = 0; i < lookupIndexCount; i++)
                        lookupIndices.Add(_face.ReadUShort());
                }

                const int noRequiredFeature = 0xFFFF;
                if (requiredFeatureIndex != noRequiredFeature)
                    CollectFeature(requiredFeatureIndex);
                foreach (int featureIndex in featureIndices)
                    CollectFeature(featureIndex);

                return lookupIndices;
            }
        }

        public GposSingleAdjustmentLookup? GetSingleAdjustmentLookup(int lookupListIndex)
            => _singleAdjustmentCache.GetOrAdd(lookupListIndex, ReadSingleAdjustmentLookup);

        public GposCursiveAttachmentLookup? GetCursiveAttachmentLookup(int lookupListIndex)
            => _cursiveAttachmentCache.GetOrAdd(lookupListIndex, ReadCursiveAttachmentLookup);

        public GposPairAdjustmentLookup? GetPairAdjustmentLookup(int lookupListIndex)
            => _pairAdjustmentCache.GetOrAdd(lookupListIndex, ReadPairAdjustmentLookup);

        public GposMarkToBaseLookup? GetMarkToBaseLookup(int lookupListIndex)
            => _markToBaseCache.GetOrAdd(lookupListIndex, ReadMarkToBaseLookup);

        public GposMarkToMarkLookup? GetMarkToMarkLookup(int lookupListIndex)
            => _markToMarkCache.GetOrAdd(lookupListIndex, ReadMarkToMarkLookup);

        public GposMarkToLigatureLookup? GetMarkToLigatureLookup(int lookupListIndex)
            => _markToLigatureCache.GetOrAdd(lookupListIndex, ReadMarkToLigatureLookup);

        public GposContextualLookup? GetContextualLookup(int lookupListIndex)
            => _contextualLookupCache.GetOrAdd(lookupListIndex, ReadContextualLookup);

        public GposChainingContextLookup? GetChainingContextLookup(int lookupListIndex)
            => _chainingContextLookupCache.GetOrAdd(lookupListIndex, ReadChainingContextLookup);

        /// <summary>The real lookup type at <paramref name="lookupListIndex"/> - a Type 9 (Extension
        /// Positioning) lookup resolves to whatever type it wraps. Returns -1 for an out-of-range index.</summary>
        public int GetResolvedLookupType(int lookupListIndex)
            => _resolvedLookupTypeCache.GetOrAdd(lookupListIndex, ReadResolvedLookupType);

        private (string Tag, int Offset)[] ReadFeatureRecords()
        {
            _face.Position = _featureListOffset;
            int featureCount = _face.ReadUShort();
            var records = new (string, int)[featureCount];
            for (int i = 0; i < featureCount; i++)
            {
                string tag = _face.ReadTag();
                int offset = _featureListOffset + _face.ReadUShort();
                records[i] = (tag, offset);
            }
            return records;
        }

        /// <summary>
        /// Resolves the <c>ScriptList</c> record to use - same fallback rationale as
        /// <see cref="GsubTable"/>'s identical method (duplicated per this file's own GSUB/GPOS
        /// convention): tries <paramref name="scriptTagPreference"/> in order, then this font's own
        /// explicit <c>"DFLT"</c> script if present, and only then the true first-listed record.
        /// </summary>
        private int FindScript(IReadOnlyList<string> scriptTagPreference)
        {
            _face.Position = _scriptListOffset;
            int scriptCount = _face.ReadUShort();
            var records = new (string Tag, int Offset)[scriptCount];
            for (int i = 0; i < scriptCount; i++)
            {
                string tag = _face.ReadTag();
                int offset = _scriptListOffset + _face.ReadUShort();
                records[i] = (tag, offset);
            }

            foreach (string preferred in scriptTagPreference)
            {
                foreach (var record in records)
                {
                    if (record.Tag == preferred)
                        return record.Offset;
                }
            }

            foreach (var record in records)
            {
                if (record.Tag == "DFLT")
                    return record.Offset;
            }

            return records.Length > 0 ? records[0].Offset : -1;
        }

        private readonly record struct LookupHeader(ushort LookupFlag, int? MarkFilteringSetIndex, IReadOnlyList<int> SubtableOffsets);

        /// <summary>
        /// Reads the Lookup table at <paramref name="lookupListIndex"/>'s header and subtable offsets,
        /// returning null if the index is out of range or (after unwrapping any Type 9 Extension
        /// Positioning wrapper) its resolved type isn't <paramref name="expectedType"/>. Callers must
        /// already hold `lock (_face)`.
        /// </summary>
        private LookupHeader? ReadLookupHeader(int lookupListIndex, int expectedType)
        {
            _face.Position = _lookupListOffset;
            int lookupCount = _face.ReadUShort();
            if (lookupListIndex < 0 || lookupListIndex >= lookupCount)
                return null;

            _face.Position = _lookupListOffset + 2 + lookupListIndex * 2;
            int lookupTableStart = _lookupListOffset + _face.ReadUShort();

            _face.Position = lookupTableStart;
            int lookupType = _face.ReadUShort();
            ushort lookupFlag = _face.ReadUShort();
            int subtableCount = _face.ReadUShort();
            var subtableOffsets = new int[subtableCount];
            for (int i = 0; i < subtableCount; i++)
                subtableOffsets[i] = lookupTableStart + _face.ReadUShort();

            // The extra markFilteringSet field is present only when lookupFlag's USE_MARK_FILTERING_SET
            // bit (0x0010) is set - read it (Types 7/8's own skip-aware matching resolves it via GDEF's
            // MarkGlyphSetsDef, same as GsubTable's identical field) rather than merely discarding it,
            // since leaving the cursor unadvanced here would misalign every later sequential read.
            const ushort useMarkFilteringSet = 0x0010;
            int? markFilteringSetIndex = (lookupFlag & useMarkFilteringSet) != 0 ? _face.ReadUShort() : null;

            if (lookupType != expectedType && lookupType != 9)
                return null;

            var resolvedOffsets = new List<int>(subtableCount);
            foreach (int subtableOffset in subtableOffsets)
            {
                int resolvedOffset = subtableOffset;
                if (lookupType == 9)
                {
                    _face.Position = subtableOffset;
                    _face.ReadUShort(); // posFormat (always 1)
                    int extensionLookupType = _face.ReadUShort();
                    uint extensionOffset = _face.ReadULong();
                    if (extensionLookupType != expectedType)
                        continue;
                    resolvedOffset = subtableOffset + (int)extensionOffset;
                }
                resolvedOffsets.Add(resolvedOffset);
            }

            return new LookupHeader(lookupFlag, markFilteringSetIndex, resolvedOffsets);
        }

        private static GposValueRecord ReadValueRecord(OpenTypeFontface face, ushort valueFormat)
        {
            short xPlacement = 0, yPlacement = 0, xAdvance = 0, yAdvance = 0;
            if ((valueFormat & 0x0001) != 0) xPlacement = face.ReadShort();
            if ((valueFormat & 0x0002) != 0) yPlacement = face.ReadShort();
            if ((valueFormat & 0x0004) != 0) xAdvance = face.ReadShort();
            if ((valueFormat & 0x0008) != 0) yAdvance = face.ReadShort();
            // Device-table offsets (0x0010/0x0020/0x0040/0x0080): read-and-discard to keep the cursor
            // aligned for whatever follows, never resolved - see this file's own type-level remarks.
            if ((valueFormat & 0x0010) != 0) face.ReadUShort();
            if ((valueFormat & 0x0020) != 0) face.ReadUShort();
            if ((valueFormat & 0x0040) != 0) face.ReadUShort();
            if ((valueFormat & 0x0080) != 0) face.ReadUShort();
            return new GposValueRecord(xPlacement, yPlacement, xAdvance, yAdvance);
        }

        private static GposAnchor ReadAnchor(OpenTypeFontface face, int offset)
        {
            face.Position = offset;
            int format = face.ReadUShort();
            short x = face.ReadShort();
            short y = face.ReadShort();
            if (format == 2) face.ReadUShort(); // anchorPoint - ignored, hinting-only
            else if (format == 3) { face.ReadUShort(); face.ReadUShort(); } // xDeviceOffset, yDeviceOffset - ignored
            return new GposAnchor(x, y);
        }

        private GposSingleAdjustmentLookup? ReadSingleAdjustmentLookup(int lookupListIndex)
        {
            // Same locking rationale as GsubTable's own per-lookup readers - see issue #543.
            lock (_face)
            {
                if (ReadLookupHeader(lookupListIndex, 1) is not { } header)
                    return null;

                var subtables = new List<GposSingleAdjustmentSubtable>(header.SubtableOffsets.Count);
                foreach (int offset in header.SubtableOffsets)
                {
                    GposSingleAdjustmentSubtable? subtable = ReadSingleAdjustmentSubtable(offset);
                    if (subtable is not null)
                        subtables.Add(subtable);
                }

                return subtables.Count > 0
                    ? new GposSingleAdjustmentLookup { Subtables = subtables, LookupFlag = header.LookupFlag }
                    : null;
            }
        }

        private GposSingleAdjustmentSubtable? ReadSingleAdjustmentSubtable(int offset)
        {
            _face.Position = offset;
            int format = _face.ReadUShort();

            if (format == 1)
            {
                int coverageOffset = offset + _face.ReadUShort();
                ushort valueFormat = _face.ReadUShort();
                GposValueRecord value = ReadValueRecord(_face, valueFormat);
                return new GposSingleAdjustmentSubtable { Coverage = CoverageTable.Read(_face, coverageOffset), Values = [value], IsUniform = true };
            }

            if (format == 2)
            {
                int coverageOffset = offset + _face.ReadUShort();
                ushort valueFormat = _face.ReadUShort();
                int valueCount = _face.ReadUShort();
                var values = new GposValueRecord[valueCount];
                for (int i = 0; i < valueCount; i++)
                    values[i] = ReadValueRecord(_face, valueFormat);
                return new GposSingleAdjustmentSubtable { Coverage = CoverageTable.Read(_face, coverageOffset), Values = values, IsUniform = false };
            }

            return null;
        }

        private GposCursiveAttachmentLookup? ReadCursiveAttachmentLookup(int lookupListIndex)
        {
            // Same locking rationale as GsubTable's own per-lookup readers - see issue #543.
            lock (_face)
            {
                if (ReadLookupHeader(lookupListIndex, 3) is not { } header)
                    return null;

                var subtables = new List<GposCursiveAttachmentSubtable>(header.SubtableOffsets.Count);
                foreach (int offset in header.SubtableOffsets)
                {
                    GposCursiveAttachmentSubtable? subtable = ReadCursiveAttachmentSubtable(offset);
                    if (subtable is not null)
                        subtables.Add(subtable);
                }

                return subtables.Count > 0
                    ? new GposCursiveAttachmentLookup { Subtables = subtables, LookupFlag = header.LookupFlag, MarkFilteringSetIndex = header.MarkFilteringSetIndex }
                    : null;
            }
        }

        private GposCursiveAttachmentSubtable? ReadCursiveAttachmentSubtable(int offset)
        {
            _face.Position = offset;
            int format = _face.ReadUShort();
            if (format != 1)
                return null;

            int coverageOffset = offset + _face.ReadUShort();
            int entryExitCount = _face.ReadUShort();
            var entryOffsets = new int[entryExitCount];
            var exitOffsets = new int[entryExitCount];
            for (int i = 0; i < entryExitCount; i++)
            {
                int entryRel = _face.ReadUShort();
                int exitRel = _face.ReadUShort();
                entryOffsets[i] = entryRel != 0 ? offset + entryRel : 0;
                exitOffsets[i] = exitRel != 0 ? offset + exitRel : 0;
            }

            var records = new GposEntryExitRecord[entryExitCount];
            for (int i = 0; i < entryExitCount; i++)
            {
                GposAnchor? entry = entryOffsets[i] != 0 ? ReadAnchor(_face, entryOffsets[i]) : null;
                GposAnchor? exit = exitOffsets[i] != 0 ? ReadAnchor(_face, exitOffsets[i]) : null;
                records[i] = new GposEntryExitRecord(entry, exit);
            }

            return new GposCursiveAttachmentSubtable { Coverage = CoverageTable.Read(_face, coverageOffset), EntryExitRecords = records };
        }

        private GposPairAdjustmentLookup? ReadPairAdjustmentLookup(int lookupListIndex)
        {
            // Same locking rationale as GsubTable's own per-lookup readers - see issue #543.
            lock (_face)
            {
                if (ReadLookupHeader(lookupListIndex, 2) is not { } header)
                    return null;

                var subtables = new List<GposPairAdjustmentSubtable>(header.SubtableOffsets.Count);
                foreach (int offset in header.SubtableOffsets)
                {
                    GposPairAdjustmentSubtable? subtable = ReadPairAdjustmentSubtable(offset);
                    if (subtable is not null)
                        subtables.Add(subtable);
                }

                return subtables.Count > 0
                    ? new GposPairAdjustmentLookup { Subtables = subtables, LookupFlag = header.LookupFlag }
                    : null;
            }
        }

        private GposPairAdjustmentSubtable? ReadPairAdjustmentSubtable(int offset)
        {
            _face.Position = offset;
            int format = _face.ReadUShort();

            if (format == 1)
            {
                int coverageOffset = offset + _face.ReadUShort();
                ushort valueFormat1 = _face.ReadUShort();
                ushort valueFormat2 = _face.ReadUShort();
                int pairSetCount = _face.ReadUShort();
                var pairSetOffsets = new int[pairSetCount];
                for (int i = 0; i < pairSetCount; i++)
                    pairSetOffsets[i] = offset + _face.ReadUShort();

                var pairSets = new GposPairValueRecord[pairSetCount][];
                for (int i = 0; i < pairSetCount; i++)
                {
                    _face.Position = pairSetOffsets[i];
                    int pairValueCount = _face.ReadUShort();
                    var records = new GposPairValueRecord[pairValueCount];
                    for (int j = 0; j < pairValueCount; j++)
                    {
                        ushort secondGlyph = _face.ReadUShort();
                        GposValueRecord v1 = ReadValueRecord(_face, valueFormat1);
                        GposValueRecord v2 = ReadValueRecord(_face, valueFormat2);
                        records[j] = new GposPairValueRecord(secondGlyph, v1, v2);
                    }
                    pairSets[i] = records;
                }

                return new GposPairAdjustmentSubtable { Coverage = CoverageTable.Read(_face, coverageOffset), PairSets = pairSets };
            }

            if (format == 2)
            {
                int coverageOffset = offset + _face.ReadUShort();
                ushort valueFormat1 = _face.ReadUShort();
                ushort valueFormat2 = _face.ReadUShort();
                int classDef1Offset = offset + _face.ReadUShort();
                int classDef2Offset = offset + _face.ReadUShort();
                int class1Count = _face.ReadUShort();
                int class2Count = _face.ReadUShort();

                var classValues = new (GposValueRecord, GposValueRecord)[class1Count * class2Count];
                for (int i = 0; i < classValues.Length; i++)
                {
                    GposValueRecord v1 = ReadValueRecord(_face, valueFormat1);
                    GposValueRecord v2 = ReadValueRecord(_face, valueFormat2);
                    classValues[i] = (v1, v2);
                }

                return new GposPairAdjustmentSubtable
                {
                    Coverage = CoverageTable.Read(_face, coverageOffset),
                    ClassDef1 = ClassDefTable.Read(_face, classDef1Offset),
                    ClassDef2 = ClassDefTable.Read(_face, classDef2Offset),
                    Class2Count = class2Count,
                    ClassValues = classValues,
                };
            }

            return null;
        }

        private GposMarkToBaseLookup? ReadMarkToBaseLookup(int lookupListIndex)
        {
            // Same locking rationale as GsubTable's own per-lookup readers - see issue #543.
            lock (_face)
            {
                if (ReadLookupHeader(lookupListIndex, 4) is not { } header)
                    return null;

                var subtables = new List<GposMarkAttachmentSubtable>(header.SubtableOffsets.Count);
                foreach (int offset in header.SubtableOffsets)
                {
                    GposMarkAttachmentSubtable? subtable = ReadMarkAttachmentSubtable(offset);
                    if (subtable is not null)
                        subtables.Add(subtable);
                }

                return subtables.Count > 0
                    ? new GposMarkToBaseLookup { Subtables = subtables, LookupFlag = header.LookupFlag, MarkFilteringSetIndex = header.MarkFilteringSetIndex }
                    : null;
            }
        }

        private GposMarkToMarkLookup? ReadMarkToMarkLookup(int lookupListIndex)
        {
            // Same locking rationale as GsubTable's own per-lookup readers - see issue #543.
            lock (_face)
            {
                if (ReadLookupHeader(lookupListIndex, 6) is not { } header)
                    return null;

                var subtables = new List<GposMarkAttachmentSubtable>(header.SubtableOffsets.Count);
                foreach (int offset in header.SubtableOffsets)
                {
                    GposMarkAttachmentSubtable? subtable = ReadMarkAttachmentSubtable(offset);
                    if (subtable is not null)
                        subtables.Add(subtable);
                }

                return subtables.Count > 0
                    ? new GposMarkToMarkLookup { Subtables = subtables, LookupFlag = header.LookupFlag }
                    : null;
            }
        }

        /// <summary>Shared by MarkToBase (Lookup Type 4, `MarkBasePosFormat1`) and MarkToMark
        /// (Lookup Type 6, `MarkMarkPosFormat1`) - both formats are byte-identical.</summary>
        private GposMarkAttachmentSubtable? ReadMarkAttachmentSubtable(int offset)
        {
            _face.Position = offset;
            int format = _face.ReadUShort();
            if (format != 1)
                return null;

            int markCoverageOffset = offset + _face.ReadUShort();
            int baseCoverageOffset = offset + _face.ReadUShort();
            int markClassCount = _face.ReadUShort();
            int markArrayOffset = offset + _face.ReadUShort();
            int baseArrayOffset = offset + _face.ReadUShort();

            (int MarkClass, GposAnchor Anchor)[] marks = ReadMarkArray(markArrayOffset);
            GposAnchor?[][] baseAnchors = ReadBaseArray(baseArrayOffset, markClassCount);

            return new GposMarkAttachmentSubtable
            {
                MarkCoverage = CoverageTable.Read(_face, markCoverageOffset),
                BaseCoverage = CoverageTable.Read(_face, baseCoverageOffset),
                MarkClassCount = markClassCount,
                Marks = marks,
                BaseAnchorsByClass = baseAnchors,
            };
        }

        private (int MarkClass, GposAnchor Anchor)[] ReadMarkArray(int markArrayOffset)
        {
            _face.Position = markArrayOffset;
            int markCount = _face.ReadUShort();
            var classes = new int[markCount];
            var anchorOffsets = new int[markCount];
            for (int i = 0; i < markCount; i++)
            {
                classes[i] = _face.ReadUShort();
                anchorOffsets[i] = markArrayOffset + _face.ReadUShort();
            }

            var marks = new (int, GposAnchor)[markCount];
            for (int i = 0; i < markCount; i++)
                marks[i] = (classes[i], ReadAnchor(_face, anchorOffsets[i]));
            return marks;
        }

        private GposAnchor?[][] ReadBaseArray(int baseArrayOffset, int markClassCount)
        {
            _face.Position = baseArrayOffset;
            int baseCount = _face.ReadUShort();
            var anchorOffsets = new int[baseCount][];
            for (int i = 0; i < baseCount; i++)
            {
                var offsets = new int[markClassCount];
                for (int c = 0; c < markClassCount; c++)
                {
                    int rel = _face.ReadUShort();
                    offsets[c] = rel != 0 ? baseArrayOffset + rel : 0;
                }
                anchorOffsets[i] = offsets;
            }

            var result = new GposAnchor?[baseCount][];
            for (int i = 0; i < baseCount; i++)
            {
                result[i] = new GposAnchor?[markClassCount];
                for (int c = 0; c < markClassCount; c++)
                    result[i][c] = anchorOffsets[i][c] != 0 ? ReadAnchor(_face, anchorOffsets[i][c]) : null;
            }
            return result;
        }

        private GposMarkToLigatureLookup? ReadMarkToLigatureLookup(int lookupListIndex)
        {
            // Same locking rationale as GsubTable's own per-lookup readers - see issue #543.
            lock (_face)
            {
                if (ReadLookupHeader(lookupListIndex, 5) is not { } header)
                    return null;

                var subtables = new List<GposMarkToLigatureSubtable>(header.SubtableOffsets.Count);
                foreach (int offset in header.SubtableOffsets)
                {
                    GposMarkToLigatureSubtable? subtable = ReadMarkToLigatureSubtable(offset);
                    if (subtable is not null)
                        subtables.Add(subtable);
                }

                return subtables.Count > 0
                    ? new GposMarkToLigatureLookup { Subtables = subtables, LookupFlag = header.LookupFlag, MarkFilteringSetIndex = header.MarkFilteringSetIndex }
                    : null;
            }
        }

        private GposMarkToLigatureSubtable? ReadMarkToLigatureSubtable(int offset)
        {
            _face.Position = offset;
            int format = _face.ReadUShort();
            if (format != 1)
                return null;

            int markCoverageOffset = offset + _face.ReadUShort();
            int ligatureCoverageOffset = offset + _face.ReadUShort();
            int markClassCount = _face.ReadUShort();
            int markArrayOffset = offset + _face.ReadUShort();
            int ligatureArrayOffset = offset + _face.ReadUShort();

            // MarkArray is byte-identical to Type 4/6's own - reused directly rather than re-read.
            (int MarkClass, GposAnchor Anchor)[] marks = ReadMarkArray(markArrayOffset);
            GposLigatureAttach[] ligatureAttachments = ReadLigatureArray(ligatureArrayOffset, markClassCount);

            return new GposMarkToLigatureSubtable
            {
                MarkCoverage = CoverageTable.Read(_face, markCoverageOffset),
                LigatureCoverage = CoverageTable.Read(_face, ligatureCoverageOffset),
                MarkClassCount = markClassCount,
                Marks = marks,
                LigatureAttachments = ligatureAttachments,
            };
        }

        private GposLigatureAttach[] ReadLigatureArray(int ligatureArrayOffset, int markClassCount)
        {
            _face.Position = ligatureArrayOffset;
            int ligatureCount = _face.ReadUShort();
            var attachOffsets = new int[ligatureCount];
            for (int i = 0; i < ligatureCount; i++)
                attachOffsets[i] = ligatureArrayOffset + _face.ReadUShort();

            var result = new GposLigatureAttach[ligatureCount];
            for (int i = 0; i < ligatureCount; i++)
                result[i] = ReadLigatureAttach(attachOffsets[i], markClassCount);
            return result;
        }

        /// <summary>Same shape as <see cref="ReadBaseArray"/>, one anchor-table-set per ligature
        /// *component* instead of per base glyph.</summary>
        private GposLigatureAttach ReadLigatureAttach(int ligatureAttachOffset, int markClassCount)
        {
            _face.Position = ligatureAttachOffset;
            int componentCount = _face.ReadUShort();
            var anchorOffsets = new int[componentCount][];
            for (int c = 0; c < componentCount; c++)
            {
                var offsets = new int[markClassCount];
                for (int m = 0; m < markClassCount; m++)
                {
                    int rel = _face.ReadUShort();
                    offsets[m] = rel != 0 ? ligatureAttachOffset + rel : 0;
                }
                anchorOffsets[c] = offsets;
            }

            var anchorsByComponent = new GposAnchor?[componentCount][];
            for (int c = 0; c < componentCount; c++)
            {
                anchorsByComponent[c] = new GposAnchor?[markClassCount];
                for (int m = 0; m < markClassCount; m++)
                    anchorsByComponent[c][m] = anchorOffsets[c][m] != 0 ? ReadAnchor(_face, anchorOffsets[c][m]) : null;
            }

            return new GposLigatureAttach { AnchorsByComponent = anchorsByComponent };
        }

        private GposContextualLookup? ReadContextualLookup(int lookupListIndex)
        {
            // Same locking rationale as GsubTable's own per-lookup readers - see issue #543.
            lock (_face)
            {
                if (ReadLookupHeader(lookupListIndex, 7) is not { } header)
                    return null;

                var subtables = new List<GposSequenceContextSubtable>(header.SubtableOffsets.Count);
                foreach (int subtableOffset in header.SubtableOffsets)
                {
                    GposSequenceContextSubtable? subtable = ReadSequenceContextSubtable(subtableOffset);
                    if (subtable is not null)
                        subtables.Add(subtable);
                }

                return subtables.Count > 0
                    ? new GposContextualLookup { Subtables = subtables, LookupFlag = header.LookupFlag, MarkFilteringSetIndex = header.MarkFilteringSetIndex }
                    : null;
            }
        }

        private GposChainingContextLookup? ReadChainingContextLookup(int lookupListIndex)
        {
            // Same locking rationale as GsubTable's own per-lookup readers - see issue #543.
            lock (_face)
            {
                if (ReadLookupHeader(lookupListIndex, 8) is not { } header)
                    return null;

                var subtables = new List<GposSequenceContextSubtable>(header.SubtableOffsets.Count);
                foreach (int subtableOffset in header.SubtableOffsets)
                {
                    GposSequenceContextSubtable? subtable = ReadChainedSequenceContextSubtable(subtableOffset);
                    if (subtable is not null)
                        subtables.Add(subtable);
                }

                return subtables.Count > 0
                    ? new GposChainingContextLookup { Subtables = subtables, LookupFlag = header.LookupFlag, MarkFilteringSetIndex = header.MarkFilteringSetIndex }
                    : null;
            }
        }

        /// <summary>Reads a non-chaining `SequenceContext` subtable (Lookup Type 7), formats 1/2/3 -
        /// byte-for-byte the same layout as GSUB's own Lookup Type 5 subtable (see
        /// <see cref="GsubTable"/>'s equivalent reader), duplicated per this file's own header note.</summary>
        private GposSequenceContextSubtable? ReadSequenceContextSubtable(int offset)
        {
            _face.Position = offset;
            int format = _face.ReadUShort();

            if (format == 1)
            {
                int coverageOffset = offset + _face.ReadUShort();
                int ruleSetCount = _face.ReadUShort();
                var ruleSetOffsets = new int[ruleSetCount];
                for (int i = 0; i < ruleSetCount; i++)
                {
                    int rel = _face.ReadUShort();
                    ruleSetOffsets[i] = rel != 0 ? offset + rel : 0;
                }

                var ruleSets = new GposSequenceRule[ruleSetCount][];
                for (int i = 0; i < ruleSetCount; i++)
                    ruleSets[i] = ruleSetOffsets[i] != 0 ? ReadSequenceRuleSet(ruleSetOffsets[i]) : [];

                return new GposSequenceContextSubtable
                {
                    Format = GposSequenceContextFormat.Glyph,
                    Coverage = CoverageTable.Read(_face, coverageOffset),
                    RuleSets = ruleSets,
                };
            }

            if (format == 2)
            {
                int coverageOffset = offset + _face.ReadUShort();
                int classDefOffset = offset + _face.ReadUShort();
                int ruleSetCount = _face.ReadUShort();
                var ruleSetOffsets = new int[ruleSetCount];
                for (int i = 0; i < ruleSetCount; i++)
                {
                    int rel = _face.ReadUShort();
                    ruleSetOffsets[i] = rel != 0 ? offset + rel : 0;
                }

                var ruleSets = new GposSequenceRule[ruleSetCount][];
                for (int i = 0; i < ruleSetCount; i++)
                    ruleSets[i] = ruleSetOffsets[i] != 0 ? ReadSequenceRuleSet(ruleSetOffsets[i]) : [];

                return new GposSequenceContextSubtable
                {
                    Format = GposSequenceContextFormat.Class,
                    Coverage = CoverageTable.Read(_face, coverageOffset),
                    InputClassDef = ClassDefTable.Read(_face, classDefOffset),
                    RuleSets = ruleSets,
                };
            }

            if (format == 3)
            {
                int glyphCount = _face.ReadUShort();
                int seqLookupCount = _face.ReadUShort();
                var coverageOffsets = new int[glyphCount];
                for (int i = 0; i < glyphCount; i++)
                    coverageOffsets[i] = offset + _face.ReadUShort();
                var records = new GposSequenceLookupRecord[seqLookupCount];
                for (int i = 0; i < seqLookupCount; i++)
                {
                    int sequenceIndex = _face.ReadUShort();
                    int lookupListIndex = _face.ReadUShort();
                    records[i] = new GposSequenceLookupRecord(sequenceIndex, lookupListIndex);
                }

                var inputCoverages = new CoverageTable[glyphCount];
                for (int i = 0; i < glyphCount; i++)
                    inputCoverages[i] = CoverageTable.Read(_face, coverageOffsets[i]);

                return new GposSequenceContextSubtable
                {
                    Format = GposSequenceContextFormat.Coverage,
                    InputCoverages = inputCoverages,
                    SeqLookupRecords = records,
                };
            }

            return null;
        }

        /// <summary>Reads a `ChainedSequenceContext` subtable (Lookup Type 8), formats 1/2/3 - same
        /// layout as GSUB's own Lookup Type 6 subtable.</summary>
        private GposSequenceContextSubtable? ReadChainedSequenceContextSubtable(int offset)
        {
            _face.Position = offset;
            int format = _face.ReadUShort();

            if (format == 1)
            {
                int coverageOffset = offset + _face.ReadUShort();
                int ruleSetCount = _face.ReadUShort();
                var ruleSetOffsets = new int[ruleSetCount];
                for (int i = 0; i < ruleSetCount; i++)
                {
                    int rel = _face.ReadUShort();
                    ruleSetOffsets[i] = rel != 0 ? offset + rel : 0;
                }

                var ruleSets = new GposSequenceRule[ruleSetCount][];
                for (int i = 0; i < ruleSetCount; i++)
                    ruleSets[i] = ruleSetOffsets[i] != 0 ? ReadChainedSequenceRuleSet(ruleSetOffsets[i]) : [];

                return new GposSequenceContextSubtable
                {
                    Format = GposSequenceContextFormat.Glyph,
                    Coverage = CoverageTable.Read(_face, coverageOffset),
                    RuleSets = ruleSets,
                };
            }

            if (format == 2)
            {
                int coverageOffset = offset + _face.ReadUShort();
                int backtrackClassDefOffset = offset + _face.ReadUShort();
                int inputClassDefOffset = offset + _face.ReadUShort();
                int lookaheadClassDefOffset = offset + _face.ReadUShort();
                int ruleSetCount = _face.ReadUShort();
                var ruleSetOffsets = new int[ruleSetCount];
                for (int i = 0; i < ruleSetCount; i++)
                {
                    int rel = _face.ReadUShort();
                    ruleSetOffsets[i] = rel != 0 ? offset + rel : 0;
                }

                var ruleSets = new GposSequenceRule[ruleSetCount][];
                for (int i = 0; i < ruleSetCount; i++)
                    ruleSets[i] = ruleSetOffsets[i] != 0 ? ReadChainedSequenceRuleSet(ruleSetOffsets[i]) : [];

                return new GposSequenceContextSubtable
                {
                    Format = GposSequenceContextFormat.Class,
                    Coverage = CoverageTable.Read(_face, coverageOffset),
                    BacktrackClassDef = ClassDefTable.Read(_face, backtrackClassDefOffset),
                    InputClassDef = ClassDefTable.Read(_face, inputClassDefOffset),
                    LookaheadClassDef = ClassDefTable.Read(_face, lookaheadClassDefOffset),
                    RuleSets = ruleSets,
                };
            }

            if (format == 3)
            {
                int backtrackGlyphCount = _face.ReadUShort();
                var backtrackOffsets = new int[backtrackGlyphCount];
                for (int i = 0; i < backtrackGlyphCount; i++)
                    backtrackOffsets[i] = offset + _face.ReadUShort();

                int inputGlyphCount = _face.ReadUShort();
                var inputOffsets = new int[inputGlyphCount];
                for (int i = 0; i < inputGlyphCount; i++)
                    inputOffsets[i] = offset + _face.ReadUShort();

                int lookaheadGlyphCount = _face.ReadUShort();
                var lookaheadOffsets = new int[lookaheadGlyphCount];
                for (int i = 0; i < lookaheadGlyphCount; i++)
                    lookaheadOffsets[i] = offset + _face.ReadUShort();

                int seqLookupCount = _face.ReadUShort();
                var records = new GposSequenceLookupRecord[seqLookupCount];
                for (int i = 0; i < seqLookupCount; i++)
                {
                    int sequenceIndex = _face.ReadUShort();
                    int lookupListIndex = _face.ReadUShort();
                    records[i] = new GposSequenceLookupRecord(sequenceIndex, lookupListIndex);
                }

                var backtrackCoverages = new CoverageTable[backtrackGlyphCount];
                for (int i = 0; i < backtrackGlyphCount; i++)
                    backtrackCoverages[i] = CoverageTable.Read(_face, backtrackOffsets[i]);

                var inputCoverages = new CoverageTable[inputGlyphCount];
                for (int i = 0; i < inputGlyphCount; i++)
                    inputCoverages[i] = CoverageTable.Read(_face, inputOffsets[i]);

                var lookaheadCoverages = new CoverageTable[lookaheadGlyphCount];
                for (int i = 0; i < lookaheadGlyphCount; i++)
                    lookaheadCoverages[i] = CoverageTable.Read(_face, lookaheadOffsets[i]);

                return new GposSequenceContextSubtable
                {
                    Format = GposSequenceContextFormat.Coverage,
                    BacktrackCoverages = backtrackCoverages,
                    InputCoverages = inputCoverages,
                    LookaheadCoverages = lookaheadCoverages,
                    SeqLookupRecords = records,
                };
            }

            return null;
        }

        private GposSequenceRule[] ReadSequenceRuleSet(int ruleSetOffset)
        {
            _face.Position = ruleSetOffset;
            int ruleCount = _face.ReadUShort();
            var ruleOffsets = new int[ruleCount];
            for (int i = 0; i < ruleCount; i++)
                ruleOffsets[i] = ruleSetOffset + _face.ReadUShort();

            var rules = new GposSequenceRule[ruleCount];
            for (int i = 0; i < ruleCount; i++)
                rules[i] = ReadSequenceRule(ruleOffsets[i]);
            return rules;
        }

        private GposSequenceRule ReadSequenceRule(int ruleOffset)
        {
            _face.Position = ruleOffset;
            int glyphCount = _face.ReadUShort();
            int seqLookupCount = _face.ReadUShort();
            // glyphCount includes the first glyph (already matched via this rule's own Coverage
            // entry), so Input holds glyphCount - 1 more - a spec-conformant font always has
            // glyphCount >= 1, but a malformed/corrupt font could claim 0; Math.Max keeps that case a
            // harmless empty (never-matching) rule instead of an OverflowException from a negative
            // array length.
            var input = new ushort[Math.Max(0, glyphCount - 1)];
            for (int i = 0; i < input.Length; i++)
                input[i] = _face.ReadUShort();
            var records = new GposSequenceLookupRecord[seqLookupCount];
            for (int i = 0; i < seqLookupCount; i++)
            {
                int sequenceIndex = _face.ReadUShort();
                int lookupListIndex = _face.ReadUShort();
                records[i] = new GposSequenceLookupRecord(sequenceIndex, lookupListIndex);
            }

            return new GposSequenceRule { Backtrack = [], Input = input, Lookahead = [], SeqLookupRecords = records };
        }

        private GposSequenceRule[] ReadChainedSequenceRuleSet(int ruleSetOffset)
        {
            _face.Position = ruleSetOffset;
            int ruleCount = _face.ReadUShort();
            var ruleOffsets = new int[ruleCount];
            for (int i = 0; i < ruleCount; i++)
                ruleOffsets[i] = ruleSetOffset + _face.ReadUShort();

            var rules = new GposSequenceRule[ruleCount];
            for (int i = 0; i < ruleCount; i++)
                rules[i] = ReadChainedSequenceRule(ruleOffsets[i]);
            return rules;
        }

        private GposSequenceRule ReadChainedSequenceRule(int ruleOffset)
        {
            _face.Position = ruleOffset;

            int backtrackGlyphCount = _face.ReadUShort();
            var backtrack = new ushort[backtrackGlyphCount];
            for (int i = 0; i < backtrack.Length; i++)
                backtrack[i] = _face.ReadUShort();

            int inputGlyphCount = _face.ReadUShort();
            // See ReadSequenceRule's own remarks on the Math.Max guard.
            var input = new ushort[Math.Max(0, inputGlyphCount - 1)];
            for (int i = 0; i < input.Length; i++)
                input[i] = _face.ReadUShort();

            int lookaheadGlyphCount = _face.ReadUShort();
            var lookahead = new ushort[lookaheadGlyphCount];
            for (int i = 0; i < lookahead.Length; i++)
                lookahead[i] = _face.ReadUShort();

            int seqLookupCount = _face.ReadUShort();
            var records = new GposSequenceLookupRecord[seqLookupCount];
            for (int i = 0; i < seqLookupCount; i++)
            {
                int sequenceIndex = _face.ReadUShort();
                int lookupListIndex = _face.ReadUShort();
                records[i] = new GposSequenceLookupRecord(sequenceIndex, lookupListIndex);
            }

            return new GposSequenceRule { Backtrack = backtrack, Input = input, Lookahead = lookahead, SeqLookupRecords = records };
        }

        private int ReadResolvedLookupType(int lookupListIndex)
        {
            // Same locking rationale as GsubTable's own per-lookup readers - see issue #543.
            lock (_face)
            {
                _face.Position = _lookupListOffset;
                int lookupCount = _face.ReadUShort();
                if (lookupListIndex < 0 || lookupListIndex >= lookupCount)
                    return -1;

                _face.Position = _lookupListOffset + 2 + lookupListIndex * 2;
                int lookupTableStart = _lookupListOffset + _face.ReadUShort();

                _face.Position = lookupTableStart;
                int lookupType = _face.ReadUShort();
                if (lookupType != 9)
                    return lookupType;

                _face.ReadUShort(); // lookupFlag
                int subtableCount = _face.ReadUShort();
                if (subtableCount == 0)
                    return lookupType;
                int firstSubtableOffset = lookupTableStart + _face.ReadUShort();

                _face.Position = firstSubtableOffset;
                _face.ReadUShort(); // posFormat (always 1)
                return _face.ReadUShort(); // extensionLookupType - the real type this Type 9 wraps.
            }
        }
    }
}

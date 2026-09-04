#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Reader for the OpenType `GPOS` (Glyph Positioning) table: its own ScriptList/FeatureList/LookupList
// common tables (independent of GSUB's - GPOS has its own tag registry, e.g. `kern`/`mark`/`mkmk`),
// Lookup Type 1 (Single Adjustment, formats 1/2), Lookup Type 2 (Pair Adjustment, formats 1/2),
// Lookup Type 4 (MarkToBase Attachment), and Lookup Type 6 (MarkToMark Attachment) subtables -
// Type 9 (Extension Positioning) is unwrapped down to the real subtable it wraps when present
// (GPOS's own extension type; GSUB's is type 7 - see `GsubTable`, and don't confuse the two: this
// is the one place in this codebase where "9" is the correct Extension type, deliberately). This is
// the subset needed for kerning (`kern`, via Types 1/2) and mark-to-base/mark-to-mark positioning
// (`mark`/`mkmk`, via Types 4/6) - see `PeachPDF.Text.GposPositioner`.
//
// Not implemented (see .claude/accepted-gaps/no-text-shaping.md): Lookup Type 3 (Cursive Attachment -
// needs complex-script joining support to have real fonts/scripts exercising it, out of scope),
// Lookup Type 5 (MarkToLigature Attachment - needs deeper integration with GSUB's ligature-merge
// cluster bookkeeping to identify the right ligature component), and Lookup Types 7/8 (Context/
// Chained Context Positioning - mirrors GSUB Lookup Types 5-8's own deferred complexity/value
// tradeoff, for the rarer positioning case). `lookupFlag`'s GDEF-based mark filtering is read (and
// its `markFilteringSet` extra field correctly skipped for cursor alignment) but not yet consulted
// during matching - GposPositioner's mark-to-base/mark-to-mark base search uses GdefTable directly
// via the shared PeachPDF.Text.GlyphSequenceFilter instead.
//
// https://learn.microsoft.com/en-us/typography/opentype/spec/gpos
// https://learn.microsoft.com/en-us/typography/opentype/spec/chapter2
//
#endregion

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
    }

    /// <summary>A GPOS lookup of type 6 (or type 9 wrapping type 6), as one or more subtables.</summary>
    internal sealed class GposMarkToMarkLookup
    {
        public required IReadOnlyList<GposMarkAttachmentSubtable> Subtables { get; init; }
        public ushort LookupFlag { get; init; }
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
        private readonly ConcurrentDictionary<int, GposPairAdjustmentLookup?> _pairAdjustmentCache = new();
        private readonly ConcurrentDictionary<int, GposMarkToBaseLookup?> _markToBaseCache = new();
        private readonly ConcurrentDictionary<int, GposMarkToMarkLookup?> _markToMarkCache = new();
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

        public GposPairAdjustmentLookup? GetPairAdjustmentLookup(int lookupListIndex)
            => _pairAdjustmentCache.GetOrAdd(lookupListIndex, ReadPairAdjustmentLookup);

        public GposMarkToBaseLookup? GetMarkToBaseLookup(int lookupListIndex)
            => _markToBaseCache.GetOrAdd(lookupListIndex, ReadMarkToBaseLookup);

        public GposMarkToMarkLookup? GetMarkToMarkLookup(int lookupListIndex)
            => _markToMarkCache.GetOrAdd(lookupListIndex, ReadMarkToMarkLookup);

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

            return records.Length > 0 ? records[0].Offset : -1;
        }

        private readonly record struct LookupHeader(ushort LookupFlag, IReadOnlyList<int> SubtableOffsets);

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

            // The extra markFilteringSet field (present only when lookupFlag's USE_MARK_FILTERING_SET
            // bit, 0x0010, is set) sits immediately after the subtable-offset array - read-and-discard
            // it here for correct cursor alignment even though this reader doesn't consult it yet (see
            // file header), or every later sequential read on this shared cursor misaligns.
            const ushort useMarkFilteringSet = 0x0010;
            if ((lookupFlag & useMarkFilteringSet) != 0)
                _face.ReadUShort();

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

            return new LookupHeader(lookupFlag, resolvedOffsets);
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
                    ? new GposMarkToBaseLookup { Subtables = subtables, LookupFlag = header.LookupFlag }
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

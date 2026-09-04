#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Reader for the OpenType `GSUB` (Glyph Substitution) table: the ScriptList/FeatureList/LookupList
// common tables, Lookup Type 1 (Single Substitution, formats 1/2), Lookup Type 2 (Multiple
// Substitution, format 1), Lookup Type 3 (Alternate Substitution, format 1), Lookup Type 4
// (Ligature Substitution), and Lookup Types 5/6 (Contextual/Chaining Context Substitution, formats
// 1/2/3) subtables - Type 7 (Extension Substitution) is unwrapped down to the real subtable it
// wraps when present (GSUB's own extension type; GPOS's is type 9 - a different table's type
// space, see `GposTable`). This is the subset needed to apply the `liga`/`clig`/`rlig`/`dlig`/
// `hlig` ligature features, the caps features (`smcp`/`c2sc`/`pcap`/`c2pc`/`unic`/`titl`), the
// numeric figure/fraction features (`lnum`/`onum`/`pnum`/`tnum`/`ordn`/`zero`, and `frac`/`afrc`
// where a font implements them as single or alternate substitution rather than ligature-style),
// the east-asian features (`jp78`/`jp83`/`jp90`/`jp04`/`smpl`/`trad`/`fwid`/`pwid`/`ruby`), `calt`
// (contextual alternates, via Lookup Types 5/6), and an explicit `font-feature-settings` tag a font
// implements via any of these lookup types (Alternate Substitution's numeric feature value selects
// which glyph alternate to use, per CSS Fonts Level 3 - see `GsubShaper`). `lookupFlag`'s mark
// filtering is honored via `GdefTable` (see `PeachPDF.Text.GlyphSequenceFilter`), and feature
// selection can pick a language-specific `LangSys` instead of always using `DefaultLangSys`.
//
// Not implemented (see .claude/accepted-gaps/no-text-shaping.md): Lookup Type 8 (Reverse Chaining
// Context Single Substitution - Arabic-joining-shaped, processes end-to-start).
//
// https://learn.microsoft.com/en-us/typography/opentype/spec/gsub
// https://learn.microsoft.com/en-us/typography/opentype/spec/chapter2
//
#endregion

using System.Collections.Concurrent;
using System.Collections.Generic;

namespace PeachPDF.Fonts.OpenType
{
    /// <summary>
    /// One ligature: <see cref="ComponentGlyphIds"/> are the glyphs that must (subject to
    /// `lookupFlag`-driven mark filtering skipping intervening glyphs) follow the coverage-matched
    /// first glyph for <see cref="LigatureGlyph"/> to apply.
    /// </summary>
    internal readonly record struct GsubLigature(ushort LigatureGlyph, ushort[] ComponentGlyphIds);

    /// <summary>One Ligature Substitution (format 1) subtable: a <see cref="Coverage"/> table over
    /// possible first glyphs, each mapping (by coverage index) to the ligatures it may start.</summary>
    internal sealed class GsubLigatureSubtable
    {
        public required CoverageTable Coverage { get; init; }
        public required GsubLigature[][] LigatureSets { get; init; }
    }

    /// <summary>A GSUB lookup of type 4 (or type 7 wrapping type 4), as one or more subtables.</summary>
    internal sealed class GsubLigatureLookup
    {
        public required IReadOnlyList<GsubLigatureSubtable> Subtables { get; init; }
        public ushort LookupFlag { get; init; }
        public int? MarkFilteringSetIndex { get; init; }
    }

    /// <summary>One Single Substitution subtable: a <see cref="Coverage"/> table over the glyphs it
    /// substitutes, plus either a shared glyph-id delta (format 1) or an explicit substitute-glyph
    /// array indexed by coverage index (format 2).</summary>
    internal sealed class GsubSingleSubstitutionSubtable
    {
        public required CoverageTable Coverage { get; init; }
        public short? Delta { get; init; }
        public ushort[]? Substitutes { get; init; }

        public bool TryGetSubstitute(ushort glyphId, out ushort substitute)
        {
            int coverageIndex = Coverage.IndexOfGlyph(glyphId);
            if (coverageIndex < 0)
            {
                substitute = 0;
                return false;
            }

            if (Delta is { } delta)
            {
                substitute = unchecked((ushort)(glyphId + delta));
                return true;
            }

            if (Substitutes is { } substitutes && coverageIndex < substitutes.Length)
            {
                substitute = substitutes[coverageIndex];
                return true;
            }

            substitute = 0;
            return false;
        }
    }

    /// <summary>A GSUB lookup of type 1 (or type 7 wrapping type 1), as one or more subtables.</summary>
    internal sealed class GsubSingleSubstitutionLookup
    {
        public required IReadOnlyList<GsubSingleSubstitutionSubtable> Subtables { get; init; }
        public ushort LookupFlag { get; init; }
        public int? MarkFilteringSetIndex { get; init; }
    }

    /// <summary>One Alternate Substitution (format 1) subtable: a <see cref="Coverage"/> table over
    /// the glyphs it offers alternates for, each (by coverage index) naming an ordered set of
    /// alternate glyphs to choose from - <see cref="TryGetAlternate"/> always resolves a specific
    /// zero-based index into that set, since PeachPDF has no interactive glyph-picker UI; the caller
    /// decides which index a given feature request means (see <c>GsubShaper</c>).</summary>
    internal sealed class GsubAlternateSubstitutionSubtable
    {
        public required CoverageTable Coverage { get; init; }
        public required ushort[][] AlternateSets { get; init; }

        public bool TryGetAlternate(ushort glyphId, int alternateIndex, out ushort substitute)
        {
            int coverageIndex = Coverage.IndexOfGlyph(glyphId);
            if (coverageIndex < 0 || coverageIndex >= AlternateSets.Length)
            {
                substitute = 0;
                return false;
            }

            ushort[] alternates = AlternateSets[coverageIndex];
            if (alternateIndex < 0 || alternateIndex >= alternates.Length)
            {
                substitute = 0;
                return false;
            }

            substitute = alternates[alternateIndex];
            return true;
        }
    }

    /// <summary>A GSUB lookup of type 3 (or type 7 wrapping type 3), as one or more subtables.</summary>
    internal sealed class GsubAlternateSubstitutionLookup
    {
        public required IReadOnlyList<GsubAlternateSubstitutionSubtable> Subtables { get; init; }
        public ushort LookupFlag { get; init; }
        public int? MarkFilteringSetIndex { get; init; }
    }

    /// <summary>One Multiple Substitution (format 1) subtable: a <see cref="Coverage"/> table over
    /// glyphs to expand, each (by coverage index) naming the sequence of glyphs it expands to. Per
    /// spec a sequence's glyph count is never 0 (this lookup type must not delete a glyph) - a
    /// malformed zero-length sequence is stored as an empty array, which
    /// <c>GsubShaper.ApplyMultipleSubstitutionLookup</c> treats as "no substitution here" rather
    /// than a special zero-glyph case.</summary>
    internal sealed class GsubMultipleSubstitutionSubtable
    {
        public required CoverageTable Coverage { get; init; }
        public required ushort[][] Sequences { get; init; }
    }

    /// <summary>A GSUB lookup of type 2 (or type 7 wrapping type 2), as one or more subtables.</summary>
    internal sealed class GsubMultipleSubstitutionLookup
    {
        public required IReadOnlyList<GsubMultipleSubstitutionSubtable> Subtables { get; init; }
        public ushort LookupFlag { get; init; }
        public int? MarkFilteringSetIndex { get; init; }
    }

    /// <summary>One <c>SequenceLookupRecord</c>/<c>ChainedSequenceLookupRecord</c>: at
    /// <see cref="SequenceIndex"/> (an index into the matched input sequence), apply the lookup at
    /// <see cref="LookupListIndex"/>.</summary>
    internal readonly record struct GsubSequenceLookupRecord(int SequenceIndex, int LookupListIndex);

    /// <summary>
    /// One contextual/chaining rule. <see cref="Input"/> holds every input position <b>after</b> the
    /// first (which is matched separately, via the owning subtable's own Coverage - format 1 - or
    /// ClassDef - format 2); <see cref="Backtrack"/> (reverse logical order - index 0 is the glyph
    /// immediately before the input sequence) and <see cref="Lookahead"/> (forward logical order -
    /// index 0 is the glyph immediately after the input sequence) are only ever non-empty for a
    /// chaining (Lookup Type 6) rule. Each element is either a literal glyph id (format 1) or a
    /// class value (format 2) - <c>GsubShaper</c>'s matcher treats the two uniformly via a
    /// caller-supplied "does this glyph satisfy this rule position" test.
    /// </summary>
    internal sealed class GsubSequenceRule
    {
        public required ushort[] Backtrack { get; init; }
        public required ushort[] Input { get; init; }
        public required ushort[] Lookahead { get; init; }
        public required GsubSequenceLookupRecord[] SeqLookupRecords { get; init; }
    }

    internal enum GsubSequenceContextFormat
    {
        /// <summary>Format 1: rule sets keyed by the first glyph's Coverage index; each rule's
        /// remaining positions are literal glyph ids.</summary>
        Glyph = 1,

        /// <summary>Format 2: rule sets keyed by the first glyph's <see cref="ClassDefTable"/>
        /// class; each rule's remaining positions are class values.</summary>
        Class = 2,

        /// <summary>Format 3: exactly one rule, every position (including the first) given as its
        /// own <see cref="CoverageTable"/> - no rule-set indirection.</summary>
        Coverage = 3,
    }

    /// <summary>
    /// One Contextual (Lookup Type 5) or Chaining Context (Lookup Type 6) subtable, in whichever of
    /// the three OpenType formats it was authored in - <see cref="Format"/> says which of this
    /// class's fields are populated. Lookup Type 5 subtables always have empty
    /// <see cref="GsubSequenceRule.Backtrack"/>/<see cref="GsubSequenceRule.Lookahead"/> (formats
    /// 1/2) and null <see cref="BacktrackCoverages"/>/<see cref="LookaheadCoverages"/> (format 3);
    /// Lookup Type 6 subtables populate them from the chained rule/coverage arrays.
    /// </summary>
    internal sealed class GsubSequenceContextSubtable
    {
        public required GsubSequenceContextFormat Format { get; init; }

        /// <summary>Format 1 only: selects a <see cref="RuleSets"/> entry by coverage index.</summary>
        public CoverageTable? Coverage { get; init; }

        /// <summary>Format 2 only: classifies the first input glyph to select a
        /// <see cref="RuleSets"/> entry by class value.</summary>
        public ClassDefTable? InputClassDef { get; init; }

        /// <summary>Format 2, chaining (Lookup Type 6) only: classifies backtrack positions.</summary>
        public ClassDefTable? BacktrackClassDef { get; init; }

        /// <summary>Format 2, chaining (Lookup Type 6) only: classifies lookahead positions.</summary>
        public ClassDefTable? LookaheadClassDef { get; init; }

        /// <summary>Formats 1/2 only: indexed by coverage index (format 1) or class value (format 2);
        /// each entry is the set of rules that may apply starting at that first glyph.</summary>
        public GsubSequenceRule[][]? RuleSets { get; init; }

        /// <summary>Format 3, chaining (Lookup Type 6) only: one Coverage table per backtrack
        /// position, in reverse logical order.</summary>
        public CoverageTable[]? BacktrackCoverages { get; init; }

        /// <summary>Format 3 only: one Coverage table per input position (including the first).</summary>
        public CoverageTable[]? InputCoverages { get; init; }

        /// <summary>Format 3, chaining (Lookup Type 6) only: one Coverage table per lookahead
        /// position, in forward logical order.</summary>
        public CoverageTable[]? LookaheadCoverages { get; init; }

        /// <summary>Format 3 only: this subtable's single rule's lookup applications (formats 1/2
        /// carry these per-rule instead, on <see cref="GsubSequenceRule.SeqLookupRecords"/>).</summary>
        public GsubSequenceLookupRecord[]? SeqLookupRecords { get; init; }
    }

    /// <summary>A GSUB lookup of type 5 (or type 7 wrapping type 5), as one or more subtables.</summary>
    internal sealed class GsubContextualLookup
    {
        public required IReadOnlyList<GsubSequenceContextSubtable> Subtables { get; init; }
        public ushort LookupFlag { get; init; }
        public int? MarkFilteringSetIndex { get; init; }
    }

    /// <summary>A GSUB lookup of type 6 (or type 7 wrapping type 6), as one or more subtables.</summary>
    internal sealed class GsubChainingContextLookup
    {
        public required IReadOnlyList<GsubSequenceContextSubtable> Subtables { get; init; }
        public ushort LookupFlag { get; init; }
        public int? MarkFilteringSetIndex { get; init; }
    }

    internal sealed class GsubTable
    {
        private readonly OpenTypeFontface _face;
        private readonly int _scriptListOffset;
        private readonly int _featureListOffset;
        private readonly int _lookupListOffset;

        // A GsubTable instance is cached and shared process-wide across concurrently-rendering
        // PdfGenerator instances (see OpenTypeFontface/FontFactory's caching), so this needs to be
        // safe for concurrent reads/writes rather than a plain Dictionary. The dictionary alone isn't
        // enough, though: computing a not-yet-cached entry still means sequential _face.Position reads
        // (see GetActiveLookupIndices/ReadLigatureLookup's own locking on _face) against the same
        // shared, mutable-cursor OpenTypeFontface.
        private readonly ConcurrentDictionary<int, GsubLigatureLookup?> _ligatureLookupCache = new();

        // Sibling caches for every other lookup-type reader and the generic type-dispatch helper,
        // same process-wide-shared-instance rationale as _ligatureLookupCache above.
        private readonly ConcurrentDictionary<int, GsubSingleSubstitutionLookup?> _singleSubstitutionLookupCache = new();
        private readonly ConcurrentDictionary<int, GsubAlternateSubstitutionLookup?> _alternateSubstitutionLookupCache = new();
        private readonly ConcurrentDictionary<int, GsubMultipleSubstitutionLookup?> _multipleSubstitutionLookupCache = new();
        private readonly ConcurrentDictionary<int, GsubContextualLookup?> _contextualLookupCache = new();
        private readonly ConcurrentDictionary<int, GsubChainingContextLookup?> _chainingContextLookupCache = new();
        private readonly ConcurrentDictionary<int, int> _resolvedLookupTypeCache = new();

        public GsubTable(OpenTypeFontface face, int tableStart)
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
        /// </summary>
        public SortedSet<int> GetActiveLookupIndices(IReadOnlyList<string> scriptTagPreference, IReadOnlySet<string> featureTags)
            => GetActiveLookupIndices(scriptTagPreference, null, featureTags);

        /// <summary>
        /// As above, but selecting the script's <paramref name="languageTag"/>-tagged `LangSys`
        /// (an OpenType 4-character language-system tag, e.g. <c>"ENG "</c>) instead of its
        /// `DefaultLangSys`, when the script actually defines one for that tag - falling back to
        /// `DefaultLangSys` exactly as the no-language overload does when <paramref name="languageTag"/>
        /// is null, absent from the script, or the script defines no `DefaultLangSys` at all.
        /// </summary>
        public SortedSet<int> GetActiveLookupIndices(IReadOnlyList<string> scriptTagPreference, string? languageTag, IReadOnlySet<string> featureTags)
        {
            // OpenTypeFontface.Position is a plain mutable field on an instance that is cached and
            // shared process-wide (OpenTypeFontfaceCache), so two threads shaping concurrently on the
            // same cached font would otherwise interleave their Position writes/reads against each
            // other. Unlike GetLigatureLookup below, this method's result isn't cached at all - it
            // re-reads the ScriptList/FeatureList tables on every single Shape() call - so it is by far
            // the widest, most frequently hit critical section, and locking on the shared face closes
            // that race. See the CI regression this fixed: issue #543.
            lock (_face)
            {
                var lookupIndices = new SortedSet<int>();

                int scriptOffset = FindScript(scriptTagPreference);
                if (scriptOffset < 0)
                    return lookupIndices;

                int langSysOffset = FindLangSys(scriptOffset, languageTag);
                if (langSysOffset == 0)
                    return lookupIndices;

                _face.Position = scriptOffset + langSysOffset;
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

        /// <summary>Parses (and caches) the lookup at <paramref name="lookupListIndex"/> as a
        /// ligature-substitution lookup, or null if it isn't one (or is an unsupported lookup type).</summary>
        public GsubLigatureLookup? GetLigatureLookup(int lookupListIndex)
            => _ligatureLookupCache.GetOrAdd(lookupListIndex, ReadLigatureLookup);

        /// <summary>Parses (and caches) the lookup at <paramref name="lookupListIndex"/> as a
        /// single-substitution lookup, or null if it isn't one (or is an unsupported lookup type).</summary>
        public GsubSingleSubstitutionLookup? GetSingleSubstitutionLookup(int lookupListIndex)
            => _singleSubstitutionLookupCache.GetOrAdd(lookupListIndex, ReadSingleSubstitutionLookup);

        /// <summary>Parses (and caches) the lookup at <paramref name="lookupListIndex"/> as an
        /// alternate-substitution lookup, or null if it isn't one (or is an unsupported lookup type).</summary>
        public GsubAlternateSubstitutionLookup? GetAlternateSubstitutionLookup(int lookupListIndex)
            => _alternateSubstitutionLookupCache.GetOrAdd(lookupListIndex, ReadAlternateSubstitutionLookup);

        /// <summary>Parses (and caches) the lookup at <paramref name="lookupListIndex"/> as a
        /// multiple-substitution lookup, or null if it isn't one (or is an unsupported lookup type).</summary>
        public GsubMultipleSubstitutionLookup? GetMultipleSubstitutionLookup(int lookupListIndex)
            => _multipleSubstitutionLookupCache.GetOrAdd(lookupListIndex, ReadMultipleSubstitutionLookup);

        /// <summary>Parses (and caches) the lookup at <paramref name="lookupListIndex"/> as a
        /// contextual-substitution lookup, or null if it isn't one (or is an unsupported lookup type).</summary>
        public GsubContextualLookup? GetContextualLookup(int lookupListIndex)
            => _contextualLookupCache.GetOrAdd(lookupListIndex, ReadContextualLookup);

        /// <summary>Parses (and caches) the lookup at <paramref name="lookupListIndex"/> as a
        /// chaining-context-substitution lookup, or null if it isn't one (or is an unsupported type).</summary>
        public GsubChainingContextLookup? GetChainingContextLookup(int lookupListIndex)
            => _chainingContextLookupCache.GetOrAdd(lookupListIndex, ReadChainingContextLookup);

        /// <summary>
        /// The real lookup type at <paramref name="lookupListIndex"/> - a Type 7 (Extension
        /// Substitution) lookup resolves to whatever type it wraps, so callers that need to dispatch
        /// generically (see <see cref="PeachPDF.Text.GsubShaper"/>) don't need their own unwrap logic.
        /// Returns -1 for an out-of-range index.
        /// </summary>
        public int GetResolvedLookupType(int lookupListIndex)
            => _resolvedLookupTypeCache.GetOrAdd(lookupListIndex, ReadResolvedLookupType);

        /// <summary>
        /// Whether every tag in <paramref name="requiredTags"/> independently resolves to at least
        /// one active lookup this reader can actually apply (Type 1, Type 3, or Type 7 wrapping
        /// either) - checked one tag at a time rather than as a single unioned lookup-index set, since
        /// a caller like CSS `all-small-caps` (smcp + c2sc) needs both tags present, not just one of
        /// them; a union-based count would wrongly report "supported" if only one existed. Verifying
        /// the lookup type too (not just tag presence in the FeatureList) matters just as much: a font
        /// can declare a feature tag against a lookup type this reader doesn't implement (multiple
        /// substitution, contextual substitution) - reporting "supported" for that would make a caller
        /// like <c>font-variant-caps</c> skip its synthesis fallback for a feature that then silently
        /// never actually substitutes anything, which is worse than not claiming support at all.
        /// </summary>
        public bool SupportsAllFeatureTags(IReadOnlyList<string> scriptTagPreference, IReadOnlySet<string> requiredTags)
        {
            foreach (string tag in requiredTags)
            {
                var lookupIndices = GetActiveLookupIndices(scriptTagPreference, new HashSet<string> { tag });
                if (lookupIndices.Count == 0)
                    return false;

                bool hasApplicableLookup = false;
                foreach (int lookupIndex in lookupIndices)
                {
                    if (GetSingleSubstitutionLookup(lookupIndex) is not null || GetAlternateSubstitutionLookup(lookupIndex) is not null)
                    {
                        hasApplicableLookup = true;
                        break;
                    }
                }

                if (!hasApplicableLookup)
                    return false;
            }

            return true;
        }

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

        /// <summary>
        /// Returns the Script table's `LangSys` offset (relative to <paramref name="scriptOffset"/>)
        /// for <paramref name="languageTag"/> if the script defines a `LangSysRecord` for it,
        /// otherwise its `DefaultLangSys` offset (0 if the script defines neither).
        /// </summary>
        private int FindLangSys(int scriptOffset, string? languageTag)
        {
            _face.Position = scriptOffset;
            int defaultLangSysOffset = _face.ReadUShort();

            if (languageTag is null)
                return defaultLangSysOffset;

            int langSysCount = _face.ReadUShort();
            for (int i = 0; i < langSysCount; i++)
            {
                string tag = _face.ReadTag();
                int offset = _face.ReadUShort();
                if (tag == languageTag)
                    return offset;
            }

            return defaultLangSysOffset;
        }

        /// <summary>Lookup-header info shared by every lookup-type reader below: <see cref="LookupFlag"/>,
        /// <see cref="MarkFilteringSetIndex"/> (present only when <c>lookupFlag</c>'s
        /// USE_MARK_FILTERING_SET bit, 0x0010, is set), and this lookup's subtable offsets - already
        /// unwrapped past Type 7 (Extension Substitution) down to whichever subtables actually wrap
        /// the expected type, if the lookup is Type 7.</summary>
        private readonly record struct LookupHeader(ushort LookupFlag, int? MarkFilteringSetIndex, IReadOnlyList<int> SubtableOffsets);

        /// <summary>
        /// Reads the Lookup table at <paramref name="lookupListIndex"/>'s header and subtable offsets,
        /// returning null if the index is out of range or (after unwrapping any Type 7 Extension
        /// Substitution wrapper) its resolved type isn't <paramref name="expectedType"/>. Callers must
        /// already hold `lock (_face)` - like every other sequential reader in this class, this performs
        /// a sequence of dependent reads against the shared, mutable-cursor OpenTypeFontface (see the
        /// #543 rationale on this class's cache fields).
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

            // The extra markFilteringSet field (present only when USE_MARK_FILTERING_SET is set) sits
            // immediately after the subtable-offset array, on the outer (possibly Type-7-wrapping)
            // Lookup table itself - not per-subtable - so it must be read here, before any subtable is
            // dereferenced below, or every later sequential read on this shared cursor misaligns.
            const ushort useMarkFilteringSet = 0x0010;
            int? markFilteringSetIndex = (lookupFlag & useMarkFilteringSet) != 0 ? _face.ReadUShort() : null;

            if (lookupType != expectedType && lookupType != 7)
                return null;

            var resolvedOffsets = new List<int>(subtableCount);
            foreach (int subtableOffset in subtableOffsets)
            {
                int resolvedOffset = subtableOffset;
                if (lookupType == 7)
                {
                    _face.Position = subtableOffset;
                    _face.ReadUShort(); // substFormat (always 1)
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

        private GsubLigatureLookup? ReadLigatureLookup(int lookupListIndex)
        {
            // Reached via _ligatureLookupCache.GetOrAdd, so a result is only ever computed once per
            // index and then cached - but the sequential _face.Position reads in ReadLookupHeader/
            // ReadLigatureSubtable/ReadLigatureSet (only ever reached from here) still need to be
            // serialized against GetActiveLookupIndices and any concurrent first resolution of a
            // different index, since _face is a single mutable cursor shared process-wide across
            // concurrently-rendering fonts. See issue #543.
            lock (_face)
            {
                if (ReadLookupHeader(lookupListIndex, 4) is not { } header)
                    return null;

                var subtables = new List<GsubLigatureSubtable>(header.SubtableOffsets.Count);
                foreach (int subtableOffset in header.SubtableOffsets)
                {
                    GsubLigatureSubtable? subtable = ReadLigatureSubtable(subtableOffset);
                    if (subtable is not null)
                        subtables.Add(subtable);
                }

                return subtables.Count > 0
                    ? new GsubLigatureLookup { Subtables = subtables, LookupFlag = header.LookupFlag, MarkFilteringSetIndex = header.MarkFilteringSetIndex }
                    : null;
            }
        }

        private GsubSingleSubstitutionLookup? ReadSingleSubstitutionLookup(int lookupListIndex)
        {
            // Same locking rationale as ReadLigatureLookup above - see issue #543.
            lock (_face)
            {
                if (ReadLookupHeader(lookupListIndex, 1) is not { } header)
                    return null;

                var subtables = new List<GsubSingleSubstitutionSubtable>(header.SubtableOffsets.Count);
                foreach (int subtableOffset in header.SubtableOffsets)
                {
                    GsubSingleSubstitutionSubtable? subtable = ReadSingleSubstitutionSubtable(subtableOffset);
                    if (subtable is not null)
                        subtables.Add(subtable);
                }

                return subtables.Count > 0
                    ? new GsubSingleSubstitutionLookup { Subtables = subtables, LookupFlag = header.LookupFlag, MarkFilteringSetIndex = header.MarkFilteringSetIndex }
                    : null;
            }
        }

        private GsubSingleSubstitutionSubtable? ReadSingleSubstitutionSubtable(int offset)
        {
            _face.Position = offset;
            int substFormat = _face.ReadUShort();

            if (substFormat == 1)
            {
                int coverageOffset = offset + _face.ReadUShort();
                short delta = _face.ReadShort();
                return new GsubSingleSubstitutionSubtable { Coverage = CoverageTable.Read(_face, coverageOffset), Delta = delta };
            }

            if (substFormat == 2)
            {
                int coverageOffset = offset + _face.ReadUShort();
                int glyphCount = _face.ReadUShort();
                var substitutes = new ushort[glyphCount];
                for (int i = 0; i < glyphCount; i++)
                    substitutes[i] = _face.ReadUShort();
                return new GsubSingleSubstitutionSubtable { Coverage = CoverageTable.Read(_face, coverageOffset), Substitutes = substitutes };
            }

            return null;
        }

        private GsubAlternateSubstitutionLookup? ReadAlternateSubstitutionLookup(int lookupListIndex)
        {
            // Same locking rationale as ReadLigatureLookup above - see issue #543.
            lock (_face)
            {
                if (ReadLookupHeader(lookupListIndex, 3) is not { } header)
                    return null;

                var subtables = new List<GsubAlternateSubstitutionSubtable>(header.SubtableOffsets.Count);
                foreach (int subtableOffset in header.SubtableOffsets)
                {
                    GsubAlternateSubstitutionSubtable? subtable = ReadAlternateSubstitutionSubtable(subtableOffset);
                    if (subtable is not null)
                        subtables.Add(subtable);
                }

                return subtables.Count > 0
                    ? new GsubAlternateSubstitutionLookup { Subtables = subtables, LookupFlag = header.LookupFlag, MarkFilteringSetIndex = header.MarkFilteringSetIndex }
                    : null;
            }
        }

        private GsubAlternateSubstitutionSubtable? ReadAlternateSubstitutionSubtable(int offset)
        {
            _face.Position = offset;
            int substFormat = _face.ReadUShort();
            if (substFormat != 1)
                return null;

            int coverageOffset = offset + _face.ReadUShort();
            int alternateSetCount = _face.ReadUShort();
            var alternateSetOffsets = new int[alternateSetCount];
            for (int i = 0; i < alternateSetCount; i++)
                alternateSetOffsets[i] = offset + _face.ReadUShort();

            var alternateSets = new ushort[alternateSetCount][];
            for (int i = 0; i < alternateSetCount; i++)
            {
                _face.Position = alternateSetOffsets[i];
                int glyphCount = _face.ReadUShort();
                var glyphs = new ushort[glyphCount];
                for (int j = 0; j < glyphCount; j++)
                    glyphs[j] = _face.ReadUShort();
                alternateSets[i] = glyphs;
            }

            return new GsubAlternateSubstitutionSubtable { Coverage = CoverageTable.Read(_face, coverageOffset), AlternateSets = alternateSets };
        }

        private GsubMultipleSubstitutionLookup? ReadMultipleSubstitutionLookup(int lookupListIndex)
        {
            // Same locking rationale as ReadLigatureLookup above - see issue #543.
            lock (_face)
            {
                if (ReadLookupHeader(lookupListIndex, 2) is not { } header)
                    return null;

                var subtables = new List<GsubMultipleSubstitutionSubtable>(header.SubtableOffsets.Count);
                foreach (int subtableOffset in header.SubtableOffsets)
                {
                    GsubMultipleSubstitutionSubtable? subtable = ReadMultipleSubstitutionSubtable(subtableOffset);
                    if (subtable is not null)
                        subtables.Add(subtable);
                }

                return subtables.Count > 0
                    ? new GsubMultipleSubstitutionLookup { Subtables = subtables, LookupFlag = header.LookupFlag, MarkFilteringSetIndex = header.MarkFilteringSetIndex }
                    : null;
            }
        }

        private GsubMultipleSubstitutionSubtable? ReadMultipleSubstitutionSubtable(int offset)
        {
            _face.Position = offset;
            int substFormat = _face.ReadUShort();
            if (substFormat != 1)
                return null;

            int coverageOffset = offset + _face.ReadUShort();
            int sequenceCount = _face.ReadUShort();
            var sequenceOffsets = new int[sequenceCount];
            for (int i = 0; i < sequenceCount; i++)
                sequenceOffsets[i] = offset + _face.ReadUShort();

            var sequences = new ushort[sequenceCount][];
            for (int i = 0; i < sequenceCount; i++)
            {
                _face.Position = sequenceOffsets[i];
                int glyphCount = _face.ReadUShort();
                if (glyphCount == 0)
                {
                    // Spec-prohibited (this lookup type must not delete a glyph) - treated as "no
                    // substitution available" rather than a special zero-glyph expansion case.
                    sequences[i] = [];
                    continue;
                }
                var glyphs = new ushort[glyphCount];
                for (int j = 0; j < glyphCount; j++)
                    glyphs[j] = _face.ReadUShort();
                sequences[i] = glyphs;
            }

            return new GsubMultipleSubstitutionSubtable { Coverage = CoverageTable.Read(_face, coverageOffset), Sequences = sequences };
        }

        private GsubContextualLookup? ReadContextualLookup(int lookupListIndex)
        {
            // Same locking rationale as ReadLigatureLookup above - see issue #543.
            lock (_face)
            {
                if (ReadLookupHeader(lookupListIndex, 5) is not { } header)
                    return null;

                var subtables = new List<GsubSequenceContextSubtable>(header.SubtableOffsets.Count);
                foreach (int subtableOffset in header.SubtableOffsets)
                {
                    GsubSequenceContextSubtable? subtable = ReadSequenceContextSubtable(subtableOffset);
                    if (subtable is not null)
                        subtables.Add(subtable);
                }

                return subtables.Count > 0
                    ? new GsubContextualLookup { Subtables = subtables, LookupFlag = header.LookupFlag, MarkFilteringSetIndex = header.MarkFilteringSetIndex }
                    : null;
            }
        }

        private GsubChainingContextLookup? ReadChainingContextLookup(int lookupListIndex)
        {
            // Same locking rationale as ReadLigatureLookup above - see issue #543.
            lock (_face)
            {
                if (ReadLookupHeader(lookupListIndex, 6) is not { } header)
                    return null;

                var subtables = new List<GsubSequenceContextSubtable>(header.SubtableOffsets.Count);
                foreach (int subtableOffset in header.SubtableOffsets)
                {
                    GsubSequenceContextSubtable? subtable = ReadChainedSequenceContextSubtable(subtableOffset);
                    if (subtable is not null)
                        subtables.Add(subtable);
                }

                return subtables.Count > 0
                    ? new GsubChainingContextLookup { Subtables = subtables, LookupFlag = header.LookupFlag, MarkFilteringSetIndex = header.MarkFilteringSetIndex }
                    : null;
            }
        }

        /// <summary>Reads a non-chaining `SequenceContext` subtable (Lookup Type 5), formats 1/2/3.</summary>
        private GsubSequenceContextSubtable? ReadSequenceContextSubtable(int offset)
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

                var ruleSets = new GsubSequenceRule[ruleSetCount][];
                for (int i = 0; i < ruleSetCount; i++)
                    ruleSets[i] = ruleSetOffsets[i] != 0 ? ReadSequenceRuleSet(ruleSetOffsets[i]) : [];

                return new GsubSequenceContextSubtable
                {
                    Format = GsubSequenceContextFormat.Glyph,
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

                var ruleSets = new GsubSequenceRule[ruleSetCount][];
                for (int i = 0; i < ruleSetCount; i++)
                    ruleSets[i] = ruleSetOffsets[i] != 0 ? ReadSequenceRuleSet(ruleSetOffsets[i]) : [];

                return new GsubSequenceContextSubtable
                {
                    Format = GsubSequenceContextFormat.Class,
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
                var records = new GsubSequenceLookupRecord[seqLookupCount];
                for (int i = 0; i < seqLookupCount; i++)
                {
                    int sequenceIndex = _face.ReadUShort();
                    int lookupListIndex = _face.ReadUShort();
                    records[i] = new GsubSequenceLookupRecord(sequenceIndex, lookupListIndex);
                }

                var inputCoverages = new CoverageTable[glyphCount];
                for (int i = 0; i < glyphCount; i++)
                    inputCoverages[i] = CoverageTable.Read(_face, coverageOffsets[i]);

                return new GsubSequenceContextSubtable
                {
                    Format = GsubSequenceContextFormat.Coverage,
                    InputCoverages = inputCoverages,
                    SeqLookupRecords = records,
                };
            }

            return null;
        }

        /// <summary>Reads a `ChainedSequenceContext` subtable (Lookup Type 6), formats 1/2/3.</summary>
        private GsubSequenceContextSubtable? ReadChainedSequenceContextSubtable(int offset)
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

                var ruleSets = new GsubSequenceRule[ruleSetCount][];
                for (int i = 0; i < ruleSetCount; i++)
                    ruleSets[i] = ruleSetOffsets[i] != 0 ? ReadChainedSequenceRuleSet(ruleSetOffsets[i]) : [];

                return new GsubSequenceContextSubtable
                {
                    Format = GsubSequenceContextFormat.Glyph,
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

                var ruleSets = new GsubSequenceRule[ruleSetCount][];
                for (int i = 0; i < ruleSetCount; i++)
                    ruleSets[i] = ruleSetOffsets[i] != 0 ? ReadChainedSequenceRuleSet(ruleSetOffsets[i]) : [];

                return new GsubSequenceContextSubtable
                {
                    Format = GsubSequenceContextFormat.Class,
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
                var records = new GsubSequenceLookupRecord[seqLookupCount];
                for (int i = 0; i < seqLookupCount; i++)
                {
                    int sequenceIndex = _face.ReadUShort();
                    int lookupListIndex = _face.ReadUShort();
                    records[i] = new GsubSequenceLookupRecord(sequenceIndex, lookupListIndex);
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

                return new GsubSequenceContextSubtable
                {
                    Format = GsubSequenceContextFormat.Coverage,
                    BacktrackCoverages = backtrackCoverages,
                    InputCoverages = inputCoverages,
                    LookaheadCoverages = lookaheadCoverages,
                    SeqLookupRecords = records,
                };
            }

            return null;
        }

        private GsubSequenceRule[] ReadSequenceRuleSet(int ruleSetOffset)
        {
            _face.Position = ruleSetOffset;
            int ruleCount = _face.ReadUShort();
            var ruleOffsets = new int[ruleCount];
            for (int i = 0; i < ruleCount; i++)
                ruleOffsets[i] = ruleSetOffset + _face.ReadUShort();

            var rules = new GsubSequenceRule[ruleCount];
            for (int i = 0; i < ruleCount; i++)
                rules[i] = ReadSequenceRule(ruleOffsets[i]);
            return rules;
        }

        private GsubSequenceRule ReadSequenceRule(int ruleOffset)
        {
            _face.Position = ruleOffset;
            int glyphCount = _face.ReadUShort();
            int seqLookupCount = _face.ReadUShort();
            var input = new ushort[glyphCount - 1];
            for (int i = 0; i < input.Length; i++)
                input[i] = _face.ReadUShort();
            var records = new GsubSequenceLookupRecord[seqLookupCount];
            for (int i = 0; i < seqLookupCount; i++)
            {
                int sequenceIndex = _face.ReadUShort();
                int lookupListIndex = _face.ReadUShort();
                records[i] = new GsubSequenceLookupRecord(sequenceIndex, lookupListIndex);
            }

            return new GsubSequenceRule { Backtrack = [], Input = input, Lookahead = [], SeqLookupRecords = records };
        }

        private GsubSequenceRule[] ReadChainedSequenceRuleSet(int ruleSetOffset)
        {
            _face.Position = ruleSetOffset;
            int ruleCount = _face.ReadUShort();
            var ruleOffsets = new int[ruleCount];
            for (int i = 0; i < ruleCount; i++)
                ruleOffsets[i] = ruleSetOffset + _face.ReadUShort();

            var rules = new GsubSequenceRule[ruleCount];
            for (int i = 0; i < ruleCount; i++)
                rules[i] = ReadChainedSequenceRule(ruleOffsets[i]);
            return rules;
        }

        private GsubSequenceRule ReadChainedSequenceRule(int ruleOffset)
        {
            _face.Position = ruleOffset;

            int backtrackGlyphCount = _face.ReadUShort();
            var backtrack = new ushort[backtrackGlyphCount];
            for (int i = 0; i < backtrack.Length; i++)
                backtrack[i] = _face.ReadUShort();

            int inputGlyphCount = _face.ReadUShort();
            var input = new ushort[inputGlyphCount - 1];
            for (int i = 0; i < input.Length; i++)
                input[i] = _face.ReadUShort();

            int lookaheadGlyphCount = _face.ReadUShort();
            var lookahead = new ushort[lookaheadGlyphCount];
            for (int i = 0; i < lookahead.Length; i++)
                lookahead[i] = _face.ReadUShort();

            int seqLookupCount = _face.ReadUShort();
            var records = new GsubSequenceLookupRecord[seqLookupCount];
            for (int i = 0; i < seqLookupCount; i++)
            {
                int sequenceIndex = _face.ReadUShort();
                int lookupListIndex = _face.ReadUShort();
                records[i] = new GsubSequenceLookupRecord(sequenceIndex, lookupListIndex);
            }

            return new GsubSequenceRule { Backtrack = backtrack, Input = input, Lookahead = lookahead, SeqLookupRecords = records };
        }

        private int ReadResolvedLookupType(int lookupListIndex)
        {
            // Same locking rationale as ReadLigatureLookup above - see issue #543.
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
                if (lookupType != 7)
                    return lookupType;

                _face.ReadUShort(); // lookupFlag
                int subtableCount = _face.ReadUShort();
                if (subtableCount == 0)
                    return lookupType;
                int firstSubtableOffset = lookupTableStart + _face.ReadUShort();

                _face.Position = firstSubtableOffset;
                _face.ReadUShort(); // substFormat (always 1)
                return _face.ReadUShort(); // extensionLookupType - the real type this Type 7 wraps.
            }
        }

        private GsubLigatureSubtable? ReadLigatureSubtable(int offset)
        {
            _face.Position = offset;
            int substFormat = _face.ReadUShort();
            if (substFormat != 1)
                return null;

            int coverageOffset = offset + _face.ReadUShort();
            int ligatureSetCount = _face.ReadUShort();
            var ligatureSetOffsets = new int[ligatureSetCount];
            for (int i = 0; i < ligatureSetCount; i++)
                ligatureSetOffsets[i] = offset + _face.ReadUShort();

            var ligatureSets = new GsubLigature[ligatureSetCount][];
            for (int i = 0; i < ligatureSetCount; i++)
                ligatureSets[i] = ReadLigatureSet(ligatureSetOffsets[i]);

            // Coverage is read last: reading it moves the cursor past the ligature sets we just
            // walked, and we no longer need sequential position after this.
            CoverageTable coverage = CoverageTable.Read(_face, coverageOffset);
            return new GsubLigatureSubtable { Coverage = coverage, LigatureSets = ligatureSets };
        }

        private GsubLigature[] ReadLigatureSet(int ligatureSetOffset)
        {
            _face.Position = ligatureSetOffset;
            int ligatureCount = _face.ReadUShort();
            var ligatureOffsets = new int[ligatureCount];
            for (int i = 0; i < ligatureCount; i++)
                ligatureOffsets[i] = ligatureSetOffset + _face.ReadUShort();

            var ligatures = new GsubLigature[ligatureCount];
            for (int i = 0; i < ligatureCount; i++)
            {
                _face.Position = ligatureOffsets[i];
                ushort ligatureGlyph = _face.ReadUShort();
                int componentCount = _face.ReadUShort();
                var components = new ushort[componentCount - 1];
                for (int j = 0; j < components.Length; j++)
                    components[j] = _face.ReadUShort();
                ligatures[i] = new GsubLigature(ligatureGlyph, components);
            }

            return ligatures;
        }
    }
}

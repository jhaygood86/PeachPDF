#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Reader for the OpenType `GSUB` (Glyph Substitution) table: the ScriptList/FeatureList/LookupList
// common tables, and Lookup Type 4 (Ligature Substitution) subtables - Type 9 (Extension
// Substitution) is unwrapped down to the Type 4 subtable it wraps when present. This is the
// subset needed to apply the `liga`/`clig`/`rlig` ligature features.
//
// Not implemented (see .claude/accepted-gaps/no-text-shaping.md): contextual lookups (types 5-8)
// referenced by a ligature feature, GDEF-based mark filtering of a lookup's `lookupFlag`, and
// per-language (non-default LangSys) feature selection. GPOS/GDEF are unrelated tables and are
// not read here.
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
    /// One ligature: <see cref="ComponentGlyphIds"/> are the glyphs that must immediately follow
    /// the coverage-matched first glyph for <see cref="LigatureGlyph"/> to apply.
    /// </summary>
    internal readonly record struct GsubLigature(ushort LigatureGlyph, ushort[] ComponentGlyphIds);

    /// <summary>One Ligature Substitution (format 1) subtable: a <see cref="Coverage"/> table over
    /// possible first glyphs, each mapping (by coverage index) to the ligatures it may start.</summary>
    internal sealed class GsubLigatureSubtable
    {
        public required CoverageTable Coverage { get; init; }
        public required GsubLigature[][] LigatureSets { get; init; }
    }

    /// <summary>A GSUB lookup of type 4 (or type 9 wrapping type 4), as one or more subtables.</summary>
    internal sealed class GsubLigatureLookup
    {
        public required IReadOnlyList<GsubLigatureSubtable> Subtables { get; init; }
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
        /// back to the first script record if none match), using only that script's default language
        /// system - a font-defined `required` feature is always included alongside the requested tags.
        /// </summary>
        public SortedSet<int> GetActiveLookupIndices(IReadOnlyList<string> scriptTagPreference, IReadOnlySet<string> featureTags)
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

        /// <summary>Parses (and caches) the lookup at <paramref name="lookupListIndex"/> as a
        /// ligature-substitution lookup, or null if it isn't one (or is an unsupported lookup type).</summary>
        public GsubLigatureLookup? GetLigatureLookup(int lookupListIndex)
            => _ligatureLookupCache.GetOrAdd(lookupListIndex, ReadLigatureLookup);

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

        private GsubLigatureLookup? ReadLigatureLookup(int lookupListIndex)
        {
            // Reached via _ligatureLookupCache.GetOrAdd, so a result is only ever computed once per
            // index and then cached - but the sequential _face.Position reads below (and in the
            // ReadLigatureSubtable/ReadLigatureSet helpers this calls, only ever reached from here)
            // still need to be serialized against GetActiveLookupIndices and any concurrent first
            // resolution of a different index, since _face is a single mutable cursor shared
            // process-wide across concurrently-rendering fonts. See issue #543.
            lock (_face)
            {
                _face.Position = _lookupListOffset;
                int lookupCount = _face.ReadUShort();
                if (lookupListIndex < 0 || lookupListIndex >= lookupCount)
                    return null;

                _face.Position = _lookupListOffset + 2 + lookupListIndex * 2;
                int lookupTableStart = _lookupListOffset + _face.ReadUShort();

                _face.Position = lookupTableStart;
                int lookupType = _face.ReadUShort();
                _face.ReadUShort(); // lookupFlag - GDEF mark filtering not implemented, see file header.
                int subtableCount = _face.ReadUShort();
                var subtableOffsets = new int[subtableCount];
                for (int i = 0; i < subtableCount; i++)
                    subtableOffsets[i] = lookupTableStart + _face.ReadUShort();

                // Only plain Ligature Substitution (4) and Extension Substitution (9) wrapping it are
                // understood; every other lookup type (single/multiple/alternate/contextual/reverse
                // chaining substitution) is skipped rather than mis-applied.
                if (lookupType != 4 && lookupType != 9)
                    return null;

                var subtables = new List<GsubLigatureSubtable>(subtableCount);
                foreach (int subtableOffset in subtableOffsets)
                {
                    int resolvedOffset = subtableOffset;
                    if (lookupType == 9)
                    {
                        _face.Position = subtableOffset;
                        _face.ReadUShort(); // substFormat (always 1)
                        int extensionLookupType = _face.ReadUShort();
                        uint extensionOffset = _face.ReadULong();
                        if (extensionLookupType != 4)
                            continue;
                        resolvedOffset = subtableOffset + (int)extensionOffset;
                    }

                    GsubLigatureSubtable? subtable = ReadLigatureSubtable(resolvedOffset);
                    if (subtable is not null)
                        subtables.Add(subtable);
                }

                return subtables.Count > 0 ? new GsubLigatureLookup { Subtables = subtables } : null;
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

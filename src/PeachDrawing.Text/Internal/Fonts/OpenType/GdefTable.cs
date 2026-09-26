#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Reader for the OpenType `GDEF` (Glyph Definition) table: `GlyphClassDef` and `MarkAttachClassDef`
// (both `ClassDef` tables, see `ClassDefTable`), and `MarkGlyphSetsDef` (GDEF 1.2+) - the subset
// needed for `lookupFlag`-driven mark filtering in GSUB/GPOS lookup application (see
// `PeachDrawing.Text.Internal.Text.GlyphSequenceFilter`).
//
// Not read: `AttachList` (attachment-point coordinates for hinting/bitmap caching - irrelevant, no
// hinting), `LigCaretList` (ligature caret positions for interactive text editors - irrelevant,
// PeachPDF produces static PDF output). `ItemVariationStore` (GDEF 1.3) is read: it holds the deltas the
// VariationIndex device tables of GPOS name, for an instance of a variable font.
//
// https://learn.microsoft.com/en-us/typography/opentype/spec/gdef
//
#endregion

using System;

namespace PeachDrawing.Text.Internal.Fonts.OpenType
{
    internal sealed class GdefTable
    {
        private readonly ClassDefTable? _glyphClassDef;
        private readonly ClassDefTable? _markAttachClassDef;
        private readonly CoverageTable?[] _markGlyphSets;

        /// <summary>The item variation store of a GDEF 1.3 table, whose deltas the <c>VariationIndex</c> device tables of GPOS refer to, or null.</summary>
        public Variations.ItemVariationStore? VariationStore { get; }

        public GdefTable(OpenTypeFontface face, int tableStart)
        {
            // GdefTable instances are cached and shared process-wide, exactly like GsubTable/
            // GposTable (see the #543 rationale in GsubTable.cs) - lock around every sequential
            // read against the shared, mutable-cursor OpenTypeFontface.
            lock (face.SyncRoot)
            {
                face.Position = tableStart;
                face.ReadUShort(); // majorVersion
                int minorVersion = face.ReadUShort();

                // Read every header offset first, before dereferencing any of them - ClassDefTable.
                // Read/CoverageTable.Read below move the shared cursor as a side effect, so any
                // offset not yet captured before that point would be lost.
                int glyphClassDefOffset = face.ReadUShort();
                face.ReadUShort(); // attachListOffset - not read, see file header
                face.ReadUShort(); // ligCaretListOffset - not read, see file header
                int markAttachClassDefOffset = face.ReadUShort();
                int markGlyphSetsDefOffset = minorVersion >= 2 ? face.ReadUShort() : 0;
                uint itemVarStoreOffset = minorVersion >= 3 ? face.ReadULong() : 0;

                _glyphClassDef = glyphClassDefOffset != 0
                    ? ClassDefTable.Read(face, tableStart + glyphClassDefOffset)
                    : null;
                _markAttachClassDef = markAttachClassDefOffset != 0
                    ? ClassDefTable.Read(face, tableStart + markAttachClassDefOffset)
                    : null;
                _markGlyphSets = markGlyphSetsDefOffset != 0
                    ? ReadMarkGlyphSetsDef(face, tableStart + markGlyphSetsDefOffset)
                    : [];

                if (itemVarStoreOffset != 0 && face.TableDictionary.TryGetValue(TableTagNames.GDEF, out var entry))
                {
                    var bytes = face.FontSource.Bytes;
                    int length = Math.Min(entry.Length, bytes.Length - entry.Offset);
                    VariationStore = length > itemVarStoreOffset
                        ? Variations.ItemVariationStore.TryParse(bytes.AsSpan(entry.Offset, length), (int)itemVarStoreOffset)
                        : null;
                }
            }
        }

        /// <summary>Returns the glyph's `GlyphClassDef` class (0 = unclassified, 1 = Base, 2 =
        /// Ligature, 3 = Mark, 4 = Component), or 0 if this font has no `GlyphClassDef`.</summary>
        public int GetGlyphClass(ushort glyphId) => _glyphClassDef?.GetClass(glyphId) ?? 0;

        /// <summary>Returns the glyph's `MarkAttachClassDef` class (0 = none/unclassified), or 0 if
        /// this font has no `MarkAttachClassDef`.</summary>
        public int GetMarkAttachClass(ushort glyphId) => _markAttachClassDef?.GetClass(glyphId) ?? 0;

        /// <summary>Returns one of `MarkGlyphSetsDef`'s mark-filtering-set Coverage tables by index
        /// (as named by a lookup's trailing `markFilteringSet` field when `lookupFlag`'s
        /// `USE_MARK_FILTERING_SET` bit is set), or null if out of range / absent.</summary>
        public CoverageTable? GetMarkGlyphSet(int index)
            => index >= 0 && index < _markGlyphSets.Length ? _markGlyphSets[index] : null;

        private static CoverageTable?[] ReadMarkGlyphSetsDef(OpenTypeFontface face, int offset)
        {
            face.Position = offset;
            face.ReadUShort(); // markSetTableFormat (always 1)
            int markSetCount = face.ReadUShort();

            // Unlike every other offset array in this codebase, MarkGlyphSetsDef's own coverage
            // offsets are Offset32 (uint32), not Offset16 - a real, deliberate format irregularity,
            // not a typo to "fix" to ReadUShort by copy-paste habit from elsewhere in this file.
            var setOffsets = new uint[markSetCount];
            for (int i = 0; i < markSetCount; i++)
                setOffsets[i] = face.ReadULong();

            var sets = new CoverageTable?[markSetCount];
            for (int i = 0; i < markSetCount; i++)
                sets[i] = setOffsets[i] != 0 ? CoverageTable.Read(face, offset + (int)setOffsets[i]) : null;

            return sets;
        }
    }
}

#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   Stefan Lange
//
// Copyright (c) 2005-2016 empira Software GmbH, Cologne Area (Germany)
//
// https://www.pdfsharp.com/
// http://sourceforge.net/projects/pdfsharp
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
#endregion

#nullable disable warnings

#define VERBOSE_

using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Fixed = System.Int32;

#pragma warning disable 0649

namespace PeachDrawing.Text.Internal.Fonts.OpenType
{
    /// <summary>
    /// Represents an OpenType fontface in memory.
    /// </summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    internal sealed class OpenTypeFontface
    {
        // Implementation Notes
        // OpenTypeFontface represents a 'decompiled' font file in memory.
        //
        // * An OpenTypeFontface can belong to more than one 
        //   XGlyphTypeface because of StyleSimulations.
        //
        // * Currently there is a one to one relationship to FontFileData.
        // 
        // * Consider OpenTypeFontface as an decompiled FontFileData.
        //
        // http://www.microsoft.com/typography/otspec/

        /// <summary>
        /// Shallow copy for font subset.
        /// </summary>
        OpenTypeFontface(OpenTypeFontface fontface)
        {
            _offsetTable = fontface._offsetTable;
            _fullFaceName = fontface._fullFaceName;
        }

        public OpenTypeFontface(FontFileData fontSource)
        {
            FontSource = fontSource;
            Read();
            _fullFaceName = name.FullFontName;
        }

        /// <summary>
        /// Gets the full face name from the name table.
        /// Name is also used as the key.
        /// </summary>
        public string FullFaceName
        {
            get { return _fullFaceName; }
        }
        readonly string _fullFaceName;

        public ulong CheckSum
        {
            get
            {
                if (_checkSum == 0)
                    _checkSum = FontFileData.CalcChecksum(FontSource.Bytes);
                return _checkSum;
            }
        }
        ulong _checkSum;

        /// <summary>
        /// Gets the bytes that represents the font data.
        /// </summary>
        public FontFileData FontSource
        {
            get { return _fontSource; }
            private set
            {
                // Stop working if font was not found.
                if (value == null)
                    throw new InvalidOperationException("Font cannot be resolved.");
                _fontSource = value;
            }
        }
        FontFileData _fontSource = null!;

        internal FontTechnology _fontTechnology;

        internal OffsetTable _offsetTable;

        /// <summary>
        /// The dictionary of all font tables.
        /// </summary>
        internal Dictionary<string, TableDirectoryEntry> TableDictionary = new Dictionary<string, TableDirectoryEntry>();

        // Keep names identical to OpenType spec.
        // ReSharper disable InconsistentNaming
        internal CMapTable cmap = null!;
        internal ControlValueTable cvt = null!;
        internal FontProgram fpgm = null!;
        internal MaximumProfileTable maxp = null!;
        internal NameTable name = null!;
        internal ControlValueProgram prep = null!;
        internal FontHeaderTable head = null!;
        internal HorizontalHeaderTable hhea = null!;
        internal HorizontalMetricsTable hmtx = null!;
        internal OS2Table os2 = null!;
        internal PostScriptTable post = null!;
        internal GlyphDataTable glyf = null!;
        internal IndexToLocationTable loca = null!;
        internal CffTable cff = null!; // optional - a TrueType-outline font has no CFF table at all
        internal GlyphSubstitutionTable gsub = null!;
        internal GlyphDefinitionTable gdef = null!; // optional - absent on many fonts
        internal GlyphPositioningTable gpos = null!; // optional - absent on many fonts
        internal ColrTable colr = null!;
        internal CpalTable cpal = null!;

        /// <summary>The bitmap colour glyphs (CBDT/CBLC or sbix), or null when the font has none.</summary>
        internal BitmapGlyphSource? bitmap;

        /// <summary>
        /// True when this font draws colour glyphs: COLR + CPAL layers over glyf outlines, or CBDT/CBLC/sbix
        /// bitmaps. (A COLR font over CFF outlines is not one this renderer can paint, so it is not counted.)
        /// </summary>
        internal bool IsColorFont => (colr != null && cpal != null && glyf != null) || bitmap != null;
        internal GlyphMathTable math = null!; // optional - only dedicated math fonts carry one
        internal VerticalHeaderTable vhea = null!; // optional - absent on purely-horizontal fonts
        internal VerticalMetricsTable vmtx = null!; // optional - absent on purely-horizontal fonts
        internal VerticalOriginTable vorg = null!; // optional - mainly CFF-flavored CJK fonts
        // ReSharper restore InconsistentNaming

        public bool CanRead
        {
            get { return FontSource != null; }
        }

        public bool CanWrite
        {
            get { return FontSource == null; }
        }

        // AddTable's per-tag field assignment, keyed by DirectoryEntry.Tag. A tag with no entry here
        // (e.g. kern) is recorded in TableDictionary but has no dedicated typed field.
        static readonly FrozenDictionary<string, Action<OpenTypeFontface, OpenTypeFontTable>> TableAssigners =
            new Dictionary<string, Action<OpenTypeFontface, OpenTypeFontTable>>
            {
                [TableTagNames.CMap] = (f, t) => f.cmap = t as CMapTable,
                [TableTagNames.Cvt] = (f, t) => f.cvt = t as ControlValueTable,
                [TableTagNames.Fpgm] = (f, t) => f.fpgm = t as FontProgram,
                [TableTagNames.MaxP] = (f, t) => f.maxp = t as MaximumProfileTable,
                [TableTagNames.Name] = (f, t) => f.name = t as NameTable,
                [TableTagNames.Head] = (f, t) => f.head = t as FontHeaderTable,
                [TableTagNames.HHea] = (f, t) => f.hhea = t as HorizontalHeaderTable,
                [TableTagNames.HMtx] = (f, t) => f.hmtx = t as HorizontalMetricsTable,
                [TableTagNames.OS2] = (f, t) => f.os2 = t as OS2Table,
                [TableTagNames.Post] = (f, t) => f.post = t as PostScriptTable,
                [TableTagNames.Glyf] = (f, t) => f.glyf = t as GlyphDataTable,
                [TableTagNames.Loca] = (f, t) => f.loca = t as IndexToLocationTable,
                [TableTagNames.GSUB] = (f, t) => f.gsub = t as GlyphSubstitutionTable,
                [TableTagNames.GDEF] = (f, t) => f.gdef = t as GlyphDefinitionTable,
                [TableTagNames.GPOS] = (f, t) => f.gpos = t as GlyphPositioningTable,
                [TableTagNames.Prep] = (f, t) => f.prep = t as ControlValueProgram,
            }.ToFrozenDictionary();

        /// <summary>
        /// Adds the specified table to this font image.
        /// </summary>
        public void AddTable(OpenTypeFontTable fontTable)
        {
            if (!CanWrite)
                throw new InvalidOperationException("Font image cannot be modified.");

            ArgumentNullException.ThrowIfNull(fontTable);

            if (fontTable._fontData == null)
            {
                fontTable._fontData = this;
            }
            else
            {
                Debug.Assert(fontTable._fontData.CanRead);
                // Create a reference to this font table
                fontTable = new IRefFontTable(this, fontTable);
            }

            TableDictionary[fontTable.DirectoryEntry.Tag] = fontTable.DirectoryEntry;

            if (TableAssigners.TryGetValue(fontTable.DirectoryEntry.Tag, out var assign))
                assign(this, fontTable);
        }

        /// <summary>
        /// Reads all required tables from the font data.
        /// </summary>
        internal void Read()
        {
            // Determine font technology
            // ReSharper disable InconsistentNaming
            const uint OTTO = 0x4f54544f;  // Adobe OpenType CFF data, tag: 'OTTO'
            const uint TTCF = 0x74746366;  // TrueType Collection tag: 'ttcf'  
            // ReSharper restore InconsistentNaming
            try
            {
#if DEBUG_
                if (Name == "Cambria")
                    Debug-Break.Break();
#endif

                // Check if data is a TrueType collection font.
                uint startTag = ReadULong();
                if (startTag == TTCF)
                {
                    _fontTechnology = FontTechnology.TrueTypeCollection;
                    throw new InvalidOperationException("TrueType collection fonts are not supported here; a face must be extracted first.");
                }

                // Read offset table
                _offsetTable.Version = startTag;
                _offsetTable.TableCount = ReadUShort();
                _offsetTable.SearchRange = ReadUShort();
                _offsetTable.EntrySelector = ReadUShort();
                _offsetTable.RangeShift = ReadUShort();

                // Move to table dictionary at position 12
                Debug.Assert(_pos == 12);
                //tableDictionary = (offsetTable.TableCount);

                if (_offsetTable.Version == OTTO)
                    _fontTechnology = FontTechnology.PostscriptOutlines;
                else
                    _fontTechnology = FontTechnology.TrueTypeOutlines;

                for (int idx = 0; idx < _offsetTable.TableCount; idx++)
                {
                    TableDirectoryEntry entry = TableDirectoryEntry.ReadFrom(this);
                    TableDictionary.Add(entry.Tag, entry);
#if VERBOSE
          Debug.WriteLine(String.Format("Font table: {0}", entry.Tag));
#endif
                }

                // PDFlib checks this, but it is not part of the OpenType spec anymore
                if (TableDictionary.ContainsKey("bhed"))
                    throw new NotSupportedException("Bitmap fonts are not supported.");

                // Read required tables
                if (Seek(CMapTable.Tag) != -1)
                    cmap = new CMapTable(this);

                if (Seek(ControlValueTable.Tag) != -1)
                    cvt = new ControlValueTable(this);

                if (Seek(FontProgram.Tag) != -1)
                    fpgm = new FontProgram(this);

                if (Seek(MaximumProfileTable.Tag) != -1)
                    maxp = new MaximumProfileTable(this);

                if (Seek(NameTable.Tag) != -1)
                    name = new NameTable(this);

                if (Seek(FontHeaderTable.Tag) != -1)
                    head = new FontHeaderTable(this);

                if (Seek(HorizontalHeaderTable.Tag) != -1)
                    hhea = new HorizontalHeaderTable(this);

                if (Seek(HorizontalMetricsTable.Tag) != -1)
                    hmtx = new HorizontalMetricsTable(this);

                if (Seek(OS2Table.Tag) != -1)
                    os2 = new OS2Table(this);

                if (Seek(PostScriptTable.Tag) != -1)
                    post = new PostScriptTable(this);

                if (Seek(GlyphDataTable.Tag) != -1)
                    glyf = new GlyphDataTable(this);

                if (Seek(IndexToLocationTable.Tag) != -1)
                    loca = new IndexToLocationTable(this);

                // Optional CFF outlines (an "OTTO" font has no glyf/loca at all - see Type2CharstringInterpreter).
                if (TableDictionary.ContainsKey(TableTagNames.Cff))
                    cff = new CffTable(this);

                if (Seek(GlyphSubstitutionTable.Tag) != -1)
                    gsub = new GlyphSubstitutionTable(this);

                if (Seek(ControlValueProgram.Tag) != -1)
                    prep = new ControlValueProgram(this);

                // Optional color-glyph tables (COLR/CPAL). Absent on ordinary fonts.
                if (TableDictionary.ContainsKey(TableTagNames.Cpal))
                    cpal = new CpalTable(this);

                if (TableDictionary.ContainsKey(TableTagNames.Colr))
                    colr = new ColrTable(this);

                // Optional bitmap colour glyphs (CBDT/CBLC, sbix): one picture per glyph and size.
                bitmap = BitmapGlyphSource.TryCreate(this, maxp?.numGlyphs ?? 0);

                // Optional glyph-definition/positioning tables (GDEF/GPOS). Absent on many fonts -
                // no kerning/mark-attachment/mark-filtering data at all in that case.
                if (TableDictionary.ContainsKey(TableTagNames.GDEF))
                    gdef = new GlyphDefinitionTable(this);

                if (TableDictionary.ContainsKey(TableTagNames.GPOS))
                    gpos = new GlyphPositioningTable(this);

                // Optional MATH table (mathematical typesetting). Absent on almost all fonts - only
                // dedicated math fonts (e.g. STIX Two Math, Latin Modern Math) carry one.
                if (TableDictionary.ContainsKey(TableTagNames.Math))
                    math = new GlyphMathTable(this);

                // Optional vertical-writing-mode tables. Absent on purely-horizontal fonts. Unlike
                // COLR/CPAL above (which self-position from their own DirectoryEntry.Offset), these read
                // sequentially from the font reader's current position - like hhea/hmtx above - so each
                // needs its own Seek first. vhea must be read before vmtx (VerticalMetricsTable.Read
                // reads vhea.numOfLongVerMetrics).
                if (Seek(VerticalHeaderTable.Tag) != -1)
                    vhea = new VerticalHeaderTable(this);

                if (vhea != null && Seek(VerticalMetricsTable.Tag) != -1)
                    vmtx = new VerticalMetricsTable(this);

                if (Seek(VerticalOriginTable.Tag) != -1)
                    vorg = new VerticalOriginTable(this);
            }
            catch (Exception)
            {
                GetType();
                throw;
            }
        }

        /// <summary>
        /// Creates a new font image that is a subset of this font image containing only the specified glyphs.
        /// </summary>
        public OpenTypeFontface CreateFontSubSet(Dictionary<int, object> glyphs, bool cidFont, Variations.VariationCoordinates? variation = null)
        {
            // Create new font image
            OpenTypeFontface fontData = new OpenTypeFontface(this);

            // Create new loca and glyf table
            IndexToLocationTable locaNew = new IndexToLocationTable();
            locaNew.ShortIndex = loca.ShortIndex;
            GlyphDataTable glyfNew = new GlyphDataTable();

            // Add all required tables
            //fontData.AddTable(os2);
            if (!cidFont)
                fontData.AddTable(cmap);
            // The hinting programs are written for the default design: the glyphs of an instance carry no instructions, and
            // without them the programs have nothing to run on.
            if (cvt != null && variation is null)
                fontData.AddTable(cvt);
            if (fpgm != null && variation is null)
                fontData.AddTable(fpgm);
            fontData.AddTable(glyfNew);
            fontData.AddTable(head);
            fontData.AddTable(hhea);
            RawFontTable? hmtxInstance = null;
            if (variation is null)
            {
                fontData.AddTable(hmtx);
            }
            else
            {
                hmtxInstance = new RawFontTable(TableTagNames.HMtx);
                fontData.AddTable(hmtxInstance);
            }
            fontData.AddTable(locaNew);
            if (maxp != null)
                fontData.AddTable(maxp);
            //fontData.AddTable(name);
            if (prep != null && variation is null)
                fontData.AddTable(prep);

            // PDFium omits a shown CID from its text page when that CID's TrueType glyph has no contour.
            // COLR base glyphs are commonly empty because their visible outlines live in layer glyphs;
            // remember only the selected empty bases so the embedded, mode-3-only subset can give them
            // a valid contour. Embedding the real layer closure would not help: no Tj references those
            // layer CIDs, and their outlines are already emitted directly as PDF vector paths.
            HashSet<int>? syntheticSelectionGlyphs = null;
            if (colr != null || bitmap != null)
            {
                foreach (int glyphId in glyphs.Keys)
                {
                    // A bitmap colour glyph (CBDT/sbix) has no outline at all: same treatment as an empty COLR base.
                    if (((colr?.HasColorGlyph(glyphId) ?? false) || (bitmap?.HasGlyph(glyphId) ?? false)) && glyf.HasNoContours(glyphId))
                        (syntheticSelectionGlyphs ??= []).Add(glyphId);
                }
            }

            // Get closure of used glyphs.
            glyf.CompleteGlyphClosure(glyphs);

            // Create a sorted array of all used glyphs.
            int glyphCount = glyphs.Count;
            int[] glyphArray = new int[glyphCount];
            glyphs.Keys.CopyTo(glyphArray, 0);
            Array.Sort(glyphArray);

            // A glyph of an instance of a variable font is written afresh, with the location's deltas applied.
            Dictionary<int, (byte[] Data, int XMin)>? instanceGlyphs = null;
            if (variation is not null)
            {
                instanceGlyphs = new Dictionary<int, (byte[], int)>(glyphCount);
                foreach (int glyphId in glyphArray)
                {
                    if (syntheticSelectionGlyphs?.Contains(glyphId) == true)
                        continue;

                    var data = Fonts.OpenType.Variations.InstanceGlyphEncoder.Encode(this, glyphId, variation, out int xMin);
                    instanceGlyphs[glyphId] = (data, xMin);
                }
            }

            // Calculate new size of glyph table.
            int size = 0;
            for (int idx = 0; idx < glyphCount; idx++)
            {
                int glyphId = glyphArray[idx];
                size += syntheticSelectionGlyphs?.Contains(glyphId) == true
                    ? InvisibleSelectionGlyphSize
                    : instanceGlyphs is not null ? instanceGlyphs[glyphId].Data.Length : glyf.GetGlyphSize(glyphId);
            }
            glyfNew.DirectoryEntry.Length = size;

            // Create new loca table
            int numGlyphs = maxp.numGlyphs;
            locaNew.LocaTable = new int[numGlyphs + 1];

            // Create new glyf table
            glyfNew.GlyphTable = new byte[glyfNew.DirectoryEntry.PaddedLength];

            // Fill new glyf and loca table
            int glyphOffset = 0;
            int glyphIndex = 0;
            for (int idx = 0; idx < numGlyphs; idx++)
            {
                locaNew.LocaTable[idx] = glyphOffset;
                if (glyphIndex < glyphCount && glyphArray[glyphIndex] == idx)
                {
                    glyphIndex++;
                    if (syntheticSelectionGlyphs?.Contains(idx) == true)
                    {
                        WriteInvisibleSelectionGlyph(idx, glyfNew.GlyphTable, glyphOffset);
                        glyphOffset += InvisibleSelectionGlyphSize;
                    }
                    else
                    {
                        ReadOnlySpan<byte> glyphData = instanceGlyphs is not null ? instanceGlyphs[idx].Data : glyf.GetGlyphData(idx);
                        if (!glyphData.IsEmpty)
                        {
                            glyphData.CopyTo(glyfNew.GlyphTable.AsSpan(glyphOffset));
                            glyphOffset += glyphData.Length;
                        }
                    }
                }
            }
            locaNew.LocaTable[numGlyphs] = glyphOffset;

            if (hmtxInstance is not null)
                hmtxInstance.Data = BuildInstanceMetrics(instanceGlyphs!, variation!);

            // Compile font tables into byte array
            fontData.Compile();

            return fontData;
        }

        /// <summary>
        /// The <c>hmtx</c> table of an instance: the source's, with each glyph that was written afresh given its advance at the
        /// location and its left side bearing (a renderer places a glyph by its side bearing, so it has to be its new leftmost point).
        /// </summary>
        private byte[] BuildInstanceMetrics(Dictionary<int, (byte[] Data, int XMin)> written, Variations.VariationCoordinates variation)
        {
            var entry = TableDictionary[TableTagNames.HMtx];
            var bytes = new byte[entry.Length];
            Buffer.BlockCopy(FontSource.Bytes, entry.Offset, bytes, 0, entry.Length);

            int metricCount = hhea.numberOfHMetrics;
            foreach (var (glyph, (data, xMin)) in written)
            {
                if (data.Length == 0)
                    continue;

                if (glyph < metricCount)
                {
                    int advance = hmtx.Metrics[glyph].advanceWidth
                        + (int)Math.Round(Variations?.GetAdvanceDelta(this, glyph, variation) ?? 0);
                    advance = Math.Clamp(advance, 0, ushort.MaxValue);
                    bytes[glyph * 4] = (byte)(advance >> 8);
                    bytes[glyph * 4 + 1] = (byte)advance;
                    bytes[glyph * 4 + 2] = (byte)(xMin >> 8);
                    bytes[glyph * 4 + 3] = (byte)xMin;
                }
                else
                {
                    int at = metricCount * 4 + (glyph - metricCount) * 2;
                    if (at + 1 < bytes.Length)
                    {
                        bytes[at] = (byte)(xMin >> 8);
                        bytes[at + 1] = (byte)xMin;
                    }
                }
            }

            return bytes;
        }

        private const int InvisibleSelectionGlyphSize = 34;

        /// <summary>
        /// Writes the stand-in outline embedded for one empty COLR base glyph: a single on-curve
        /// rectangle spanning that glyph's own advance width and the font's ascent/descent. It exists
        /// only in a PDF's embedded color-font subset and is shown exclusively with text rendering
        /// mode 3, so it paints no ink - it exists purely so a viewer has glyph geometry to select.
        /// Sizing it to the real glyph box rather than to a token one is what lets a search hit
        /// highlight and a mouse drag actually cover the emoji underneath: PDFium derives a
        /// character's box from the contour extents, so a 1x1-unit contour yields a hit target
        /// thousands of times smaller than the visible artwork.
        /// </summary>
        private void WriteInvisibleSelectionGlyph(int glyphId, byte[] destination, int destinationOffset)
        {
            int unitsPerEm = head.unitsPerEm;

            int width = unitsPerEm;
            if (hmtx?.Metrics is { Length: > 0 } metrics)
            {
                // Past numberOfHMetrics every remaining glyph repeats the last advance - see
                // OpenTypeDescriptor.GlyphIndexToWidth, which clamps the same way.
                int metricIndex = Math.Clamp(glyphId, 0, metrics.Length - 1);
                if (metrics[metricIndex].advanceWidth > 0)
                    width = metrics[metricIndex].advanceWidth;
            }

            int top = hhea?.ascender ?? 0;
            int bottom = hhea?.descender ?? 0;
            if (top <= bottom)
            {
                // A font with unusable vertical metrics still needs a non-degenerate box.
                top = unitsPerEm;
                bottom = 0;
            }

            // Every coordinate lands in an sfnt FWord, so halving the int16 range here keeps both the
            // absolute values and the top-to-bottom delta written below inside a short.
            width = Math.Clamp(width, 1, short.MaxValue / 2);
            top = Math.Clamp(top, short.MinValue / 2, short.MaxValue / 2);
            bottom = Math.Clamp(bottom, short.MinValue / 2, short.MaxValue / 2);

            // Four on-curve points, counter-clockwise from the bottom-left. The flags deliberately set
            // only ON_CURVE_POINT, so each coordinate is a plain int16 delta from the previous point -
            // the short/same-value encodings cannot express a full-size box. The resulting length is
            // even, which keeps the glyph valid when the source font uses short loca offsets.
            int offset = destinationOffset;

            void WriteInt16(int value)
            {
                destination[offset++] = (byte)(value >> 8);
                destination[offset++] = (byte)value;
            }

            WriteInt16(1);      // numberOfContours
            WriteInt16(0);      // xMin
            WriteInt16(bottom); // yMin
            WriteInt16(width);  // xMax
            WriteInt16(top);    // yMax
            WriteInt16(3);      // endPtsOfContours[0] - four points
            WriteInt16(0);      // instructionLength

            for (int i = 0; i < 4; i++)
                destination[offset++] = 0x01; // ON_CURVE_POINT

            WriteInt16(0);      // x deltas, reaching (0, bottom)
            WriteInt16(width);  //                    (width, bottom)
            WriteInt16(0);      //                    (width, top)
            WriteInt16(-width); //                    (0, top)

            WriteInt16(bottom); // y deltas for the same four points
            WriteInt16(0);
            WriteInt16(top - bottom);
            WriteInt16(0);
        }

        /// <summary>
        /// Compiles the font to its binary representation.
        /// </summary>
        void Compile()
        {
            MemoryStream stream = new MemoryStream();
            OpenTypeFontWriter writer = new OpenTypeFontWriter(stream);

            int tableCount = TableDictionary.Count;
            int selector = _entrySelectors[tableCount];

            _offsetTable.Version = 0x00010000;
            _offsetTable.TableCount = tableCount;
            _offsetTable.SearchRange = (ushort)((1 << selector) * 16);
            _offsetTable.EntrySelector = (ushort)selector;
            _offsetTable.RangeShift = (ushort)((tableCount - (1 << selector)) * 16);
            _offsetTable.Write(writer);

            // Sort tables by tag name
            string[] tags = new string[tableCount];
            TableDictionary.Keys.CopyTo(tags, 0);
            Array.Sort(tags, StringComparer.Ordinal);

#if VERBOSE
      Debug.WriteLine("Start Compile");
#endif
            // Write tables in alphabetical order
            int tablePosition = 12 + 16 * tableCount;
            for (int idx = 0; idx < tableCount; idx++)
            {
                TableDirectoryEntry entry = TableDictionary[tags[idx]];
#if DEBUG
                if (entry.Tag == "glyf" || entry.Tag == "loca")
                    GetType();
#endif
                entry.FontTable.PrepareForCompilation();
                entry.Offset = tablePosition;
                writer.Position = tablePosition;
                entry.FontTable.Write(writer);
                int endPosition = writer.Position;
                tablePosition = endPosition;
                writer.Position = 12 + 16 * idx;
                entry.Write(writer);
#if VERBOSE
                Debug.WriteLine(String.Format("  Write Table '{0}', offset={1}, length={2}, checksum={3}, ", entry.Tag, entry.Offset, entry.Length, entry.CheckSum));
#endif
            }
#if VERBOSE
            Debug.WriteLine("End Compile");
#endif
            writer.Stream.Flush();
            int l = (int)writer.Stream.Length;
            FontSource = FontFileData.CreateCompiledFont(stream.ToArray());
        }
        // 2^entrySelector[n] <= n
        static readonly int[] _entrySelectors = { 0, 0, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4 };

        public int Position
        {
            get { return _pos; }
            set { _pos = value; }
        }
        int _pos;

        /// <summary>
        /// Guards every on-demand read against <see cref="Position"/>/<see cref="Seek(string)"/>/
        /// <see cref="ReadByte"/> and friends that happens AFTER this fontface's one-time, already-safe
        /// load-time parse (see <c>FontFactory.CacheFontSource</c>'s lock around construction/caching).
        /// <see cref="_pos"/> is a single mutable cursor shared by every consumer of this cached,
        /// process-wide instance - GSUB/GPOS's per-lookup lazy readers, COLRv1's on-demand paint-graph
        /// decode, and glyf outline decoding are all invoked lazily, well after load, from whichever
        /// thread happens to be shaping or painting a document that uses this font, so two such reads
        /// racing on <see cref="_pos"/> at once corrupts both (silently wrong values, not just a crash -
        /// confirmed via a stress run forcing high test parallelism). Lock scope must cover a whole
        /// logical read (seek + however many subsequent primitive reads it takes), not one primitive
        /// call at a time, since interleaving between a seek and its reads is exactly what corrupts a
        /// result without throwing. Reentrant (a plain object + `lock`), since a paint/lookup read can
        /// recurse into another read of the same fontface on the same thread.
        /// </summary>
        internal readonly object SyncRoot = new();

        /// <summary>
        /// The variation tables of a variable font, parsed once on first use, or <see langword="null"/> for a font that is not variable.
        /// They are read from the font's bytes and not through the shared cursor, so this needs no lock.
        /// </summary>
        internal Variations.FontVariations? Variations
        {
            get
            {
                if (!_variationsRead)
                {
                    _variations = Fonts.OpenType.Variations.FontVariations.TryCreate(this);
                    _variationsRead = true;
                }

                return _variations;
            }
        }

        private Variations.FontVariations? _variations;
        private volatile bool _variationsRead;

        /// <summary>
        /// The lazy-lookup-cache idiom used throughout GSUB/GPOS (<c>_someCache.GetOrAdd(index, someDelegate)</c>)
        /// is safe for the <see cref="ConcurrentDictionary{TKey,TValue}"/> itself, but not for <paramref name="factory"/> -
        /// <c>GetOrAdd</c> may invoke its factory more than once under contention, and here the factory
        /// reads through this fontface's single shared <see cref="Position"/> cursor (see
        /// <see cref="SyncRoot"/>). Checks the cache without locking first (the overwhelmingly common
        /// case, once every real lookup index has been read once - a lookup is parsed at most once per
        /// font, ever), and only takes <paramref name="face"/>'s lock on a miss.
        /// </summary>
        internal static TValue LockedGetOrAdd<TKey, TValue>(OpenTypeFontface face, ConcurrentDictionary<TKey, TValue> cache, TKey key, Func<TKey, TValue> factory) where TKey : notnull
        {
            if (cache.TryGetValue(key, out var existing))
                return existing;

            lock (face.SyncRoot)
            {
                return cache.GetOrAdd(key, factory);
            }
        }

        public int Seek(string tag)
        {
            if (TableDictionary.ContainsKey(tag))
            {
                _pos = TableDictionary[tag].Offset;
                return _pos;
            }
            return -1;
        }

        public int SeekOffset(int offset)
        {
            _pos += offset;
            return _pos;
        }

        /// <summary>
        /// Reads a System.Byte.
        /// </summary>
        public byte ReadByte()
        {
            return _fontSource.Bytes[_pos++];
        }

        /// <summary>
        /// Reads a System.Int16.
        /// </summary>
        public short ReadShort()
        {
            int pos = _pos;
            _pos += 2;
            return (short)((_fontSource.Bytes[pos] << 8) | (_fontSource.Bytes[pos + 1]));
        }

        /// <summary>
        /// Reads a System.UInt16.
        /// </summary>
        public ushort ReadUShort()
        {
            int pos = _pos;
            _pos += 2;
            return (ushort)((_fontSource.Bytes[pos] << 8) | (_fontSource.Bytes[pos + 1]));
        }

        /// <summary>
        /// Reads a System.Int32.
        /// </summary>
        public int ReadLong()
        {
            int pos = _pos;
            _pos += 4;
            return (_fontSource.Bytes[pos] << 24) | (_fontSource.Bytes[pos + 1] << 16) | (_fontSource.Bytes[pos + 2] << 8) | (_fontSource.Bytes[pos + 3]);
        }

        /// <summary>
        /// Reads a System.UInt32.
        /// </summary>
        public uint ReadULong()
        {
            int pos = _pos;
            _pos += 4;
            return (uint)((_fontSource.Bytes[pos] << 24) | (_fontSource.Bytes[pos + 1] << 16) | (_fontSource.Bytes[pos + 2] << 8) | (_fontSource.Bytes[pos + 3]));
        }

        /// <summary>
        /// Reads a System.Int32.
        /// </summary>
        public Fixed ReadFixed()
        {
            int pos = _pos;
            _pos += 4;
            return (_fontSource.Bytes[pos] << 24) | (_fontSource.Bytes[pos + 1] << 16) | (_fontSource.Bytes[pos + 2] << 8) | (_fontSource.Bytes[pos + 3]);
        }

        /// <summary>
        /// Reads a System.Int16.
        /// </summary>
        public short ReadFWord()
        {
            int pos = _pos;
            _pos += 2;
            return (short)((_fontSource.Bytes[pos] << 8) | (_fontSource.Bytes[pos + 1]));
        }

        /// <summary>
        /// Reads a System.UInt16.
        /// </summary>
        public ushort ReadUFWord()
        {
            int pos = _pos;
            _pos += 2;
            return (ushort)((_fontSource.Bytes[pos] << 8) | (_fontSource.Bytes[pos + 1]));
        }

        /// <summary>
        /// Reads a System.Int64.
        /// </summary>
        public long ReadLongDate()
        {
            int pos = _pos;
            _pos += 8;
            byte[] bytes = _fontSource.Bytes;
            return (((long)bytes[pos]) << 56) | (((long)bytes[pos + 1]) << 48) | (((long)bytes[pos + 2]) << 40) | (((long)bytes[pos + 3]) << 32) |
                   (((long)bytes[pos + 4]) << 24) | (((long)bytes[pos + 5]) << 16) | (((long)bytes[pos + 6]) << 8) | bytes[pos + 7];
        }

        /// <summary>
        /// Reads a System.String with the specified size.
        /// </summary>
        public string ReadString(int size)
        {
            char[] chars = new char[size];
            for (int idx = 0; idx < size; idx++)
                chars[idx] = (char)_fontSource.Bytes[_pos++];
            return new string(chars);
        }

        /// <summary>
        /// Reads a System.Byte[] with the specified size.
        /// </summary>
        public byte[] ReadBytes(int size)
        {
            byte[] bytes = new byte[size];
            for (int idx = 0; idx < size; idx++)
                bytes[idx] = _fontSource.Bytes[_pos++];
            return bytes;
        }

        /// <summary>
        /// Reads the specified buffer.
        /// </summary>
        public void Read(byte[] buffer)
        {
            Read(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Reads the specified buffer.
        /// </summary>
        public void Read(byte[] buffer, int offset, int length)
        {
            Buffer.BlockCopy(_fontSource.Bytes, _pos, buffer, offset, length);
            _pos += length;
        }

        /// <summary>
        /// Reads a System.Char[4] as System.String.
        /// </summary>
        public string ReadTag()
        {
            return ReadString(4);
        }

        /// <summary>
        /// Gets the DebuggerDisplayAttribute text.
        /// </summary>
        // ReSharper disable UnusedMember.Local
        internal string DebuggerDisplay
        // ReSharper restore UnusedMember.Local
        {
            get { return string.Format(CultureInfo.InvariantCulture, "OpenType fontfaces: {0}", _fullFaceName); }
        }

        /// <summary>
        /// Represents the font offset table.
        /// </summary>
        internal struct OffsetTable
        {
            /// <summary>
            /// 0x00010000 for Version 1.0.
            /// </summary>
            public uint Version;

            /// <summary>
            /// Number of tables.
            /// </summary>
            public int TableCount;

            /// <summary>
            /// (Maximum power of 2 ≤ numTables) x 16.
            /// </summary>
            public ushort SearchRange;

            /// <summary>
            /// Log2(maximum power of 2 ≤ numTables).
            /// </summary>
            public ushort EntrySelector;

            /// <summary>
            /// NumTables x 16-searchRange.
            /// </summary>
            public ushort RangeShift;

            /// <summary>
            /// Writes the offset table.
            /// </summary>
            public void Write(OpenTypeFontWriter writer)
            {
                writer.WriteUInt(Version);
                writer.WriteShort(TableCount);
                writer.WriteUShort(SearchRange);
                writer.WriteUShort(EntrySelector);
                writer.WriteUShort(RangeShift);
            }
        }
    }
}

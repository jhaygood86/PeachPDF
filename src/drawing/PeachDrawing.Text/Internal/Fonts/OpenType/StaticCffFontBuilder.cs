#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Writes the glyphs of a variable font with CFF2 outlines, at one location, as a static OpenType font with CFF outlines (an 'OTTO'
// file with a `CFF ` table): the form a PDF can embed. A PDF cannot embed a variable font, and CFF2 is the variable form of CFF, so
// the charstrings are not copied: each glyph is interpreted (Type2CharstringInterpreter, blends resolved, subroutines expanded, hints
// dropped) and its outline written afresh as a charstring of moveto, lineto and curveto operators over whole-number coordinates.
//
// The result is a CID-keyed CFF font (Adobe-Identity-0) whose charset is the identity, so a glyph index is also its CID and the
// subset keeps every glyph where it was, like the TrueType exporter does: a glyph that was not asked for is an empty charstring.
// CID-keyed needs no glyph names, and so no String INDEX that grows with the glyph count.
//
// https://adobe-type-tools.github.io/font-tech-notes/pdfs/5176.CFF.pdf
//
#endregion

using PeachDrawing.Text.Internal.Fonts.OpenType.Variations;
using PeachDrawing.Text.Outlines;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachDrawing.Text.Internal.Fonts.OpenType
{
    /// <summary>Builds a static OpenType/CFF font from the glyphs of a face with CFF2 outlines at one location.</summary>
    internal static class StaticCffFontBuilder
    {
        // A coordinate is kept to the range whose differences a 16-bit charstring operand holds, so a hostile font cannot overflow one.
        private const int MaxCoordinate = 16383;

        /// <summary>
        /// Writes the font: the glyphs in <paramref name="glyphs"/> (and glyph 0) with their outlines and advances at the descriptor's
        /// location, every other glyph empty, with the tables of <paramref name="face"/> a viewer needs beside the outlines.
        /// </summary>
        public static byte[] Build(OpenTypeFontface face, OpenTypeDescriptor descriptor, IEnumerable<int> glyphs, bool keepCharacterMap)
        {
            int glyphCount = face.maxp.numGlyphs;
            var wanted = new SortedSet<int>(glyphs) { 0 };

            var charStrings = new byte[glyphCount][];
            var advances = new int[glyphCount];
            var leftBearings = new int[glyphCount];
            int lastGlyph = 0;
            for (int glyph = 0; glyph < glyphCount; glyph++)
            {
                bool isWanted = wanted.Contains(glyph);
                advances[glyph] = AdvanceOf(descriptor, glyph);

                if (!isWanted)
                {
                    charStrings[glyph] = [14]; // endchar
                    continue;
                }

                lastGlyph = glyph;
                var outline = descriptor.TryGetGlyphOutline(glyph, out var drawn) ? drawn : null;
                charStrings[glyph] = EncodeGlyph(outline, advances[glyph], out leftBearings[glyph]);
            }

            var tables = new List<(string Tag, byte[] Data)>
            {
                (TableTagNames.Cff, BuildCff(charStrings)),
                (TableTagNames.HMtx, BuildHmtx(advances, leftBearings, lastGlyph + 1)),
                (TableTagNames.MaxP, new byte[] { 0x00, 0x00, 0x50, 0x00, (byte)(glyphCount >> 8), (byte)glyphCount }), // version 0.5
            };

            AddCopy(tables, face, TableTagNames.Head, table =>
            {
                if (table.Length >= 12)
                    Array.Clear(table, 8, 4); // checkSumAdjustment, set when the file is written

                return table;
            });
            AddCopy(tables, face, TableTagNames.HHea, table =>
            {
                if (table.Length >= 36)
                {
                    table[34] = (byte)((lastGlyph + 1) >> 8);
                    table[35] = (byte)(lastGlyph + 1);
                }

                return table;
            });
            AddCopy(tables, face, TableTagNames.OS2, table => table);
            AddCopy(tables, face, TableTagNames.Name, table => table);
            AddCopy(tables, face, TableTagNames.Post, table =>
            {
                // Only the header: a CFF font's glyph names are in the CFF table, and the source's names are for its own glyphs.
                var header = table.Length >= 32 ? table[..32] : new byte[32];
                header[0] = 0x00; header[1] = 0x03; header[2] = 0x00; header[3] = 0x00;
                return header;
            });
            if (keepCharacterMap)
                AddCopy(tables, face, TableTagNames.CMap, table => table);

            return WriteSfnt(tables);
        }

        /// <summary>The advance of a glyph at the descriptor's location; a font whose <c>hmtx</c> cannot be read has none (0).</summary>
        private static int AdvanceOf(OpenTypeDescriptor descriptor, int glyph)
        {
            try
            {
                return Math.Clamp(descriptor.GlyphIndexToWidth(glyph), 0, ushort.MaxValue);
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or NullReferenceException or OverflowException)
            {
                return 0;
            }
        }

        private static void AddCopy(List<(string Tag, byte[] Data)> tables, OpenTypeFontface face, string tag, Func<byte[], byte[]> adjust)
        {
            if (!face.TableDictionary.TryGetValue(tag, out var entry))
                return;

            byte[] source = face.FontSource.Bytes;
            if (entry.Offset < 0 || entry.Length < 0 || (long)entry.Offset + entry.Length > source.Length)
                return;

            tables.Add((tag, adjust(source.AsSpan(entry.Offset, entry.Length).ToArray())));
        }

        // ---------------------------------------------------------------------------------------------------------------------------
        // Charstrings
        // ---------------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// A glyph as a Type 2 charstring: the advance as the width operand (the Private DICT's default and nominal widths are both 0),
        /// then for each contour a moveto and the lines and curves, all relative to the previous point, then endchar. A coordinate is
        /// rounded to a whole number before it is made relative, so the rounding does not add up along a contour.
        /// </summary>
        private static byte[] EncodeGlyph(GlyphOutline? outline, int advance, out int leftBearing)
        {
            var bytes = new List<byte>();
            bool widthPending = advance != 0;
            int x = 0, y = 0;
            int minX = int.MaxValue;

            void Operand(int value) => WriteOperand(bytes, value);

            void Operator(int op)
            {
                bytes.Add((byte)op);
                widthPending = false;
            }

            void Width()
            {
                // (An operand is a signed 16-bit number; hmtx, which has the whole advance, is what a viewer places glyphs by.)
                if (widthPending)
                    Operand(Math.Min(advance, short.MaxValue));
            }

            int Round(double value) => Math.Clamp(FontVariations.Round(double.IsNaN(value) ? 0 : value), -MaxCoordinate, MaxCoordinate);

            if (outline is not null)
            {
                foreach (var contour in outline.Contours)
                {
                    if (contour.Segments.Count == 0)
                        continue;

                    int startX = Round(contour.Start.X), startY = Round(contour.Start.Y);
                    Width();
                    Operand(startX - x);
                    Operand(startY - y);
                    Operator(21); // rmoveto
                    x = startX;
                    y = startY;
                    minX = Math.Min(minX, x);

                    foreach (var segment in contour.Segments)
                    {
                        if (segment.IsCubic)
                        {
                            int x1 = Round(segment.Control1.X), y1 = Round(segment.Control1.Y);
                            int x2 = Round(segment.Control2.X), y2 = Round(segment.Control2.Y);
                            int x3 = Round(segment.End.X), y3 = Round(segment.End.Y);
                            Operand(x1 - x); Operand(y1 - y);
                            Operand(x2 - x1); Operand(y2 - y1);
                            Operand(x3 - x2); Operand(y3 - y2);
                            Operator(8); // rrcurveto
                            minX = Math.Min(minX, Math.Min(x1, Math.Min(x2, x3)));
                            x = x3;
                            y = y3;
                        }
                        else
                        {
                            int x1 = Round(segment.End.X), y1 = Round(segment.End.Y);
                            Operand(x1 - x); Operand(y1 - y);
                            Operator(5); // rlineto
                            minX = Math.Min(minX, x1);
                            x = x1;
                            y = y1;
                        }
                    }
                }
            }

            Width();
            bytes.Add(14); // endchar
            leftBearing = minX == int.MaxValue ? 0 : minX;
            return bytes.ToArray();
        }

        /// <summary>Writes a charstring operand: one byte for -107 to 107, two for -1131 to 1131, else a 16-bit integer (its value is within it).</summary>
        private static void WriteOperand(List<byte> bytes, int value)
        {
            if (value >= -107 && value <= 107)
            {
                bytes.Add((byte)(value + 139));
            }
            else if (value >= 108 && value <= 1131)
            {
                value -= 108;
                bytes.Add((byte)(247 + (value >> 8)));
                bytes.Add((byte)value);
            }
            else if (value >= -1131 && value <= -108)
            {
                value = -value - 108;
                bytes.Add((byte)(251 + (value >> 8)));
                bytes.Add((byte)value);
            }
            else
            {
                bytes.Add(28);
                bytes.Add((byte)(value >> 8));
                bytes.Add((byte)value);
            }
        }

        // ---------------------------------------------------------------------------------------------------------------------------
        // The CFF table
        // ---------------------------------------------------------------------------------------------------------------------------

        private const int SidAdobe = 391; // the first string after the 391 standard ones
        private const int SidIdentity = 392;

        private static byte[] BuildCff(byte[][] charStrings)
        {
            int glyphCount = charStrings.Length;

            var header = new byte[] { 1, 0, 4, 4 }; // major, minor, hdrSize, offSize
            byte[] nameIndex = Index([System.Text.Encoding.ASCII.GetBytes("PeachDrawingInstance")]);
            byte[] stringIndex = Index([System.Text.Encoding.ASCII.GetBytes("Adobe"), System.Text.Encoding.ASCII.GetBytes("Identity")]);
            byte[] globalSubrs = [0, 0];
            byte[] charStringsIndex = Index(charStrings);
            byte[] charset = BuildCharset(glyphCount);
            byte[] fdSelect = BuildFdSelect(glyphCount);
            byte[] privateDict = [.. Integer(0), 20, .. Integer(0), 21]; // defaultWidthX, nominalWidthX

            // The Top DICT and the Font DICT use operands of a fixed size for what is an offset, so their sizes do not depend on the offsets
            // and the layout can be worked out before they are written.
            int topDictLength = TopDict(glyphCount, 0, 0, 0, 0).Length;
            byte[] fontDict = [.. FixedInteger(privateDict.Length), .. FixedInteger(0), 18];
            byte[] fdArray = Index([fontDict]);

            int topDictIndexLength = Index([new byte[topDictLength]]).Length;
            int at = header.Length + nameIndex.Length + topDictIndexLength + stringIndex.Length + globalSubrs.Length;
            int charsetAt = at;
            int fdSelectAt = charsetAt + charset.Length;
            int charStringsAt = fdSelectAt + fdSelect.Length;
            int fdArrayAt = charStringsAt + charStringsIndex.Length;
            int privateAt = fdArrayAt + fdArray.Length;

            byte[] topDictIndex = Index([TopDict(glyphCount, charsetAt, fdSelectAt, charStringsAt, fdArrayAt)]);
            fontDict = [.. FixedInteger(privateDict.Length), .. FixedInteger(privateAt), 18];
            fdArray = Index([fontDict]);

            return
            [
                .. header, .. nameIndex, .. topDictIndex, .. stringIndex, .. globalSubrs,
                .. charset, .. fdSelect, .. charStringsIndex, .. fdArray, .. privateDict,
            ];
        }

        /// <summary>The Top DICT of a CID-keyed font: ROS, CIDCount, and where the charset, FDSelect, CharStrings and FDArray are.</summary>
        private static byte[] TopDict(int glyphCount, int charset, int fdSelect, int charStrings, int fdArray) =>
        [
            .. Integer(SidAdobe), .. Integer(SidIdentity), .. Integer(0), 12, 30,      // ROS
            .. Integer(glyphCount), 12, 34,                                            // CIDCount
            .. FixedInteger(charset), 15,                                              // charset
            .. FixedInteger(fdSelect), 12, 37,                                         // FDSelect
            .. FixedInteger(charStrings), 17,                                          // CharStrings
            .. FixedInteger(fdArray), 12, 36,                                          // FDArray
        ];

        /// <summary>The identity charset: format 2, one range from glyph 1 (glyph 0 is implicit); with no glyph but .notdef, format 0 with no entries.</summary>
        private static byte[] BuildCharset(int glyphCount) =>
            glyphCount < 2 ? [0] : [2, 0, 1, (byte)((glyphCount - 2) >> 8), (byte)(glyphCount - 2)];

        /// <summary>An FDSelect of format 3 that gives every glyph Font DICT 0.</summary>
        private static byte[] BuildFdSelect(int glyphCount) =>
            [3, 0, 1, 0, 0, 0, (byte)(glyphCount >> 8), (byte)glyphCount];

        /// <summary>A DICT integer in its shortest form.</summary>
        private static byte[] Integer(int value)
        {
            if (value >= -107 && value <= 107)
                return [(byte)(value + 139)];
            if (value >= 108 && value <= 1131)
                return [(byte)(247 + ((value - 108) >> 8)), (byte)(value - 108)];
            if (value >= -1131 && value <= -108)
                return [(byte)(251 + ((-value - 108) >> 8)), (byte)(-value - 108)];
            if (value >= short.MinValue && value <= short.MaxValue)
                return [28, (byte)(value >> 8), (byte)value];
            return FixedInteger(value);
        }

        /// <summary>A DICT integer that always takes five bytes, so that what it is does not change how long the DICT is.</summary>
        private static byte[] FixedInteger(int value) => [29, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

        /// <summary>A CFF INDEX of the given objects, with the smallest offset size that holds their total length.</summary>
        private static byte[] Index(IReadOnlyList<byte[]> objects)
        {
            if (objects.Count == 0)
                return [0, 0];

            int total = 1;
            foreach (var item in objects)
                total += item.Length;

            int offSize = total <= 0xFF ? 1 : total <= 0xFFFF ? 2 : total <= 0xFFFFFF ? 3 : 4;
            var bytes = new List<byte>(2 + 1 + (objects.Count + 1) * offSize + total)
            {
                (byte)(objects.Count >> 8), (byte)objects.Count, (byte)offSize,
            };

            void Offset(int value)
            {
                for (int shift = (offSize - 1) * 8; shift >= 0; shift -= 8)
                    bytes.Add((byte)(value >> shift));
            }

            int offset = 1;
            Offset(offset);
            foreach (var item in objects)
            {
                offset += item.Length;
                Offset(offset);
            }

            foreach (var item in objects)
                bytes.AddRange(item);

            return bytes.ToArray();
        }

        // ---------------------------------------------------------------------------------------------------------------------------
        // hmtx and the file
        // ---------------------------------------------------------------------------------------------------------------------------

        /// <summary>An <c>hmtx</c> with a full metric (advance and left side bearing) for every glyph up to <paramref name="metricCount"/>.</summary>
        private static byte[] BuildHmtx(int[] advances, int[] leftBearings, int metricCount)
        {
            var table = new byte[metricCount * 4];
            for (int glyph = 0; glyph < metricCount; glyph++)
            {
                table[glyph * 4] = (byte)(advances[glyph] >> 8);
                table[glyph * 4 + 1] = (byte)advances[glyph];
                table[glyph * 4 + 2] = (byte)(leftBearings[glyph] >> 8);
                table[glyph * 4 + 3] = (byte)leftBearings[glyph];
            }

            return table;
        }

        /// <summary>Assembles the tables into an OpenType file with PostScript outlines: header, directory sorted by tag, tables padded to four bytes.</summary>
        private static byte[] WriteSfnt(List<(string Tag, byte[] Data)> tables)
        {
            tables.Sort((a, b) => string.CompareOrdinal(a.Tag, b.Tag));

            int count = tables.Count;
            int selector = 0;
            while ((1 << (selector + 1)) <= count)
                selector++;

            int directoryEnd = 12 + 16 * count;
            int size = directoryEnd + tables.Sum(t => (t.Data.Length + 3) & ~3);
            var file = new byte[size];

            file[0] = (byte)'O'; file[1] = (byte)'T'; file[2] = (byte)'T'; file[3] = (byte)'O';
            WriteU16(file, 4, count);
            WriteU16(file, 6, (1 << selector) * 16);
            WriteU16(file, 8, selector);
            WriteU16(file, 10, count * 16 - (1 << selector) * 16);

            int offset = directoryEnd;
            int headOffset = -1;
            for (int i = 0; i < count; i++)
            {
                var (tag, data) = tables[i];
                if (tag == TableTagNames.Head)
                    headOffset = offset;

                Array.Copy(data, 0, file, offset, data.Length);

                int entry = 12 + i * 16;
                for (int c = 0; c < 4; c++)
                    file[entry + c] = (byte)tag[c];
                WriteU32(file, entry + 4, Checksum(file, offset, (data.Length + 3) & ~3));
                WriteU32(file, entry + 8, (uint)offset);
                WriteU32(file, entry + 12, (uint)data.Length);

                offset += (data.Length + 3) & ~3;
            }

            // head.checkSumAdjustment makes the whole file's checksum 0xB1B0AFBA.
            if (headOffset >= 0 && tables.First(t => t.Tag == TableTagNames.Head).Data.Length >= 12)
                WriteU32(file, headOffset + 8, 0xB1B0AFBA - Checksum(file, 0, file.Length));

            return file;
        }

        private static uint Checksum(byte[] data, int offset, int length)
        {
            uint sum = 0;
            for (int i = 0; i < length; i += 4)
            {
                sum += ((uint)data[offset + i] << 24) | ((uint)data[offset + i + 1] << 16) | ((uint)data[offset + i + 2] << 8) | data[offset + i + 3];
            }

            return sum;
        }

        private static void WriteU16(byte[] data, int at, int value)
        {
            data[at] = (byte)(value >> 8);
            data[at + 1] = (byte)value;
        }

        private static void WriteU32(byte[] data, int at, uint value)
        {
            data[at] = (byte)(value >> 24);
            data[at + 1] = (byte)(value >> 16);
            data[at + 2] = (byte)(value >> 8);
            data[at + 3] = (byte)value;
        }
    }
}

using System;
using System.Text;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Fonts with bitmap colour glyphs, made by splicing hand-built <c>CBLC</c>/<c>CBDT</c> or <c>sbix</c> tables into a real bundled font
    /// (the same technique the vertical-metrics tests use): the outlines, cmap and metrics are the font's own, and only the pictures are ours.
    /// </summary>
    internal static class BitmapGlyphFontFixture
    {
        /// <summary>One picture of one glyph in one strike.</summary>
        internal sealed record Picture(int Ppem, int GlyphId, byte[] Png, int Width, int Height, int BearingX, int BearingY, int ImageFormat = 17, int IndexFormat = 0);

        /// <summary>The glyph id of <paramref name="ch"/> in <paramref name="fontBytes"/>.</summary>
        internal static int GlyphId(byte[] fontBytes, char ch) => TypefaceFixtures.FromBytes(fontBytes).GlyphOf(ch);

        /// <summary>The number of glyphs in <paramref name="fontBytes"/>.</summary>
        internal static int GlyphCount(byte[] fontBytes) => SyntheticFontTables.GlyphCount(fontBytes);

        /// <summary>The base font with <c>CBLC</c> and <c>CBDT</c> tables holding <paramref name="pictures"/> (index format 1, image formats 17/18, or 19 with index format 2).</summary>
        internal static byte[] WithCbdt(byte[] fontBytes, params Picture[] pictures)
        {
            var strikes = pictures.GroupBy(p => p.Ppem).OrderBy(g => g.Key).ToList();

            // CBDT: header 3.0, then the glyph records; remember where each starts.
            var cbdt = new SfntByteBuilder();
            cbdt.U16(3);
            cbdt.U16(0);
            var offsets = new Dictionary<Picture, (int Start, int End)>();
            foreach (var picture in pictures)
            {
                var start = cbdt.Position;
                switch (picture.ImageFormat)
                {
                    case 17:
                        cbdt.Byte((byte)picture.Height);
                        cbdt.Byte((byte)picture.Width);
                        cbdt.Byte((byte)picture.BearingX);
                        cbdt.Byte((byte)picture.BearingY);
                        cbdt.Byte((byte)picture.Width);
                        break;
                    case 18:
                        BigMetrics(cbdt, picture);
                        break;
                }

                cbdt.U32((uint)picture.Png.Length);
                foreach (var b in picture.Png)
                    cbdt.Byte(b);

                offsets[picture] = (start, cbdt.Position);
            }

            // CBLC: header, one 48-byte BitmapSize per strike, then per strike an IndexSubTableArray (one entry per glyph) and the subtables.
            var cblc = new SfntByteBuilder();
            cblc.U16(3);
            cblc.U16(0);
            cblc.U32((uint)strikes.Count);
            var sizeRecords = cblc.Position;
            for (var i = 0; i < strikes.Count * 48; i++)
                cblc.Byte(0);

            for (var s = 0; s < strikes.Count; s++)
            {
                var group = strikes[s].ToList();
                var arrayStart = cblc.Position;

                // Array entries first (each points at its subtable, which follows the whole array).
                var subtableOffsets = new List<int>();
                var subtables = new SfntByteBuilder();
                foreach (var picture in group)
                {
                    subtableOffsets.Add(subtables.Position);
                    var (start, end) = offsets[picture];
                    var indexFormat = picture.IndexFormat != 0 ? picture.IndexFormat : picture.ImageFormat == 19 ? 2 : 1;
                    subtables.U16(indexFormat);
                    subtables.U16((ushort)picture.ImageFormat);
                    subtables.U32((uint)start);
                    switch (indexFormat)
                    {
                        case 1: // 32-bit offsets
                            subtables.U32(0);
                            subtables.U32((uint)(end - start));
                            break;
                        case 3: // 16-bit offsets
                            subtables.U16(0);
                            subtables.U16(end - start);
                            break;
                        case 4: // sparse: (glyph id, offset) pairs plus a sentinel
                            subtables.U32(1);
                            subtables.U16(picture.GlyphId);
                            subtables.U16(0);
                            subtables.U16(0);
                            subtables.U16(end - start);
                            break;
                        case 2: // fixed-size images with shared big metrics
                            subtables.U32((uint)(end - start));
                            BigMetrics(subtables, picture);
                            break;
                        case 5: // sparse, fixed-size, shared big metrics
                            subtables.U32((uint)(end - start));
                            BigMetrics(subtables, picture);
                            subtables.U32(1);
                            subtables.U16(picture.GlyphId);
                            break;
                    }
                }

                foreach (var (picture, index) in group.Select((p, i) => (p, i)))
                {
                    cblc.U16(picture.GlyphId);
                    cblc.U16(picture.GlyphId);
                    cblc.U32((uint)(group.Count * 8 + subtableOffsets[index]));
                }

                var subtableBytes = subtables.ToArray();
                foreach (var b in subtableBytes)
                    cblc.Byte(b);

                var indexTablesSize = cblc.Position - arrayStart;
                var first = group.Min(p => p.GlyphId);
                var last = group.Max(p => p.GlyphId);

                // Patch the strike's BitmapSize record.
                var record = sizeRecords + s * 48;
                cblc.PatchU32(record, (uint)arrayStart);
                cblc.PatchU32(record + 4, (uint)indexTablesSize);
                cblc.PatchU32(record + 8, (uint)group.Count);
                cblc.PatchU16(record + 40, first);
                cblc.PatchU16(record + 42, last);
                cblc.PatchU16(record + 44, (strikes[s].Key << 8) | strikes[s].Key);
            }

            var withCblc = SyntheticFontTables.InsertTableDirectoryEntry(fontBytes, "CBLC", cblc.ToArray());
            return SyntheticFontTables.InsertTableDirectoryEntry(withCblc, "CBDT", cbdt.ToArray());
        }

        private static void BigMetrics(SfntByteBuilder b, Picture picture)
        {
            b.Byte((byte)picture.Height);
            b.Byte((byte)picture.Width);
            b.Byte((byte)picture.BearingX);
            b.Byte((byte)picture.BearingY);
            b.Byte((byte)picture.Width); // horiAdvance
            b.Byte(0); // vertBearingX
            b.Byte(0); // vertBearingY
            b.Byte((byte)picture.Height); // vertAdvance
        }

        /// <summary>The base font with an <c>sbix</c> table. A picture's <c>BearingY</c> is the lower-left origin offset Y (sbix places the bottom-left corner).</summary>
        internal static byte[] WithSbix(byte[] fontBytes, IReadOnlyList<Picture> pictures, IReadOnlyDictionary<(int Ppem, int GlyphId), int>? duplicates = null)
        {
            var numGlyphs = GlyphCount(fontBytes);
            var strikes = pictures.GroupBy(p => p.Ppem).OrderBy(g => g.Key).ToList();

            var table = new SfntByteBuilder();
            table.U16(1); // version
            table.U16(1); // flags
            table.U32((uint)strikes.Count);
            var offsetSlots = table.Position;
            for (var i = 0; i < strikes.Count; i++)
                table.U32(0);

            for (var s = 0; s < strikes.Count; s++)
            {
                var strikeStart = table.Position;
                table.PatchU32(offsetSlots + s * 4, (uint)strikeStart);
                table.U16(strikes[s].Key); // ppem
                table.U16(72); // ppi

                var slots = table.Position;
                for (var i = 0; i <= numGlyphs; i++)
                    table.U32(0);

                var records = new Dictionary<int, byte[]>();
                foreach (var picture in strikes[s])
                {
                    var record = new SfntByteBuilder();
                    record.S16(picture.BearingX);
                    record.S16(picture.BearingY);
                    record.Tag("png ");
                    foreach (var b in picture.Png)
                        record.Byte(b);
                    records[picture.GlyphId] = record.ToArray();
                }

                if (duplicates is not null)
                {
                    foreach (var ((ppem, glyph), target) in duplicates)
                    {
                        if (ppem != strikes[s].Key)
                            continue;

                        var record = new SfntByteBuilder();
                        record.S16(0);
                        record.S16(0);
                        record.Tag("dupe");
                        record.U16(target);
                        records[glyph] = record.ToArray();
                    }
                }

                var offsets = new uint[numGlyphs + 1];
                var running = (uint)(table.Position - strikeStart);
                for (var g = 0; g <= numGlyphs; g++)
                {
                    offsets[g] = running;
                    if (g < numGlyphs && records.TryGetValue(g, out var bytes))
                    {
                        foreach (var b in bytes)
                            table.Byte(b);
                        running += (uint)bytes.Length;
                    }
                }

                for (var g = 0; g <= numGlyphs; g++)
                    table.PatchU32(slots + g * 4, offsets[g]);
            }

            return SyntheticFontTables.InsertTableDirectoryEntry(fontBytes, "sbix", table.ToArray());
        }
    }
}

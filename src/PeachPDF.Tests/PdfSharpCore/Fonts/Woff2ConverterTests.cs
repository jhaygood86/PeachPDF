using System.IO.Compression;
using PeachPDF.Fonts;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    public class Woff2ConverterTests
    {
        // ---------------------------------------------------------------
        // Minimal WOFF2 builder used to synthesize test fixtures in-memory
        // (this fork has no bundled WOFF2 sample files, so tests construct
        // spec-conformant byte streams directly, compressing table data with
        // the real System.IO.Compression.BrotliStream so the decompression
        // path in Woff2Converter.Convert is genuinely exercised).
        // ---------------------------------------------------------------

        private const uint GlyfTag = 0x676C7966; // 'glyf'
        private const uint LocaTag = 0x6C6F6361; // 'loca'
        private const uint MaxpTag = 0x6D617870; // 'maxp'
        private const uint HeadTag = 0x68656164; // 'head'

        // Index of each tag within Woff2Converter's private KnownTags table (spec table 2).
        private const int HeadKnownIndex = 1;
        private const int MaxpKnownIndex = 4;
        private const int GlyfKnownIndex = 10;
        private const int LocaKnownIndex = 11;

        private sealed class TableSpec
        {
            public required int KnownTagIndex;
            public required int TransformVersion;
            public required byte[] Content;
        }

        private static void WriteUInt16BE(List<byte> buf, int value)
        {
            buf.Add((byte)(value >> 8));
            buf.Add((byte)value);
        }

        private static void WriteUInt32BE(List<byte> buf, uint value)
        {
            buf.Add((byte)(value >> 24));
            buf.Add((byte)(value >> 16));
            buf.Add((byte)(value >> 8));
            buf.Add((byte)value);
        }

        private static void WriteInt16BE(List<byte> buf, short value) => WriteUInt16BE(buf, (ushort)value);

        private static byte[] WriteBase128(uint value)
        {
            var chunks = new List<byte>();
            do
            {
                chunks.Add((byte)(value & 0x7F));
                value >>= 7;
            } while (value > 0);
            chunks.Reverse();
            var result = new byte[chunks.Count];
            for (int i = 0; i < chunks.Count; i++)
            {
                result[i] = chunks[i];
                if (i < chunks.Count - 1) result[i] |= 0x80;
            }
            return result;
        }

        private static byte[] Brotli(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var br = new BrotliStream(ms, CompressionMode.Compress, leaveOpen: true))
                br.Write(data, 0, data.Length);
            return ms.ToArray();
        }

        private static byte[] BuildWoff2(params TableSpec[] tables)
        {
            var concatenated = new List<byte>();
            foreach (var t in tables)
                concatenated.AddRange(t.Content);
            byte[] compressed = Brotli(concatenated.ToArray());

            var buf = new List<byte>();
            WriteUInt32BE(buf, 0x774F4632); // signature 'wOF2'
            WriteUInt32BE(buf, 0x00010000); // flavor
            WriteUInt32BE(buf, 0);          // length (unused by Convert)
            WriteUInt16BE(buf, tables.Length);
            WriteUInt16BE(buf, 0);          // reserved
            WriteUInt32BE(buf, 0);          // totalSfntSize (unused)
            WriteUInt32BE(buf, (uint)compressed.Length); // totalCompressedSize
            WriteUInt16BE(buf, 1); WriteUInt16BE(buf, 0); // major/minor version (unused)
            WriteUInt32BE(buf, 0); WriteUInt32BE(buf, 0); WriteUInt32BE(buf, 0); // meta*
            WriteUInt32BE(buf, 0); WriteUInt32BE(buf, 0); // priv*
            while (buf.Count < 48) buf.Add(0);

            foreach (var t in tables)
            {
                byte flags = (byte)(t.KnownTagIndex | (t.TransformVersion << 6));
                buf.Add(flags);
                buf.AddRange(WriteBase128((uint)t.Content.Length));
                bool isGlyfOrLoca = t.KnownTagIndex == GlyfKnownIndex || t.KnownTagIndex == LocaKnownIndex;
                bool transformed = isGlyfOrLoca ? t.TransformVersion == 0 : t.TransformVersion != 0;
                if (transformed)
                    buf.AddRange(WriteBase128((uint)t.Content.Length));
            }

            buf.AddRange(compressed);
            return buf.ToArray();
        }

        [Fact]
        public void IsWoff2_ValidSignature_ReturnsTrue()
        {
            Assert.True(Woff2Converter.IsWoff2([0x77, 0x4F, 0x46, 0x32, 0, 0, 0, 0]));
        }

        [Fact]
        public void IsWoff2_WrongSignature_ReturnsFalse()
        {
            Assert.False(Woff2Converter.IsWoff2([0x00, 0x01, 0x00, 0x00]));
        }

        [Fact]
        public void IsWoff2_TooShort_ReturnsFalse()
        {
            Assert.False(Woff2Converter.IsWoff2([0x77, 0x4F, 0x46]));
        }

        [Fact]
        public void Convert_FileShorterThanHeader_Throws()
        {
            var data = new byte[47];
            Assert.Throws<InvalidDataException>(() => Woff2Converter.Convert(data));
        }

        [Fact]
        public void Convert_TruncatedTableDirectory_Throws()
        {
            var header = new byte[48];
            header[12] = 0; header[13] = 1; // numTables = 1, but no directory bytes follow
            Assert.Throws<InvalidDataException>(() => Woff2Converter.Convert(header));
        }

        [Fact]
        public void Convert_ReservedTransformVersionForGlyf_Throws()
        {
            var glyf = new TableSpec { KnownTagIndex = GlyfKnownIndex, TransformVersion = 1, Content = [0, 0, 0, 0] };
            var data = BuildWoff2(glyf);
            Assert.Throws<InvalidDataException>(() => Woff2Converter.Convert(data));
        }

        [Fact]
        public void Convert_ReservedTransformVersionForNonGlyfTable_Throws()
        {
            var head = new TableSpec { KnownTagIndex = HeadKnownIndex, TransformVersion = 1, Content = [0, 0, 0, 0] };
            var data = BuildWoff2(head);
            Assert.Throws<InvalidDataException>(() => Woff2Converter.Convert(data));
        }

        [Fact]
        public void Convert_CompressedBlockBeyondEndOfFile_Throws()
        {
            var head = new TableSpec { KnownTagIndex = HeadKnownIndex, TransformVersion = 0, Content = [1, 2, 3, 4] };
            var data = BuildWoff2(head);
            // Corrupt totalCompressedSize (offset 20) to a value far larger than the file.
            data[20] = 0x7F; data[21] = 0xFF; data[22] = 0xFF; data[23] = 0xFF;
            Assert.Throws<InvalidDataException>(() => Woff2Converter.Convert(data));
        }

        [Fact]
        public void Convert_IdentityTables_RebuildsValidSfntWithOriginalTableBytes()
        {
            var head = new TableSpec { KnownTagIndex = HeadKnownIndex, TransformVersion = 0, Content = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE] };
            var maxp = new TableSpec { KnownTagIndex = MaxpKnownIndex, TransformVersion = 0, Content = [0x11, 0x22, 0x33] };
            var data = BuildWoff2(head, maxp);

            byte[] sfnt = Woff2Converter.Convert(data);

            int numTables = (sfnt[4] << 8) | sfnt[5];
            Assert.Equal(2, numTables);

            var foundTags = new List<uint>();
            for (int i = 0; i < numTables; i++)
            {
                int recordOffset = 12 + i * 16;
                uint tag = (uint)((sfnt[recordOffset] << 24) | (sfnt[recordOffset + 1] << 16) | (sfnt[recordOffset + 2] << 8) | sfnt[recordOffset + 3]);
                uint tableOffset = (uint)((sfnt[recordOffset + 8] << 24) | (sfnt[recordOffset + 9] << 16) | (sfnt[recordOffset + 10] << 8) | sfnt[recordOffset + 11]);
                uint length = (uint)((sfnt[recordOffset + 12] << 24) | (sfnt[recordOffset + 13] << 16) | (sfnt[recordOffset + 14] << 8) | sfnt[recordOffset + 15]);
                foundTags.Add(tag);

                byte[] expectedContent = tag == HeadTag ? head.Content : tag == MaxpTag ? maxp.Content : throw new Xunit.Sdk.XunitException("Unexpected tag in rebuilt sfnt");
                var actualContent = new byte[length];
                Array.Copy(sfnt, (int)tableOffset, actualContent, 0, (int)length);
                Assert.Equal(expectedContent, actualContent);
            }

            Assert.Contains(HeadTag, foundTags);
            Assert.Contains(MaxpTag, foundTags);
        }

        [Fact]
        public void Convert_TransformedGlyfAndLoca_ReconstructsSimpleGlyphAndShortLoca()
        {
            // Transformed glyf table containing a single simple glyph: 1 contour, 2 points,
            // no per-glyph bbox (forces the "compute bbox from coordinates" branch), and 0
            // instructions. Per the W3C WOFF2 reference decoder, flag stream has exactly one
            // byte per point (no repeat/run-length encoding), the on-curve bit is bit 7 CLEAR
            // (0 = on-curve), and the low 7 bits select one of 128 coordinate "triplet"
            // encodings.
            // Point 0: flag=23 (in the 20-83 triplet range), extra byte 0x44 -> dx=+5, dy=+5.
            // Point 1: flag=0 (in the 0-9 triplet range), extra byte 0x00 -> dx=0, dy=0.
            // bboxBitmap is padded to a 4-byte boundary (ceil(numGlyphs/32)*4), so for a single
            // glyph that's 4 bytes, not 1.
            byte[] transformedGlyf =
            [
                0x00, 0x00, // reserved
                0x00, 0x00, // optionFlags
                0x00, 0x01, // numGlyphs = 1
                0x00, 0x00, // indexFormat = 0 (short loca)
                0x00, 0x00, 0x00, 0x02, // nContourStreamSize = 2
                0x00, 0x00, 0x00, 0x01, // nPointsStreamSize = 1
                0x00, 0x00, 0x00, 0x02, // flagStreamSize = 2
                0x00, 0x00, 0x00, 0x03, // glyphStreamSize = 3
                0x00, 0x00, 0x00, 0x00, // compositeStreamSize = 0
                0x00, 0x00, 0x00, 0x04, // bboxStreamSize = 4 (4-byte-aligned bitmap, no bbox values)
                0x00, 0x00, 0x00, 0x00, // instructionStreamSize (unused on read)
                // nContourStream: nContours for glyph 0 = 1
                0x00, 0x01,
                // nPointsStream: totalPoints for the single contour = 2
                0x02,
                // flagStream: point0 flag=23 (on-curve), point1 flag=0 (on-curve)
                0x17, 0x00,
                // glyphStream: point0's extra byte (0x44), point1's extra byte (0x00), instrLen=0
                0x44, 0x00, 0x00,
                // bboxStream: 4-byte bitmap, all bits 0 (bbox not present for glyph 0)
                0x00, 0x00, 0x00, 0x00,
            ];
            var glyf = new TableSpec { KnownTagIndex = GlyfKnownIndex, TransformVersion = 0, Content = transformedGlyf };
            var loca = new TableSpec { KnownTagIndex = LocaKnownIndex, TransformVersion = 3, Content = [] };
            var maxp = new TableSpec { KnownTagIndex = MaxpKnownIndex, TransformVersion = 0, Content = [0, 0, 0, 0, 0, 1] };
            var head = new TableSpec { KnownTagIndex = HeadKnownIndex, TransformVersion = 0, Content = new byte[54] };
            var data = BuildWoff2(glyf, loca, maxp, head);

            byte[] sfnt = Woff2Converter.Convert(data);

            int numTables = (sfnt[4] << 8) | sfnt[5];
            byte[] glyfBytes = null!, locaBytes = null!;
            for (int i = 0; i < numTables; i++)
            {
                int recordOffset = 12 + i * 16;
                uint tag = (uint)((sfnt[recordOffset] << 24) | (sfnt[recordOffset + 1] << 16) | (sfnt[recordOffset + 2] << 8) | sfnt[recordOffset + 3]);
                uint tableOffset = (uint)((sfnt[recordOffset + 8] << 24) | (sfnt[recordOffset + 9] << 16) | (sfnt[recordOffset + 10] << 8) | sfnt[recordOffset + 11]);
                uint length = (uint)((sfnt[recordOffset + 12] << 24) | (sfnt[recordOffset + 13] << 16) | (sfnt[recordOffset + 14] << 8) | sfnt[recordOffset + 15]);
                var content = new byte[length];
                Array.Copy(sfnt, (int)tableOffset, content, 0, (int)length);
                if (tag == GlyfTag) glyfBytes = content;
                if (tag == LocaTag) locaBytes = content;
            }

            Assert.NotNull(glyfBytes);
            Assert.NotNull(locaBytes);

            byte[] expectedGlyf =
            [
                0x00, 0x01, // nContours = 1
                0x00, 0x05, // xMin = 5 (computed from coordinates, since bbox wasn't present)
                0x00, 0x05, // yMin = 5
                0x00, 0x05, // xMax = 5
                0x00, 0x05, // yMax = 5
                0x00, 0x01, // endPtsOfContours[0] = 1
                0x00, 0x00, // instructionLength = 0
                0x09, 0x01, // flags: repeat-encoded (flag=1, run=2 -> byte|0x08, count-1)
                0x00, 0x05, // x delta for point0 = 5
                0x00, 0x00, // x delta for point1 = 0
                0x00, 0x05, // y delta for point0 = 5
                0x00, 0x00, // y delta for point1 = 0
            ];
            Assert.Equal(expectedGlyf, glyfBytes);

            byte[] expectedLoca = [0x00, 0x00, 0x00, 0x0C]; // short loca: offsets 0 and 24 (/2 = 0, 12)
            Assert.Equal(expectedLoca, locaBytes);
        }
    }
}

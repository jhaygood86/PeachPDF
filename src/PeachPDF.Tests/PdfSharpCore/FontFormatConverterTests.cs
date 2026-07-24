using PeachPDF.Fonts;
using System.IO.Compression;
using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    public class FontFormatConverterTests
    {
        // -----------------------------------------------------------------------
        // Magic byte detection
        // -----------------------------------------------------------------------

        [Fact]
        public void IsWoff_WithWoffSignature_ReturnsTrue()
        {
            var data = new byte[] { 0x77, 0x4F, 0x46, 0x46, 0, 0, 0, 0 };
            Assert.True(WoffConverter.IsWoff(data));
        }

        [Fact]
        public void IsWoff_WithTtfBytes_ReturnsFalse()
        {
            var data = new byte[] { 0x00, 0x01, 0x00, 0x00, 0, 0, 0, 0 };
            Assert.False(WoffConverter.IsWoff(data));
        }

        [Fact]
        public void IsWoff2_WithWoff2Signature_ReturnsTrue()
        {
            var data = new byte[] { 0x77, 0x4F, 0x46, 0x32, 0, 0, 0, 0 };
            Assert.True(Woff2Converter.IsWoff2(data));
        }

        [Fact]
        public void IsWoff2_WithTtfBytes_ReturnsFalse()
        {
            var data = new byte[] { 0x00, 0x01, 0x00, 0x00, 0, 0, 0, 0 };
            Assert.False(Woff2Converter.IsWoff2(data));
        }

        [Fact]
        public void ToOpenType_WithTtfBytes_ReturnsSameBytes()
        {
            var ttf = new byte[] { 0x00, 0x01, 0x00, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
            var result = FontFormatConverter.ToOpenType(ttf);
            Assert.Same(ttf, result);
        }

        // -----------------------------------------------------------------------
        // WOFF round-trip using a real system TTF
        // -----------------------------------------------------------------------

        [Fact]
        public void WoffConvert_RoundTrip_ProducesValidOpenType()
        {
            byte[] ttfBytes = File.ReadAllBytes(BundledFonts.Ttf);

            // Wrap TTF as uncompressed WOFF (compLength == origLength = no compression)
            byte[] woffBytes = WrapTtfAsWoff(ttfBytes);

            // Verify IsWoff recognizes it
            Assert.True(WoffConverter.IsWoff(woffBytes));

            // Convert back to OpenType
            byte[] result = WoffConverter.Convert(woffBytes);

            // Result must start with a valid OpenType signature
            uint magic = ReadUInt32BE(result, 0);
            Assert.True(
                magic == 0x00010000 || magic == 0x4F54544F || magic == 0x74727565,
                $"Unexpected OpenType version: 0x{magic:X8}");

            // Must be parseable by TtfFontDescription
            using var ms = new MemoryStream(result);
            var desc = TtfFontDescription.LoadDescription(ms);
            Assert.NotEmpty(desc.FontFamilyInvariantCulture);
        }

        // -----------------------------------------------------------------------
        // CFF font detection: OTTO magic bytes
        // -----------------------------------------------------------------------

        [Fact]
        public void IsWoff_WithOttoBytes_ReturnsFalse()
        {
            // OTF/CFF has OTTO magic — not WOFF
            var data = new byte[] { 0x4F, 0x54, 0x54, 0x4F, 0, 0, 0, 0 };
            Assert.False(WoffConverter.IsWoff(data));
        }

        [Fact]
        public void ToOpenType_WithOtfBytes_ReturnsSameBytes()
        {
            byte[] otfBytes = File.ReadAllBytes(BundledFonts.Otf);
            byte[] result = FontFormatConverter.ToOpenType(otfBytes);

            // OTF passes through unchanged
            Assert.Same(otfBytes, result);

            // Verify OTTO magic preserved
            Assert.Equal(0x4F54544FU, ReadUInt32BE(result, 0));
        }

        [Fact]
        public void TtfFontDescription_CffFont_ParsesWithoutError()
        {
            // TtfFontDescription must handle CFF (OTTO) fonts
            var desc = TtfFontDescription.LoadDescription(BundledFonts.Otf);
            Assert.NotEmpty(desc.FontFamilyInvariantCulture);
        }

        // -----------------------------------------------------------------------
        // WOFF2 conversion using the bundled WOFF2 test font
        // -----------------------------------------------------------------------

        [Fact]
        public void Woff2Convert_BundledFont_ProducesValidOpenType()
        {
            byte[] woff2Bytes = File.ReadAllBytes(BundledFonts.Woff2);
            Assert.True(Woff2Converter.IsWoff2(woff2Bytes));

            byte[] result = Woff2Converter.Convert(woff2Bytes);

            uint magic = ReadUInt32BE(result, 0);
            Assert.True(
                magic == 0x00010000 || magic == 0x4F54544F || magic == 0x74727565,
                $"Unexpected OpenType magic after WOFF2 conversion: 0x{magic:X8}");

            using var ms = new MemoryStream(result);
            var desc = TtfFontDescription.LoadDescription(ms);
            Assert.NotEmpty(desc.FontFamilyInvariantCulture);
        }

        // -----------------------------------------------------------------------
        // WOFF with compressed tables
        // -----------------------------------------------------------------------

        [Fact]
        public void WoffConvert_CompressedTable_DecompressesCorrectly()
        {
            byte[] ttfBytes = File.ReadAllBytes(BundledFonts.Ttf);

            // Build a WOFF where the first table is zlib-compressed.
            byte[] woffBytes = WrapTtfAsWoffWithCompressedFirstTable(ttfBytes);

            Assert.True(WoffConverter.IsWoff(woffBytes));

            byte[] result = WoffConverter.Convert(woffBytes);

            // Result must be parseable as a valid OpenType font.
            using var ms = new MemoryStream(result);
            var desc = TtfFontDescription.LoadDescription(ms);
            Assert.NotEmpty(desc.FontFamilyInvariantCulture);
        }

        // -----------------------------------------------------------------------
        // FontFormatConverter passthrough for unknown bytes
        // -----------------------------------------------------------------------

        [Fact]
        public void ToOpenType_WithArbitraryNonFontBytes_ReturnsSameReference()
        {
            // Bytes that are not WOFF, not WOFF2 — should pass through unchanged.
            var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04 };
            var result = FontFormatConverter.ToOpenType(data);
            Assert.Same(data, result);
        }

        // -----------------------------------------------------------------------
        // Woff2Converter detection
        // -----------------------------------------------------------------------

        [Fact]
        public void IsWoff2_WithEmptyArray_ReturnsFalse()
        {
            Assert.False(Woff2Converter.IsWoff2([]));
        }

        [Fact]
        public void IsWoff2_WithThreeBytes_ReturnsFalse()
        {
            Assert.False(Woff2Converter.IsWoff2([0x77, 0x4F, 0x46]));
        }

        // -----------------------------------------------------------------------
        // WoffConverter edge cases
        // -----------------------------------------------------------------------

        [Fact]
        public void IsWoff_WithEmptyArray_ReturnsFalse()
        {
            Assert.False(WoffConverter.IsWoff([]));
        }

        [Fact]
        public void IsWoff_WithThreeBytes_ReturnsFalse()
        {
            Assert.False(WoffConverter.IsWoff([0x77, 0x4F, 0x46]));
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        // Create a WOFF file from raw OpenType bytes (no per-table compression).
        // Per spec, compLength == origLength means table data is stored verbatim.
        private static byte[] WrapTtfAsWoff(byte[] ttf)
        {
            // Parse TTF offset table
            uint sfVersion = ReadUInt32BE(ttf, 0);
            int numTables = ReadUInt16BE(ttf, 4);

            // Table directory entries: tag(4), checksum(4), offset(4), length(4)
            var tags = new uint[numTables];
            var checksums = new uint[numTables];
            var offsets = new uint[numTables];
            var lengths = new uint[numTables];

            for (int i = 0; i < numTables; i++)
            {
                int e = 12 + i * 16;
                tags[i] = ReadUInt32BE(ttf, e);
                checksums[i] = ReadUInt32BE(ttf, e + 4);
                offsets[i] = ReadUInt32BE(ttf, e + 8);
                lengths[i] = ReadUInt32BE(ttf, e + 12);
            }

            // WOFF header = 44 bytes, table directory = 20 bytes × n
            int woffDirStart = 44;
            int dataStart = woffDirStart + numTables * 20;

            // Compute WOFF table offsets (each table is a copy of the original, no compression)
            var woffOffsets = new uint[numTables];
            uint woffPos = (uint)dataStart;
            for (int i = 0; i < numTables; i++)
            {
                woffOffsets[i] = woffPos;
                woffPos += lengths[i];
                woffPos = (woffPos + 3u) & ~3u; // pad to 4 bytes
            }

            uint totalSize = woffPos;
            byte[] woff = new byte[checked((int)totalSize)];
            int p = 0;

            // WOFF header (44 bytes)
            WriteUInt32BE(woff, p, 0x774F4646); p += 4; // signature 'wOFF'
            WriteUInt32BE(woff, p, sfVersion); p += 4;   // flavor (original sfVersion)
            WriteUInt32BE(woff, p, totalSize); p += 4;   // length
            WriteUInt16BE(woff, p, (ushort)numTables); p += 2;
            WriteUInt16BE(woff, p, 0); p += 2;           // reserved
            WriteUInt32BE(woff, p, (uint)ttf.Length); p += 4; // totalSfntSize
            WriteUInt16BE(woff, p, 1); p += 2;           // majorVersion
            WriteUInt16BE(woff, p, 0); p += 2;           // minorVersion
            WriteUInt32BE(woff, p, 0); p += 4;           // metaOffset
            WriteUInt32BE(woff, p, 0); p += 4;           // metaLength
            WriteUInt32BE(woff, p, 0); p += 4;           // metaOrigLength
            WriteUInt32BE(woff, p, 0); p += 4;           // privOffset
            WriteUInt32BE(woff, p, 0); p += 4;           // privLength

            // WOFF table directory (20 bytes each)
            for (int i = 0; i < numTables; i++)
            {
                WriteUInt32BE(woff, p, tags[i]); p += 4;
                WriteUInt32BE(woff, p, woffOffsets[i]); p += 4;
                WriteUInt32BE(woff, p, lengths[i]); p += 4;  // compLength = origLength (no compression)
                WriteUInt32BE(woff, p, lengths[i]); p += 4;  // origLength
                WriteUInt32BE(woff, p, checksums[i]); p += 4;
            }

            // Copy table data
            for (int i = 0; i < numTables; i++)
            {
                Array.Copy(ttf, (int)offsets[i], woff, (int)woffOffsets[i], (int)lengths[i]);
            }

            return woff;
        }

        private static uint ReadUInt32BE(byte[] d, int o) =>
            ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];

        private static int ReadUInt16BE(byte[] d, int o) =>
            ((int)d[o] << 8) | d[o + 1];

        private static void WriteUInt32BE(byte[] d, int o, uint v)
        { d[o] = (byte)(v >> 24); d[o + 1] = (byte)(v >> 16); d[o + 2] = (byte)(v >> 8); d[o + 3] = (byte)v; }

        private static void WriteUInt16BE(byte[] d, int o, ushort v)
        { d[o] = (byte)(v >> 8); d[o + 1] = (byte)v; }

        // Creates a WOFF where exactly one table is zlib-compressed so we can test the
        // InflaterInputStream decompression path in WoffConverter. The largest table is
        // chosen (rather than always table 0) because small tables can compress *larger*
        // than their original size once zlib's header/checksum overhead is added, which
        // would violate the WOFF spec's compLength <= origLength requirement.
        private static byte[] WrapTtfAsWoffWithCompressedFirstTable(byte[] ttf)
        {
            uint sfVersion = ReadUInt32BE(ttf, 0);
            int numTables = ReadUInt16BE(ttf, 4);

            var tags = new uint[numTables];
            var checksums = new uint[numTables];
            var offsets = new uint[numTables];
            var lengths = new uint[numTables];

            for (int i = 0; i < numTables; i++)
            {
                int e = 12 + i * 16;
                tags[i] = ReadUInt32BE(ttf, e);
                checksums[i] = ReadUInt32BE(ttf, e + 4);
                offsets[i] = ReadUInt32BE(ttf, e + 8);
                lengths[i] = ReadUInt32BE(ttf, e + 12);
            }

            int compressedIndex = 0;
            for (int i = 1; i < numTables; i++)
            {
                if (lengths[i] > lengths[compressedIndex])
                    compressedIndex = i;
            }

            // zlib-compress the chosen table
            byte[] compressedTableRaw = new byte[checked((int)lengths[compressedIndex])];
            Array.Copy(ttf, (int)offsets[compressedIndex], compressedTableRaw, 0, (int)lengths[compressedIndex]);
            byte[] compressedTableCompressed = ZlibCompress(compressedTableRaw);

            // Build WOFF: header(44) + directory(20*n) + one compressed table + verbatim others
            int dataStart = 44 + numTables * 20;
            var woffOffsets = new uint[numTables];
            var woffCompLengths = new uint[numTables];

            uint woffPos = (uint)dataStart;

            for (int i = 0; i < numTables; i++)
            {
                woffOffsets[i] = woffPos;
                woffCompLengths[i] = i == compressedIndex ? (uint)compressedTableCompressed.Length : lengths[i];
                woffPos += woffCompLengths[i];
                woffPos = (woffPos + 3u) & ~3u;
            }

            byte[] woff = new byte[checked((int)woffPos)];
            int p = 0;

            WriteUInt32BE(woff, p, 0x774F4646); p += 4;
            WriteUInt32BE(woff, p, sfVersion); p += 4;
            WriteUInt32BE(woff, p, woffPos); p += 4;
            WriteUInt16BE(woff, p, (ushort)numTables); p += 2;
            WriteUInt16BE(woff, p, 0); p += 2;
            WriteUInt32BE(woff, p, (uint)ttf.Length); p += 4;
            WriteUInt16BE(woff, p, 1); p += 2;
            WriteUInt16BE(woff, p, 0); p += 2;
            WriteUInt32BE(woff, p, 0); p += 4;
            WriteUInt32BE(woff, p, 0); p += 4;
            WriteUInt32BE(woff, p, 0); p += 4;
            WriteUInt32BE(woff, p, 0); p += 4;
            WriteUInt32BE(woff, p, 0); p += 4;

            for (int i = 0; i < numTables; i++)
            {
                WriteUInt32BE(woff, p, tags[i]); p += 4;
                WriteUInt32BE(woff, p, woffOffsets[i]); p += 4;
                WriteUInt32BE(woff, p, woffCompLengths[i]); p += 4;
                WriteUInt32BE(woff, p, lengths[i]); p += 4;
                WriteUInt32BE(woff, p, checksums[i]); p += 4;
            }

            // Write the compressed table
            Array.Copy(compressedTableCompressed, 0, woff, (int)woffOffsets[compressedIndex], compressedTableCompressed.Length);

            // Write remaining tables (verbatim)
            for (int i = 0; i < numTables; i++)
            {
                if (i != compressedIndex)
                    Array.Copy(ttf, (int)offsets[i], woff, (int)woffOffsets[i], (int)lengths[i]);
            }

            return woff;
        }

        private static byte[] ZlibCompress(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(data, 0, data.Length);
            return ms.ToArray();
        }
    }
}

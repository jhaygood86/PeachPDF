using System;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Hand-builds a minimal, baseline (uncompressed, single-strip, chunky) RGB TIFF byte-for-byte - same
    /// "no encoder to round-trip through" reasoning as <c>CmykTiffFixture</c>, adapted for the plain RGB
    /// (PhotometricInterpretation 2, 3 samples/pixel) case instead of CMYK - a non-CMYK TIFF reaches
    /// <c>PeachImageSource</c>'s generic raster fallback (issue #1107's <c>IsLosslessEncoding</c> check),
    /// not <c>DecodeCmykRaster</c>. Little-endian ("II") byte order.
    /// </summary>
    internal static class RgbTiffFixture
    {
        private const ushort TagImageWidth = 256;
        private const ushort TagImageLength = 257;
        private const ushort TagBitsPerSample = 258;
        private const ushort TagCompression = 259;
        private const ushort TagPhotometricInterpretation = 262;
        private const ushort TagStripOffsets = 273;
        private const ushort TagSamplesPerPixel = 277;
        private const ushort TagRowsPerStrip = 278;
        private const ushort TagStripByteCounts = 279;
        private const ushort TagPlanarConfiguration = 284;

        private const ushort TypeShort = 3;
        private const ushort TypeLong = 4;

        /// <summary>
        /// Builds a <paramref name="width"/>x<paramref name="height"/> RGB TIFF, every pixel set to
        /// (<paramref name="r"/>, <paramref name="g"/>, <paramref name="b"/>), uncompressed
        /// (Compression tag = 1 - lossless by construction, per <c>TiffDecoder.Identify</c>'s
        /// <c>IsLosslessEncoding</c> computation).
        /// </summary>
        internal static byte[] Build(int width, int height, byte r, byte g, byte b)
        {
            var pixelData = new byte[width * height * 3];
            for (var i = 0; i < pixelData.Length; i += 3)
            {
                pixelData[i] = r;
                pixelData[i + 1] = g;
                pixelData[i + 2] = b;
            }

            const int entryCount = 9;
            const int headerSize = 8;
            var ifdStart = headerSize;
            var ifdSize = 2 + entryCount * 12 + 4; // count + entries + next-IFD offset
            var dataStart = ifdStart + ifdSize;

            var bitsPerSampleOffset = dataStart;
            var cursor = bitsPerSampleOffset + 6; // 3 x SHORT

            var stripOffset = cursor;
            cursor += pixelData.Length;

            var buffer = new byte[cursor];

            // --- Header ---
            buffer[0] = (byte)'I';
            buffer[1] = (byte)'I';
            WriteUInt16(buffer, 2, 42);
            WriteUInt32(buffer, 4, (uint)ifdStart);

            // --- IFD ---
            var pos = ifdStart;
            WriteUInt16(buffer, pos, entryCount);
            pos += 2;

            pos = WriteShortEntry(buffer, pos, TagImageWidth, (ushort)width);
            pos = WriteShortEntry(buffer, pos, TagImageLength, (ushort)height);
            pos = WriteEntry(buffer, pos, TagBitsPerSample, TypeShort, 3, (uint)bitsPerSampleOffset);
            pos = WriteShortEntry(buffer, pos, TagCompression, 1); // no compression
            pos = WriteShortEntry(buffer, pos, TagPhotometricInterpretation, 2); // RGB
            pos = WriteEntry(buffer, pos, TagStripOffsets, TypeLong, 1, (uint)stripOffset);
            pos = WriteShortEntry(buffer, pos, TagSamplesPerPixel, 3);
            pos = WriteEntry(buffer, pos, TagRowsPerStrip, TypeLong, 1, (uint)height);
            pos = WriteEntry(buffer, pos, TagStripByteCounts, TypeLong, 1, (uint)pixelData.Length);
            pos = WriteShortEntry(buffer, pos, TagPlanarConfiguration, 1); // chunky

            WriteUInt32(buffer, pos, 0); // no next IFD
            pos += 4;

            // --- Out-of-line data ---
            WriteUInt16(buffer, bitsPerSampleOffset, 8);
            WriteUInt16(buffer, bitsPerSampleOffset + 2, 8);
            WriteUInt16(buffer, bitsPerSampleOffset + 4, 8);

            Array.Copy(pixelData, 0, buffer, stripOffset, pixelData.Length);

            return buffer;
        }

        // A SHORT (2-byte) value stored inline is left-aligned within the 4-byte value/offset field for
        // little-endian TIFF (the remaining 2 bytes stay zero).
        private static int WriteShortEntry(byte[] buffer, int pos, ushort tag, ushort value)
        {
            WriteUInt16(buffer, pos, tag);
            WriteUInt16(buffer, pos + 2, TypeShort);
            WriteUInt32(buffer, pos + 4, 1);
            WriteUInt16(buffer, pos + 8, value);
            WriteUInt16(buffer, pos + 10, 0);
            return pos + 12;
        }

        private static int WriteEntry(byte[] buffer, int pos, ushort tag, ushort type, uint count, uint valueOrOffset)
        {
            WriteUInt16(buffer, pos, tag);
            WriteUInt16(buffer, pos + 2, type);
            WriteUInt32(buffer, pos + 4, count);
            WriteUInt32(buffer, pos + 8, valueOrOffset);
            return pos + 12;
        }

        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }
    }
}

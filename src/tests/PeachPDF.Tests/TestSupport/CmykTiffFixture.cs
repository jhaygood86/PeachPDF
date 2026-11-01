using System;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Hand-builds a minimal, baseline (uncompressed, single-strip, chunky) CMYK TIFF byte-for-byte - see
    /// <c>IccProfileFixture</c>'s own doc comment for why: PeachImage's TIFF codec is decode-only (its
    /// <c>Encode</c> is not implemented), so there's no round-trip path to produce a real one, and a real
    /// CMYK TIFF from a scanner/press workflow is typically megabytes. Little-endian ("II") byte order.
    /// </summary>
    internal static class CmykTiffFixture
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
        private const ushort TagInkSet = 332;
        private const ushort TagIccProfile = 34675;

        private const ushort TypeShort = 3;
        private const ushort TypeLong = 4;
        private const ushort TypeUndefined = 7;

        /// <summary>
        /// Builds a <paramref name="width"/>x<paramref name="height"/> CMYK TIFF, every pixel set to
        /// (<paramref name="c"/>, <paramref name="m"/>, <paramref name="y"/>, <paramref name="k"/>), with
        /// <paramref name="iccProfile"/> embedded via tag 34675 when given.
        /// </summary>
        internal static byte[] Build(int width, int height, byte c, byte m, byte y, byte k, byte[]? iccProfile = null)
        {
            var pixelData = new byte[width * height * 4];
            for (var i = 0; i < pixelData.Length; i += 4)
            {
                pixelData[i] = c;
                pixelData[i + 1] = m;
                pixelData[i + 2] = y;
                pixelData[i + 3] = k;
            }

            // IFD entry count depends on whether an ICC profile tag is included.
            var entryCount = iccProfile is null ? 10 : 11;

            const int headerSize = 8;
            var ifdStart = headerSize;
            var ifdSize = 2 + entryCount * 12 + 4; // count + entries + next-IFD offset
            var dataStart = ifdStart + ifdSize;

            // Out-of-line value blocks, in the order they're laid out after the IFD.
            var bitsPerSampleOffset = dataStart;
            var cursor = bitsPerSampleOffset + 8; // 4 x SHORT

            int? iccOffset = null;
            if (iccProfile is not null)
            {
                iccOffset = cursor;
                cursor += iccProfile.Length;
            }

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
            WriteUInt16(buffer, pos, (ushort)entryCount);
            pos += 2;

            pos = WriteShortEntry(buffer, pos, TagImageWidth, (ushort)width);
            pos = WriteShortEntry(buffer, pos, TagImageLength, (ushort)height);
            pos = WriteEntry(buffer, pos, TagBitsPerSample, TypeShort, 4, (uint)bitsPerSampleOffset);
            pos = WriteShortEntry(buffer, pos, TagCompression, 1); // no compression
            pos = WriteShortEntry(buffer, pos, TagPhotometricInterpretation, 5); // Separated (CMYK)
            pos = WriteEntry(buffer, pos, TagStripOffsets, TypeLong, 1, (uint)stripOffset);
            pos = WriteShortEntry(buffer, pos, TagSamplesPerPixel, 4);
            pos = WriteEntry(buffer, pos, TagRowsPerStrip, TypeLong, 1, (uint)height);
            pos = WriteEntry(buffer, pos, TagStripByteCounts, TypeLong, 1, (uint)pixelData.Length);
            pos = WriteShortEntry(buffer, pos, TagPlanarConfiguration, 1); // chunky
            if (iccOffset is int reallyIccOffset)
            {
                pos = WriteEntry(buffer, pos, TagIccProfile, TypeUndefined, (uint)iccProfile!.Length, (uint)reallyIccOffset);
            }
            else
            {
                pos = WriteShortEntry(buffer, pos, TagInkSet, 1); // CMYK
            }

            WriteUInt32(buffer, pos, 0); // no next IFD
            pos += 4;

            // --- Out-of-line data ---
            WriteUInt16(buffer, bitsPerSampleOffset, 8);
            WriteUInt16(buffer, bitsPerSampleOffset + 2, 8);
            WriteUInt16(buffer, bitsPerSampleOffset + 4, 8);
            WriteUInt16(buffer, bitsPerSampleOffset + 6, 8);

            if (iccProfile is not null)
            {
                Array.Copy(iccProfile, 0, buffer, iccOffset!.Value, iccProfile.Length);
            }

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

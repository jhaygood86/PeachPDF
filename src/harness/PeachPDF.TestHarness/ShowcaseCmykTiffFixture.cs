using System;

/// <summary>
/// Hand-builds a minimal, baseline (uncompressed, single-strip, chunky) CMYK TIFF byte-for-byte for the
/// CMYK TIFF showcase - PeachImage's TIFF codec is decode-only (no <c>Encode</c>), so there's no round
/// -trip path to produce a real one, and a real CMYK TIFF from a scanner/press workflow is typically
/// megabytes. Same shape as <c>PeachPDF.Tests.TestSupport.CmykTiffFixture</c> (not referenced directly -
/// that helper is internal to the test assembly), extended to paint a simple two-color pattern instead of
/// a single flat fill, so the showcase actually shows something.
/// </summary>
internal static class ShowcaseCmykTiffFixture
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
    private const ushort TagIccProfile = 34675;

    private const ushort TypeShort = 3;
    private const ushort TypeLong = 4;
    private const ushort TypeUndefined = 7;

    /// <summary>
    /// Builds a <paramref name="width"/>x<paramref name="height"/> CMYK TIFF: a checkerboard of
    /// (<paramref name="c1"/>,<paramref name="m1"/>,<paramref name="y1"/>,<paramref name="k1"/>) and
    /// (<paramref name="c2"/>,<paramref name="m2"/>,<paramref name="y2"/>,<paramref name="k2"/>) in
    /// <paramref name="blockSize"/>-pixel blocks, with <paramref name="iccProfile"/> embedded via tag
    /// 34675 when given.
    /// </summary>
    internal static byte[] BuildCheckerboard(int width, int height, int blockSize,
        byte c1, byte m1, byte y1, byte k1, byte c2, byte m2, byte y2, byte k2, byte[]? iccProfile = null)
    {
        var pixelData = new byte[width * height * 4];
        for (var py = 0; py < height; py++)
        {
            for (var px = 0; px < width; px++)
            {
                var block = (px / blockSize) + (py / blockSize);
                var offset = (py * width + px) * 4;
                if (block % 2 == 0)
                {
                    pixelData[offset] = c1;
                    pixelData[offset + 1] = m1;
                    pixelData[offset + 2] = y1;
                    pixelData[offset + 3] = k1;
                }
                else
                {
                    pixelData[offset] = c2;
                    pixelData[offset + 1] = m2;
                    pixelData[offset + 2] = y2;
                    pixelData[offset + 3] = k2;
                }
            }
        }

        var entryCount = iccProfile is null ? 9 : 10;

        const int headerSize = 8;
        var ifdStart = headerSize;
        var ifdSize = 2 + entryCount * 12 + 4;
        var dataStart = ifdStart + ifdSize;

        var bitsPerSampleOffset = dataStart;
        var cursor = bitsPerSampleOffset + 8;

        int? iccOffset = null;
        if (iccProfile is not null)
        {
            iccOffset = cursor;
            cursor += iccProfile.Length;
        }

        var stripOffset = cursor;
        cursor += pixelData.Length;

        var buffer = new byte[cursor];

        buffer[0] = (byte)'I';
        buffer[1] = (byte)'I';
        WriteUInt16(buffer, 2, 42);
        WriteUInt32(buffer, 4, (uint)ifdStart);

        var pos = ifdStart;
        WriteUInt16(buffer, pos, (ushort)entryCount);
        pos += 2;

        pos = WriteShortEntry(buffer, pos, TagImageWidth, (ushort)width);
        pos = WriteShortEntry(buffer, pos, TagImageLength, (ushort)height);
        pos = WriteEntry(buffer, pos, TagBitsPerSample, TypeShort, 4, (uint)bitsPerSampleOffset);
        pos = WriteShortEntry(buffer, pos, TagCompression, 1);
        pos = WriteShortEntry(buffer, pos, TagPhotometricInterpretation, 5);
        pos = WriteEntry(buffer, pos, TagStripOffsets, TypeLong, 1, (uint)stripOffset);
        pos = WriteShortEntry(buffer, pos, TagSamplesPerPixel, 4);
        pos = WriteEntry(buffer, pos, TagRowsPerStrip, TypeLong, 1, (uint)height);
        pos = WriteEntry(buffer, pos, TagStripByteCounts, TypeLong, 1, (uint)pixelData.Length);
        if (iccOffset is int reallyIccOffset)
        {
            pos = WriteEntry(buffer, pos, TagIccProfile, TypeUndefined, (uint)iccProfile!.Length, (uint)reallyIccOffset);
        }
        else
        {
            pos = WriteShortEntry(buffer, pos, TagPlanarConfiguration, 1);
        }

        WriteUInt32(buffer, pos, 0);
        pos += 4;

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

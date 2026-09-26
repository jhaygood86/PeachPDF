using System;

namespace PeachDrawing.Text.Internal.Fonts.OpenType.Variations
{
    /// <summary>
    /// Big-endian reads over a span of a font table. The variation tables are parsed from the font's bytes without going through the
    /// face's shared read cursor, so reading them needs no lock and can happen from any number of threads.
    /// </summary>
    internal static class BigEndian
    {
        internal static byte U8(ReadOnlySpan<byte> data, int offset) => data[offset];

        internal static sbyte I8(ReadOnlySpan<byte> data, int offset) => (sbyte)data[offset];

        internal static ushort U16(ReadOnlySpan<byte> data, int offset) => (ushort)((data[offset] << 8) | data[offset + 1]);

        internal static short I16(ReadOnlySpan<byte> data, int offset) => (short)U16(data, offset);

        internal static uint U32(ReadOnlySpan<byte> data, int offset) =>
            ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];

        internal static int I32(ReadOnlySpan<byte> data, int offset) => (int)U32(data, offset);

        /// <summary>A 2.14 fixed-point number, as the normalized axis coordinates are written.</summary>
        internal static double F2Dot14(ReadOnlySpan<byte> data, int offset) => I16(data, offset) / 16384.0;

        /// <summary>A 16.16 fixed-point number, as the axis ranges of <c>fvar</c> are written.</summary>
        internal static double Fixed(ReadOnlySpan<byte> data, int offset) => I32(data, offset) / 65536.0;

        /// <summary>A four-character tag.</summary>
        internal static string Tag(ReadOnlySpan<byte> data, int offset) =>
            new string(new[] { (char)data[offset], (char)data[offset + 1], (char)data[offset + 2], (char)data[offset + 3] });
    }
}

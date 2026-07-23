using System;
using System.Runtime.InteropServices;

namespace PeachPDF.Imaging.Linux
{
    /// <summary>
    /// The SOVERSION group an <c>avifRGBImage</c> layout belongs to. libavif's public struct is NOT
    /// append-only across its SONAME history (verified by diffing the real upstream
    /// <c>include/avif/avif.h</c> at the tag corresponding to each SOVERSION bump: 0.8.4 -> 9, 0.9.3 -> 13,
    /// 0.10.0 -> 14, 0.11.0 -> 15, 1.0.0+ -> 16) - v0.11.0 inserts two fields in the *middle* of the
    /// struct, not at the tail, so a single fixed struct definition would be an out-of-bounds write
    /// (undersized) or a silently-wrong pixel pointer (oversized) depending on direction. 13 and 14
    /// coincidentally share the same field offsets on a standard 64-bit ABI (an alignment-padding
    /// artifact, not a portable guarantee), so they share one layout here.
    /// </summary>
    internal enum AvifRgbImageLayout
    {
        V9,
        V13Or14,
        V15,
        V16,
    }

    /// <summary>
    /// <c>avifRGBImage</c> as laid out in libavif 0.8.4 (SOVERSION 9): width, height, depth, format,
    /// chromaUpsampling, ignoreAlpha, pixels, rowBytes - no alphaPremultiplied/isFloat/maxThreads fields
    /// exist yet. The single <see cref="Reserved0"/> field stands in for ignoreAlpha, whose value we never
    /// need to read - only the struct's total shape (so the library writes pixels/rowBytes at the offset
    /// we expect) matters here.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AvifRgbImageV9
    {
        public uint Width;
        public uint Height;
        public uint Depth;
        public int Format;
        public int ChromaUpsampling;
        public int Reserved0;
        public IntPtr Pixels;
        public uint RowBytes;
    }

    /// <summary>
    /// <c>avifRGBImage</c> as laid out in libavif 0.9.3/0.10.x (SOVERSION 13/14). 13 adds
    /// <c>alphaPremultiplied</c>; 14 additionally adds <c>isFloat</c> - both land at the same
    /// <c>pixels</c>/<c>rowBytes</c> offset on a standard 64-bit ABI, so one shared layout covers both.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AvifRgbImageV13Or14
    {
        public uint Width;
        public uint Height;
        public uint Depth;
        public int Format;
        public int ChromaUpsampling;
        public int Reserved0;
        public int Reserved1;
        public int Reserved2;
        public IntPtr Pixels;
        public uint RowBytes;
    }

    /// <summary>
    /// <c>avifRGBImage</c> as laid out in libavif 0.11.x (SOVERSION 15), which inserts
    /// <c>chromaDownsampling</c>/<c>avoidLibYUV</c> between <c>chromaUpsampling</c> and <c>ignoreAlpha</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AvifRgbImageV15
    {
        public uint Width;
        public uint Height;
        public uint Depth;
        public int Format;
        public int ChromaUpsampling;
        public int Reserved0;
        public int Reserved1;
        public int Reserved2;
        public int Reserved3;
        public int Reserved4;
        public IntPtr Pixels;
        public uint RowBytes;
    }

    /// <summary>
    /// <c>avifRGBImage</c> as laid out in libavif 1.0.0 through the current 1.4.2 (SOVERSION 16), which
    /// additionally adds <c>maxThreads</c>. Stable for over a year of releases.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AvifRgbImageV16
    {
        public uint Width;
        public uint Height;
        public uint Depth;
        public int Format;
        public int ChromaUpsampling;
        public int Reserved0;
        public int Reserved1;
        public int Reserved2;
        public int Reserved3;
        public int Reserved4;
        public int Reserved5;
        public IntPtr Pixels;
        public uint RowBytes;
    }

    /// <summary>
    /// libavif bindings. The 6 functions used here have identical signatures across every SOVERSION this
    /// project supports (9/13/14/15/16, verified by diffing the real upstream header at the tag
    /// corresponding to each) - only <c>avifRGBImage</c>'s struct layout needs per-group dispatch, which is
    /// why <c>avifRGBImageSetDefaults</c>/<c>avifImageYUVToRGB</c> each have 4 concrete (non-generic)
    /// overloads below rather than one generic method - LibraryImport's source generator does not support
    /// generic P/Invoke signatures. <c>avifDecoder</c> is treated as fully opaque (an untyped
    /// <see cref="IntPtr"/>, never marshaled as a struct) since it churns even more than
    /// <c>avifRGBImage</c>; the one field needed off it - <c>decoder-&gt;image</c> - is read via
    /// <see cref="Marshal.ReadIntPtr(IntPtr, int)"/> using a per-group offset instead.
    /// </summary>
    internal static partial class NativeAvif
    {
        public const string LibraryName = "peachpdf-avif";

        /// <summary>AVIF_RESULT_OK, per avif.h's avifResult enum.</summary>
        public const int ResultOk = 0;

        /// <summary>AVIF_RGB_FORMAT_RGBA, per avif.h's avifRGBFormat enum.</summary>
        public const int RgbFormatRgba = 1;

        /// <summary>Byte offset of avifDecoder's `image` field, keyed by SOVERSION (9/13/14/15/16).</summary>
        public static int DecoderImageFieldOffset(AvifRgbImageLayout layout) => layout switch
        {
            AvifRgbImageLayout.V9 => 16,
            AvifRgbImageLayout.V13Or14 => 40,
            AvifRgbImageLayout.V15 => 48,
            AvifRgbImageLayout.V16 => 48,
            _ => throw new ArgumentOutOfRangeException(nameof(layout)),
        };

        [LibraryImport(LibraryName, EntryPoint = "avifDecoderCreate")]
        public static partial IntPtr avifDecoderCreate();

        [LibraryImport(LibraryName, EntryPoint = "avifDecoderDestroy")]
        public static partial void avifDecoderDestroy(IntPtr decoder);

        [LibraryImport(LibraryName, EntryPoint = "avifDecoderSetIOMemory")]
        public static partial int avifDecoderSetIOMemory(IntPtr decoder, ReadOnlySpan<byte> data, nuint size);

        [LibraryImport(LibraryName, EntryPoint = "avifDecoderParse")]
        public static partial int avifDecoderParse(IntPtr decoder);

        [LibraryImport(LibraryName, EntryPoint = "avifDecoderNextImage")]
        public static partial int avifDecoderNextImage(IntPtr decoder);

        [LibraryImport(LibraryName, EntryPoint = "avifRGBImageSetDefaults")]
        public static partial void avifRGBImageSetDefaults_V9(ref AvifRgbImageV9 rgb, IntPtr image);

        [LibraryImport(LibraryName, EntryPoint = "avifRGBImageSetDefaults")]
        public static partial void avifRGBImageSetDefaults_V13Or14(ref AvifRgbImageV13Or14 rgb, IntPtr image);

        [LibraryImport(LibraryName, EntryPoint = "avifRGBImageSetDefaults")]
        public static partial void avifRGBImageSetDefaults_V15(ref AvifRgbImageV15 rgb, IntPtr image);

        [LibraryImport(LibraryName, EntryPoint = "avifRGBImageSetDefaults")]
        public static partial void avifRGBImageSetDefaults_V16(ref AvifRgbImageV16 rgb, IntPtr image);

        [LibraryImport(LibraryName, EntryPoint = "avifImageYUVToRGB")]
        public static partial int avifImageYUVToRGB_V9(IntPtr image, ref AvifRgbImageV9 rgb);

        [LibraryImport(LibraryName, EntryPoint = "avifImageYUVToRGB")]
        public static partial int avifImageYUVToRGB_V13Or14(IntPtr image, ref AvifRgbImageV13Or14 rgb);

        [LibraryImport(LibraryName, EntryPoint = "avifImageYUVToRGB")]
        public static partial int avifImageYUVToRGB_V15(IntPtr image, ref AvifRgbImageV15 rgb);

        [LibraryImport(LibraryName, EntryPoint = "avifImageYUVToRGB")]
        public static partial int avifImageYUVToRGB_V16(IntPtr image, ref AvifRgbImageV16 rgb);
    }
}

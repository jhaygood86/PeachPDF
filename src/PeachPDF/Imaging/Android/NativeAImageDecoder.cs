using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PeachPDF.Imaging.Android
{
    /// <summary>
    /// NDK ImageDecoder (&lt;android/imagedecoder.h&gt;), added in API level 30 (Android 11) and shipped in
    /// the system-provided libjnigraphics.so - unlike libavif, NDK APIs are guaranteed ABI-stable once
    /// introduced, so (unlike <see cref="Linux.NativeAvif"/>) there is no per-version struct/offset
    /// dispatch needed here; everything is an opaque pointer plus plain scalar getters.
    /// </summary>
    [SupportedOSPlatform("android30.0")]
    internal static partial class NativeAImageDecoder
    {
        private const string LibraryName = "jnigraphics";

        /// <summary>ANDROID_BITMAP_FORMAT_RGBA_8888, per &lt;android/bitmap.h&gt;.</summary>
        public const int AndroidBitmapFormatRgba8888 = 1;

        /// <summary>ANDROID_IMAGE_DECODER_SUCCESS.</summary>
        public const int ResultSuccess = 0;

        [LibraryImport(LibraryName, EntryPoint = "AImageDecoder_createFromBuffer")]
        public static partial int AImageDecoder_createFromBuffer(ReadOnlySpan<byte> buffer, nuint length, out IntPtr outDecoder);

        [LibraryImport(LibraryName, EntryPoint = "AImageDecoder_delete")]
        public static partial void AImageDecoder_delete(IntPtr decoder);

        [LibraryImport(LibraryName, EntryPoint = "AImageDecoder_setAndroidBitmapFormat")]
        public static partial int AImageDecoder_setAndroidBitmapFormat(IntPtr decoder, int format);

        [LibraryImport(LibraryName, EntryPoint = "AImageDecoder_getHeaderInfo")]
        public static partial IntPtr AImageDecoder_getHeaderInfo(IntPtr decoder);

        [LibraryImport(LibraryName, EntryPoint = "AImageDecoderHeaderInfo_getWidth")]
        public static partial int AImageDecoderHeaderInfo_getWidth(IntPtr info);

        [LibraryImport(LibraryName, EntryPoint = "AImageDecoderHeaderInfo_getHeight")]
        public static partial int AImageDecoderHeaderInfo_getHeight(IntPtr info);

        [LibraryImport(LibraryName, EntryPoint = "AImageDecoder_getMinimumStride")]
        public static partial nuint AImageDecoder_getMinimumStride(IntPtr decoder);

        [LibraryImport(LibraryName, EntryPoint = "AImageDecoder_decodeImage")]
        public static partial int AImageDecoder_decodeImage(IntPtr decoder, IntPtr pixels, nuint stride, nuint size);
    }
}

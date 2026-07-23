using System;
using System.Runtime.InteropServices;

namespace PeachPDF.Imaging.Linux
{
    /// <summary>
    /// libwebp's "simple decoding API" - confirmed (by diffing the real upstream header at v0.5.0 through
    /// the current v1.6.0) to have scalar/pointer-only signatures that have never changed, so a single
    /// fixed binding is safe across every SONAME (6 or 7) this project probes.
    /// </summary>
    internal static partial class NativeWebP
    {
        /// <summary>
        /// Logical library name - resolved to whichever real SONAME actually loaded via a
        /// <see cref="NativeLibrary.SetDllImportResolver"/> callback registered by <see cref="LinuxPlatformCodec"/>.
        /// </summary>
        public const string LibraryName = "peachpdf-webp";

        [LibraryImport(LibraryName, EntryPoint = "WebPGetInfo")]
        public static partial int WebPGetInfo(ReadOnlySpan<byte> data, nuint dataSize, out int width, out int height);

        [LibraryImport(LibraryName, EntryPoint = "WebPDecodeRGBA")]
        public static partial IntPtr WebPDecodeRGBA(ReadOnlySpan<byte> data, nuint dataSize, out int width, out int height);

        [LibraryImport(LibraryName, EntryPoint = "WebPFree")]
        public static partial void WebPFree(IntPtr ptr);
    }
}

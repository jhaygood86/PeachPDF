using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PeachPDF.Imaging.Linux
{
    /// <summary>
    /// Decode-only native codec for Linux, covering exactly WebP and AVIF (JPEG/PNG/GIF/BMP keep using
    /// STB on Linux, unchanged). Neither library ships an encoder we'd use here, so
    /// <see cref="PlatformImageCodecs.Encoder"/> stays null on Linux and JPEG/BMP re-encoding always falls
    /// back to StbImageWriteSharp.
    /// </summary>
    /// <remarks>
    /// A real end-user machine only ever has the runtime shared library installed (e.g.
    /// <c>libwebp.so.7</c>/<c>libavif.so.16</c>) - the unversioned <c>libwebp.so</c>/<c>libavif.so</c>
    /// symlink a bare library-name lookup would resolve against is only installed by the corresponding
    /// <c>-dev</c> package. So this probes real-world versioned SONAME candidates directly, researched
    /// against every Linux distro/version Microsoft currently lists as officially supported for .NET
    /// 8/9/10, plus Ubuntu 20.04/Debian 11 as legacy fallbacks for existing .NET 8 deployments:
    /// <list type="bullet">
    /// <item>libwebp has been SONAME 7 since 1.0.0 (~2018); only SONAME 6 (Ubuntu 20.04/Debian 11) needs a fallback.</item>
    /// <item>libavif's SONAME has bumped repeatedly (9/13/14/15/16). On RHEL/CentOS Stream, libavif is
    /// EPEL-only (never in BaseOS/AppStream); on Ubuntu it lives in `universe`, not `main`; on Ubuntu
    /// 20.04 it isn't packaged at all. None of this needs special-casing - a missing library simply never
    /// loads, and AVIF falls back to nothing (not STB, which cannot decode AVIF either) exactly as
    /// intended.</item>
    /// </list>
    /// </remarks>
    internal sealed class LinuxPlatformCodec : IPlatformImageDecoder
    {
        private static readonly (string SoName, int SoVersion)[] AvifCandidates =
        [
            ("libavif.so.16", 16),
            ("libavif.so.15", 15),
            ("libavif.so.14", 14),
            ("libavif.so.13", 13),
            ("libavif.so.9", 9),
        ];

        private static readonly string[] WebPCandidates = ["libwebp.so.7", "libwebp.so.6"];

        private static IntPtr _webpHandle;
        private static IntPtr _avifHandle;
        private static AvifRgbImageLayout _avifLayout;

        private LinuxPlatformCodec()
        {
        }

        /// <summary>
        /// Probes for libwebp/libavif and, if either loaded, returns a codec instance. Registers a
        /// process-wide <see cref="NativeLibrary.SetDllImportResolver"/> for this assembly - safe to call
        /// only once, which <see cref="PlatformImageCodecs"/>'s static constructor already guarantees.
        /// </summary>
        public static LinuxPlatformCodec? TryCreate()
        {
            NativeLibrary.SetDllImportResolver(typeof(LinuxPlatformCodec).Assembly, ResolveLibrary);

            foreach (var candidate in WebPCandidates)
            {
                if (NativeLibrary.TryLoad(candidate, out var handle))
                {
                    _webpHandle = handle;
                    break;
                }
            }

            foreach (var (soName, soVersion) in AvifCandidates)
            {
                if (NativeLibrary.TryLoad(soName, out var handle))
                {
                    _avifHandle = handle;
                    _avifLayout = ToLayout(soVersion);
                    break;
                }
            }

            return _webpHandle == IntPtr.Zero && _avifHandle == IntPtr.Zero ? null : new LinuxPlatformCodec();
        }

        private static AvifRgbImageLayout ToLayout(int soVersion) => soVersion switch
        {
            16 => AvifRgbImageLayout.V16,
            15 => AvifRgbImageLayout.V15,
            13 or 14 => AvifRgbImageLayout.V13Or14,
            _ => AvifRgbImageLayout.V9,
        };

        private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName == NativeWebP.LibraryName) return _webpHandle;
            if (libraryName == NativeAvif.LibraryName) return _avifHandle;

            // Not one of ours - let the runtime's default resolution algorithm handle it.
            return IntPtr.Zero;
        }

        public bool TryDecode(ReadOnlySpan<byte> bytes, out DecodedImage result)
        {
            try
            {
                if (_webpHandle != IntPtr.Zero && ImageFormatSniffer.IsWebP(bytes))
                {
                    return TryDecodeWebP(bytes, out result);
                }

                if (_avifHandle != IntPtr.Zero && ImageFormatSniffer.IsAvif(bytes))
                {
                    return TryDecodeAvif(bytes, out result);
                }
            }
            catch
            {
                // Any native failure falls back to STB - which cannot decode WebP/AVIF either, so the
                // image simply won't render; that matches the documented "no fallback" behavior for these
                // two formats on Linux.
            }

            result = default;
            return false;
        }

        private static bool TryDecodeWebP(ReadOnlySpan<byte> bytes, out DecodedImage result)
        {
            if (NativeWebP.WebPGetInfo(bytes, (nuint)bytes.Length, out _, out _) == 0)
            {
                result = default;
                return false;
            }

            var pixels = NativeWebP.WebPDecodeRGBA(bytes, (nuint)bytes.Length, out var width, out var height);
            if (pixels == IntPtr.Zero)
            {
                result = default;
                return false;
            }

            try
            {
                var buffer = new byte[width * height * 4];
                Marshal.Copy(pixels, buffer, 0, buffer.Length);
                result = DecodedImage.FromRgba(buffer, width, height);
                return true;
            }
            finally
            {
                NativeWebP.WebPFree(pixels);
            }
        }

        private static bool TryDecodeAvif(ReadOnlySpan<byte> bytes, out DecodedImage result)
        {
            var decoder = NativeAvif.avifDecoderCreate();
            if (decoder == IntPtr.Zero)
            {
                result = default;
                return false;
            }

            try
            {
                if (NativeAvif.avifDecoderSetIOMemory(decoder, bytes, (nuint)bytes.Length) != NativeAvif.ResultOk
                    || NativeAvif.avifDecoderParse(decoder) != NativeAvif.ResultOk
                    || NativeAvif.avifDecoderNextImage(decoder) != NativeAvif.ResultOk)
                {
                    result = default;
                    return false;
                }

                var imagePtr = Marshal.ReadIntPtr(decoder, NativeAvif.DecoderImageFieldOffset(_avifLayout));

                // LibraryImport's source generator doesn't support generic P/Invoke signatures, so each
                // avifRGBImage layout needs its own concrete conversion path - see NativeAvif's remarks.
                return _avifLayout switch
                {
                    AvifRgbImageLayout.V9 => TryConvertV9(imagePtr, out result),
                    AvifRgbImageLayout.V13Or14 => TryConvertV13Or14(imagePtr, out result),
                    AvifRgbImageLayout.V15 => TryConvertV15(imagePtr, out result),
                    _ => TryConvertV16(imagePtr, out result),
                };
            }
            finally
            {
                NativeAvif.avifDecoderDestroy(decoder);
            }
        }

        private static bool TryConvertV9(IntPtr image, out DecodedImage result)
        {
            var rgb = default(AvifRgbImageV9);
            NativeAvif.avifRGBImageSetDefaults_V9(ref rgb, image);
            rgb.Depth = 8;
            rgb.Format = NativeAvif.RgbFormatRgba;
            AllocateRgbaBuffer(rgb.Width, rgb.Height, out var buffer, out rgb.RowBytes, out var byteCount);
            rgb.Pixels = buffer;
            try
            {
                if (NativeAvif.avifImageYUVToRGB_V9(image, ref rgb) != NativeAvif.ResultOk)
                {
                    result = default;
                    return false;
                }

                result = CopyOutRgba(buffer, byteCount, (int)rgb.Width, (int)rgb.Height);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static bool TryConvertV13Or14(IntPtr image, out DecodedImage result)
        {
            var rgb = default(AvifRgbImageV13Or14);
            NativeAvif.avifRGBImageSetDefaults_V13Or14(ref rgb, image);
            rgb.Depth = 8;
            rgb.Format = NativeAvif.RgbFormatRgba;
            AllocateRgbaBuffer(rgb.Width, rgb.Height, out var buffer, out rgb.RowBytes, out var byteCount);
            rgb.Pixels = buffer;
            try
            {
                if (NativeAvif.avifImageYUVToRGB_V13Or14(image, ref rgb) != NativeAvif.ResultOk)
                {
                    result = default;
                    return false;
                }

                result = CopyOutRgba(buffer, byteCount, (int)rgb.Width, (int)rgb.Height);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static bool TryConvertV15(IntPtr image, out DecodedImage result)
        {
            var rgb = default(AvifRgbImageV15);
            NativeAvif.avifRGBImageSetDefaults_V15(ref rgb, image);
            rgb.Depth = 8;
            rgb.Format = NativeAvif.RgbFormatRgba;
            AllocateRgbaBuffer(rgb.Width, rgb.Height, out var buffer, out rgb.RowBytes, out var byteCount);
            rgb.Pixels = buffer;
            try
            {
                if (NativeAvif.avifImageYUVToRGB_V15(image, ref rgb) != NativeAvif.ResultOk)
                {
                    result = default;
                    return false;
                }

                result = CopyOutRgba(buffer, byteCount, (int)rgb.Width, (int)rgb.Height);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static bool TryConvertV16(IntPtr image, out DecodedImage result)
        {
            var rgb = default(AvifRgbImageV16);
            NativeAvif.avifRGBImageSetDefaults_V16(ref rgb, image);
            rgb.Depth = 8;
            rgb.Format = NativeAvif.RgbFormatRgba;
            AllocateRgbaBuffer(rgb.Width, rgb.Height, out var buffer, out rgb.RowBytes, out var byteCount);
            rgb.Pixels = buffer;
            try
            {
                if (NativeAvif.avifImageYUVToRGB_V16(image, ref rgb) != NativeAvif.ResultOk)
                {
                    result = default;
                    return false;
                }

                result = CopyOutRgba(buffer, byteCount, (int)rgb.Width, (int)rgb.Height);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// Forcing depth=8/format=RGBA (done by each TryConvertVxx caller) means the output is always
        /// tightly-packed 8-bit RGBA, so rowBytes is simply width*4 - allocated here ourselves rather than
        /// via avifRGBImageAllocatePixels/avifRGBImageFreePixels, whose return type changed from void to
        /// avifResult exactly at the SOVERSION 15->16 boundary (a real ABI break confirmed in libavif's own
        /// 1.0.0 CHANGELOG entry) and so isn't safe to bind with one fixed signature either.
        /// </summary>
        private static void AllocateRgbaBuffer(uint width, uint height, out IntPtr buffer, out uint rowBytes, out int byteCount)
        {
            rowBytes = width * 4;
            byteCount = checked((int)(rowBytes * height));
            buffer = Marshal.AllocHGlobal(byteCount);
        }

        private static DecodedImage CopyOutRgba(IntPtr buffer, int byteCount, int width, int height)
        {
            var managed = new byte[byteCount];
            Marshal.Copy(buffer, managed, 0, byteCount);
            return DecodedImage.FromRgba(managed, width, height);
        }
    }
}

using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PeachPDF.Imaging.Apple
{
    /// <summary>
    /// A CGRect - on every 64-bit Apple target (the only ones .NET 8/10 support), CGFloat is a native
    /// double, so this is simply 4 sequential doubles (origin.x, origin.y, size.width, size.height).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CGRect
    {
        public double X, Y, Width, Height;
    }

    /// <summary>
    /// P/Invoke surface for CoreFoundation/CoreGraphics/ImageIO. These are long-stable, well-documented C
    /// APIs (unlike libavif's early-version churn) that have not changed shape across any macOS/iOS
    /// version relevant to .NET 8+. Library resolution goes through a logical-name + resolver indirection
    /// (mirroring <see cref="Linux.LinuxPlatformCodec"/>) because on macOS these frameworks load by
    /// absolute path, while on iOS system frameworks are typically statically linked into the app binary
    /// and reached via the main program's own symbol table instead - a distinction this project cannot
    /// verify on-device in this repository's CI (only macos-latest is covered), so the resolver simply
    /// tries the macOS path first and falls back to the main program handle, which self-heals into "native
    /// codec unavailable" (STB fallback) if that assumption is ever wrong on a given iOS build.
    /// </summary>
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("ios")]
    [SupportedOSPlatform("maccatalyst")]
    internal static partial class AppleNativeMethods
    {
        private const string CoreFoundationLogical = "peachpdf-corefoundation";
        private const string CoreGraphicsLogical = "peachpdf-coregraphics";
        private const string ImageIoLogical = "peachpdf-imageio";

        private const string CoreFoundationMacPath = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string CoreGraphicsMacPath = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string ImageIoMacPath = "/System/Library/Frameworks/ImageIO.framework/ImageIO";

        private static IntPtr _coreFoundationHandle;
        private static IntPtr _coreGraphicsHandle;
        private static IntPtr _imageIoHandle;
        private static bool _resolverRegistered;
        private static readonly object ResolverLock = new();

        public const int CFStringEncodingUtf8 = 0x08000100;

        /// <summary>AVIF_RGB_FORMAT_RGBA-equivalent: straight (non-premultiplied), alpha-last, byte order
        /// matching our RGBA convention. CGBitmapContextCreate itself only accepts premultiplied alpha for
        /// a 4-component format, so the raw context buffer comes out premultiplied and the caller
        /// un-premultiplies afterward - see AppleImageIoCodec.</summary>
        public const uint BitmapInfoPremultipliedLastBigEndian = 1u /* kCGImageAlphaPremultipliedLast */ | (4u << 12) /* kCGBitmapByteOrder32Big */;

        /// <summary>Registers the DllImportResolver exactly once - safe because PlatformImageCodecs's
        /// static constructor only ever constructs one AppleImageIoCodec per process.</summary>
        public static void EnsureLoaded()
        {
            lock (ResolverLock)
            {
                if (_resolverRegistered) return;

                NativeLibrary.SetDllImportResolver(typeof(AppleNativeMethods).Assembly, Resolve);
                _resolverRegistered = true;

                _coreFoundationHandle = LoadFrameworkOrMainProgram(CoreFoundationMacPath);
                _coreGraphicsHandle = LoadFrameworkOrMainProgram(CoreGraphicsMacPath);
                _imageIoHandle = LoadFrameworkOrMainProgram(ImageIoMacPath);
            }
        }

        private static IntPtr LoadFrameworkOrMainProgram(string macPath)
        {
            if (NativeLibrary.TryLoad(macPath, out var handle)) return handle;

            try
            {
                return NativeLibrary.GetMainProgramHandle();
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName == CoreFoundationLogical) return _coreFoundationHandle;
            if (libraryName == CoreGraphicsLogical) return _coreGraphicsHandle;
            if (libraryName == ImageIoLogical) return _imageIoHandle;
            return IntPtr.Zero;
        }

        // ---- CoreFoundation ----

        [LibraryImport(CoreFoundationLogical, EntryPoint = "CFDataCreate")]
        public static partial IntPtr CFDataCreate(IntPtr allocator, ReadOnlySpan<byte> bytes, nint length);

        [LibraryImport(CoreFoundationLogical, EntryPoint = "CFDataCreateMutable")]
        public static partial IntPtr CFDataCreateMutable(IntPtr allocator, nint capacity);

        [LibraryImport(CoreFoundationLogical, EntryPoint = "CFDataGetLength")]
        public static partial nint CFDataGetLength(IntPtr data);

        [LibraryImport(CoreFoundationLogical, EntryPoint = "CFDataGetBytePtr")]
        public static partial IntPtr CFDataGetBytePtr(IntPtr data);

        [LibraryImport(CoreFoundationLogical, EntryPoint = "CFRelease")]
        public static partial void CFRelease(IntPtr cf);

        [LibraryImport(CoreFoundationLogical, EntryPoint = "CFStringCreateWithCString", StringMarshalling = StringMarshalling.Utf8)]
        public static partial IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, int encoding);

        // ---- ImageIO ----

        [LibraryImport(ImageIoLogical, EntryPoint = "CGImageSourceCreateWithData")]
        public static partial IntPtr CGImageSourceCreateWithData(IntPtr data, IntPtr options);

        [LibraryImport(ImageIoLogical, EntryPoint = "CGImageSourceCreateImageAtIndex")]
        public static partial IntPtr CGImageSourceCreateImageAtIndex(IntPtr isrc, nuint index, IntPtr options);

        [LibraryImport(ImageIoLogical, EntryPoint = "CGImageDestinationCreateWithData")]
        public static partial IntPtr CGImageDestinationCreateWithData(IntPtr data, IntPtr type, nuint count, IntPtr options);

        [LibraryImport(ImageIoLogical, EntryPoint = "CGImageDestinationAddImage")]
        public static partial void CGImageDestinationAddImage(IntPtr idst, IntPtr image, IntPtr properties);

        [LibraryImport(ImageIoLogical, EntryPoint = "CGImageDestinationFinalize")]
        [return: MarshalAs(UnmanagedType.U1)]
        public static partial bool CGImageDestinationFinalize(IntPtr idst);

        // ---- CoreGraphics ----

        [LibraryImport(CoreGraphicsLogical, EntryPoint = "CGImageGetWidth")]
        public static partial nuint CGImageGetWidth(IntPtr image);

        [LibraryImport(CoreGraphicsLogical, EntryPoint = "CGImageGetHeight")]
        public static partial nuint CGImageGetHeight(IntPtr image);

        [LibraryImport(CoreGraphicsLogical, EntryPoint = "CGImageRelease")]
        public static partial void CGImageRelease(IntPtr image);

        [LibraryImport(CoreGraphicsLogical, EntryPoint = "CGColorSpaceCreateDeviceRGB")]
        public static partial IntPtr CGColorSpaceCreateDeviceRGB();

        [LibraryImport(CoreGraphicsLogical, EntryPoint = "CGColorSpaceRelease")]
        public static partial void CGColorSpaceRelease(IntPtr space);

        [LibraryImport(CoreGraphicsLogical, EntryPoint = "CGBitmapContextCreate")]
        public static partial IntPtr CGBitmapContextCreate(IntPtr data, nuint width, nuint height, nuint bitsPerComponent, nuint bytesPerRow, IntPtr space, uint bitmapInfo);

        [LibraryImport(CoreGraphicsLogical, EntryPoint = "CGBitmapContextGetData")]
        public static partial IntPtr CGBitmapContextGetData(IntPtr context);

        [LibraryImport(CoreGraphicsLogical, EntryPoint = "CGContextDrawImage")]
        public static partial void CGContextDrawImage(IntPtr c, CGRect rect, IntPtr image);

        [LibraryImport(CoreGraphicsLogical, EntryPoint = "CGContextRelease")]
        public static partial void CGContextRelease(IntPtr c);

        [LibraryImport(CoreGraphicsLogical, EntryPoint = "CGContextSetBlendMode")]
        public static partial void CGContextSetBlendMode(IntPtr c, int blendMode);

        [LibraryImport(CoreGraphicsLogical, EntryPoint = "CGDataProviderCreateWithData")]
        public static partial IntPtr CGDataProviderCreateWithData(IntPtr info, IntPtr data, nint size, IntPtr releaseData);

        [LibraryImport(CoreGraphicsLogical, EntryPoint = "CGDataProviderRelease")]
        public static partial void CGDataProviderRelease(IntPtr provider);

        [LibraryImport(CoreGraphicsLogical, EntryPoint = "CGImageCreate")]
        public static partial IntPtr CGImageCreate(nuint width, nuint height, nuint bitsPerComponent, nuint bitsPerPixel, nuint bytesPerRow, IntPtr space, uint bitmapInfo, IntPtr provider, IntPtr decode, [MarshalAs(UnmanagedType.U1)] bool shouldInterpolate, int intent);
    }
}

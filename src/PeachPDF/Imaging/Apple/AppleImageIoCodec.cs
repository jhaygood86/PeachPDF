using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PeachPDF.Imaging.Apple
{
    /// <summary>
    /// Native decode/encode for macOS and iOS via Image I/O + CoreGraphics + CoreFoundation. One code path
    /// covers both OSes since Image I/O's C API is identical on both. Handles every format Image I/O
    /// supports on the running OS version - including WebP (since macOS 11/iOS 14) and AVIF (since macOS
    /// 13/iOS 16) for free, with no format-specific code here: "does this OS support it" is answered by
    /// simply attempting the decode and treating a null result as unsupported.
    /// </summary>
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("ios")]
    [SupportedOSPlatform("maccatalyst")]
    internal sealed unsafe class AppleImageIoCodec : IPlatformImageDecoder, IPlatformImageEncoder
    {
        private const int CGImageAlphaPremultipliedLast = 1;
        private const int CGImageAlphaLast = 3;
        private const uint CGBitmapByteOrder32Big = 4u << 12;
        private const int CGBlendModeCopy = 17;
        private const int CGRenderingIntentDefault = 0;

        public AppleImageIoCodec()
        {
            AppleNativeMethods.EnsureLoaded();
        }

        public bool TryDecode(ReadOnlySpan<byte> bytes, out DecodedImage result)
        {
            result = default;

            var cfData = IntPtr.Zero;
            var source = IntPtr.Zero;
            var image = IntPtr.Zero;
            var colorSpace = IntPtr.Zero;
            var context = IntPtr.Zero;

            try
            {
                cfData = AppleNativeMethods.CFDataCreate(IntPtr.Zero, bytes, bytes.Length);
                if (cfData == IntPtr.Zero) return false;

                source = AppleNativeMethods.CGImageSourceCreateWithData(cfData, IntPtr.Zero);
                if (source == IntPtr.Zero) return false;

                image = AppleNativeMethods.CGImageSourceCreateImageAtIndex(source, 0, IntPtr.Zero);
                if (image == IntPtr.Zero) return false;

                var width = (int)AppleNativeMethods.CGImageGetWidth(image);
                var height = (int)AppleNativeMethods.CGImageGetHeight(image);
                if (width <= 0 || height <= 0) return false;

                colorSpace = AppleNativeMethods.CGColorSpaceCreateDeviceRGB();
                if (colorSpace == IntPtr.Zero) return false;

                var bytesPerRow = (nuint)(width * 4);

                // CGBitmapContextCreate only accepts premultiplied alpha for a 4-component destination -
                // the buffer comes out premultiplied and gets un-premultiplied below to match STB's
                // straight-alpha convention.
                context = AppleNativeMethods.CGBitmapContextCreate(IntPtr.Zero, (nuint)width, (nuint)height, 8, bytesPerRow, colorSpace, CGImageAlphaPremultipliedLast | CGBitmapByteOrder32Big);
                if (context == IntPtr.Zero) return false;

                // Defensive: force the draw to replace pixel values outright rather than composite over
                // whatever the freshly-allocated context buffer happened to contain.
                AppleNativeMethods.CGContextSetBlendMode(context, CGBlendModeCopy);
                AppleNativeMethods.CGContextDrawImage(context, new CGRect { X = 0, Y = 0, Width = width, Height = height }, image);

                var dataPtr = AppleNativeMethods.CGBitmapContextGetData(context);
                if (dataPtr == IntPtr.Zero) return false;

                var byteCount = width * height * 4;
                var buffer = new byte[byteCount];
                Marshal.Copy(dataPtr, buffer, 0, byteCount);
                UnpremultiplyInPlace(buffer);

                result = DecodedImage.FromRgba(buffer, width, height);
                return true;
            }
            finally
            {
                if (context != IntPtr.Zero) AppleNativeMethods.CGContextRelease(context);
                if (colorSpace != IntPtr.Zero) AppleNativeMethods.CGColorSpaceRelease(colorSpace);
                if (image != IntPtr.Zero) AppleNativeMethods.CGImageRelease(image);
                if (source != IntPtr.Zero) AppleNativeMethods.CFRelease(source);
                if (cfData != IntPtr.Zero) AppleNativeMethods.CFRelease(cfData);
            }
        }

        public bool TryEncodeJpeg(in DecodedImage image, int quality, out byte[] jpegBytes)
        {
            return TryEncode(image, "public.jpeg", out jpegBytes);
        }

        public bool TryEncodeBmp(in DecodedImage image, out byte[] bmpBytes)
        {
            return TryEncode(image, "com.microsoft.bmp", out bmpBytes);
        }

        private static bool TryEncode(in DecodedImage image, string uti, out byte[] encodedBytes)
        {
            encodedBytes = [];

            var colorSpace = IntPtr.Zero;
            var provider = IntPtr.Zero;
            var cgImage = IntPtr.Zero;
            var utiString = IntPtr.Zero;
            var outputData = IntPtr.Zero;
            var destination = IntPtr.Zero;
            var pinned = default(GCHandle);

            try
            {
                colorSpace = AppleNativeMethods.CGColorSpaceCreateDeviceRGB();
                if (colorSpace == IntPtr.Zero) return false;

                // Pin the managed buffer directly rather than copying into unmanaged memory - the
                // no-op release callback below means CoreGraphics never needs to free it itself; the
                // GCHandle is freed here once the CGImageRef (which is the only thing that could still
                // reference the pinned memory) has been released.
                pinned = GCHandle.Alloc(image.Rgba, GCHandleType.Pinned);
                var dataPtr = pinned.AddrOfPinnedObject();

                provider = AppleNativeMethods.CGDataProviderCreateWithData(IntPtr.Zero, dataPtr, image.Rgba.Length, NoOpReleaseCallback);
                if (provider == IntPtr.Zero) return false;

                var bytesPerRow = (nuint)(image.Width * 4);
                cgImage = AppleNativeMethods.CGImageCreate((nuint)image.Width, (nuint)image.Height, 8, 32, bytesPerRow, colorSpace, CGImageAlphaLast | CGBitmapByteOrder32Big, provider, IntPtr.Zero, false, CGRenderingIntentDefault);
                if (cgImage == IntPtr.Zero) return false;

                utiString = AppleNativeMethods.CFStringCreateWithCString(IntPtr.Zero, uti, AppleNativeMethods.CFStringEncodingUtf8);
                if (utiString == IntPtr.Zero) return false;

                outputData = AppleNativeMethods.CFDataCreateMutable(IntPtr.Zero, 0);
                if (outputData == IntPtr.Zero) return false;

                destination = AppleNativeMethods.CGImageDestinationCreateWithData(outputData, utiString, 1, IntPtr.Zero);
                if (destination == IntPtr.Zero) return false;

                AppleNativeMethods.CGImageDestinationAddImage(destination, cgImage, IntPtr.Zero);
                if (!AppleNativeMethods.CGImageDestinationFinalize(destination)) return false;

                var length = (int)AppleNativeMethods.CFDataGetLength(outputData);
                var bytePtr = AppleNativeMethods.CFDataGetBytePtr(outputData);
                if (bytePtr == IntPtr.Zero || length <= 0) return false;

                encodedBytes = new byte[length];
                Marshal.Copy(bytePtr, encodedBytes, 0, length);
                return true;
            }
            finally
            {
                if (destination != IntPtr.Zero) AppleNativeMethods.CFRelease(destination);
                if (outputData != IntPtr.Zero) AppleNativeMethods.CFRelease(outputData);
                if (utiString != IntPtr.Zero) AppleNativeMethods.CFRelease(utiString);
                if (cgImage != IntPtr.Zero) AppleNativeMethods.CGImageRelease(cgImage);
                if (provider != IntPtr.Zero) AppleNativeMethods.CGDataProviderRelease(provider);
                if (colorSpace != IntPtr.Zero) AppleNativeMethods.CGColorSpaceRelease(colorSpace);
                if (pinned.IsAllocated) pinned.Free();
            }
        }

        private static readonly IntPtr NoOpReleaseCallback = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, nint, void>)&ReleaseDataNoOp;

        [UnmanagedCallersOnly]
        private static void ReleaseDataNoOp(IntPtr info, IntPtr data, nint size)
        {
            // The pinned GCHandle in TryEncode owns this memory's lifetime, not CoreGraphics.
        }

        private static void UnpremultiplyInPlace(byte[] rgba)
        {
            for (var i = 0; i < rgba.Length; i += 4)
            {
                var a = rgba[i + 3];
                if (a is 0 or 255) continue;

                rgba[i] = (byte)Math.Min(255, rgba[i] * 255 / a);
                rgba[i + 1] = (byte)Math.Min(255, rgba[i + 1] * 255 / a);
                rgba[i + 2] = (byte)Math.Min(255, rgba[i + 2] * 255 / a);
            }
        }
    }
}

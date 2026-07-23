using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PeachPDF.Imaging.Android
{
    /// <summary>
    /// Decode-only native codec for Android via the NDK ImageDecoder API. No CI runner in this repository
    /// exercises this path (the test matrix is windows-latest/ubuntu-latest/macos-latest only) - it is
    /// implemented and unit-testable for structure, but its real-device correctness is unverified by CI.
    /// There is no NDK encode counterpart, so Android always falls back to StbImageWriteSharp for JPEG/BMP
    /// re-encoding (<see cref="PlatformImageCodecs.Encoder"/> stays null on Android).
    /// </summary>
    /// <remarks>
    /// Deliberately attributed "android" with no minimum version (unlike <see cref="NativeAImageDecoder"/>,
    /// whose P/Invoke declarations genuinely require API 30+) - this class is safe to construct and call
    /// on any Android version, since <see cref="TryDecode"/> itself checks
    /// <c>OperatingSystem.IsAndroidVersionAtLeast(30)</c> before ever touching the API-30-only native
    /// surface, so callers on older Android don't need their own version guard.
    /// </remarks>
    [SupportedOSPlatform("android")]
    internal sealed class AndroidNdkImageDecoder : IPlatformImageDecoder
    {
        public bool TryDecode(ReadOnlySpan<byte> bytes, out DecodedImage result)
        {
            result = default;

            if (!OperatingSystem.IsAndroidVersionAtLeast(30)) return false;

            var decoderPtr = IntPtr.Zero;
            var buffer = IntPtr.Zero;

            try
            {
                if (NativeAImageDecoder.AImageDecoder_createFromBuffer(bytes, (nuint)bytes.Length, out decoderPtr) != NativeAImageDecoder.ResultSuccess
                    || decoderPtr == IntPtr.Zero)
                {
                    return false;
                }

                if (NativeAImageDecoder.AImageDecoder_setAndroidBitmapFormat(decoderPtr, NativeAImageDecoder.AndroidBitmapFormatRgba8888) != NativeAImageDecoder.ResultSuccess)
                {
                    return false;
                }

                var headerInfo = NativeAImageDecoder.AImageDecoder_getHeaderInfo(decoderPtr);
                if (headerInfo == IntPtr.Zero) return false;

                var width = NativeAImageDecoder.AImageDecoderHeaderInfo_getWidth(headerInfo);
                var height = NativeAImageDecoder.AImageDecoderHeaderInfo_getHeight(headerInfo);
                if (width <= 0 || height <= 0) return false;

                var minimumStride = (int)NativeAImageDecoder.AImageDecoder_getMinimumStride(decoderPtr);
                var stride = Math.Max(width * 4, minimumStride);
                var bufferSize = stride * height;

                buffer = Marshal.AllocHGlobal(bufferSize);

                if (NativeAImageDecoder.AImageDecoder_decodeImage(decoderPtr, buffer, (nuint)stride, (nuint)bufferSize) != NativeAImageDecoder.ResultSuccess)
                {
                    return false;
                }

                var managed = new byte[width * 4 * height];
                if (stride == width * 4)
                {
                    Marshal.Copy(buffer, managed, 0, managed.Length);
                }
                else
                {
                    // Drop the extra stride padding row-by-row.
                    var rowBytes = width * 4;
                    for (var row = 0; row < height; row++)
                    {
                        Marshal.Copy(buffer + row * stride, managed, row * rowBytes, rowBytes);
                    }
                }

                result = DecodedImage.FromRgba(managed, width, height);
                return true;
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                if (decoderPtr != IntPtr.Zero) NativeAImageDecoder.AImageDecoder_delete(decoderPtr);
            }
        }
    }
}

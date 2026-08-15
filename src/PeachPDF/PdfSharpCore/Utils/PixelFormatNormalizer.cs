#if NET10_0_OR_GREATER
using PeachImage;
using System;

namespace PeachPDF.PdfSharpCore.Utils
{
    /// <summary>
    /// Converts a decoded <see cref="Image"/> of any <see cref="PixelFormat"/> to a tightly-packed
    /// Rgba32 buffer, mirroring what StbImageSharp always produced by decoding straight to
    /// <c>ColorComponents.RedGreenBlueAlpha</c>. PeachImage's own <c>TargetPixelFormat</c> converter
    /// doesn't cover every source format (e.g. Cmyk32, the 16-bit-per-channel formats), so this covers
    /// the full <see cref="PixelFormat"/> matrix itself rather than depending on that.
    /// </summary>
    internal static class PixelFormatNormalizer
    {
        /// <remarks>
        /// Takes ownership of <paramref name="source"/>: returns it unchanged when it's already Rgba32,
        /// otherwise disposes it and returns a new converted image (matching
        /// <c>PeachImage.Formats.Jpeg.Decoding.PixelFormatConverter.ConvertIfNeeded</c>'s convention).
        /// </remarks>
        public static Image ToRgba32(Image source)
        {
            if (source.PixelFormat == PixelFormat.Rgba32)
            {
                return source;
            }

            var dest = Image.Create(source.Width, source.Height, PixelFormat.Rgba32);
            var src = source.GetPixelSpan();
            var dst = dest.GetPixelSpan();
            int pixelCount = source.Width * source.Height;

            switch (source.PixelFormat)
            {
                case PixelFormat.Gray8:
                    for (int i = 0, s = 0, d = 0; i < pixelCount; i++, s += 1, d += 4)
                    {
                        byte gray = src[s];
                        dst[d] = gray; dst[d + 1] = gray; dst[d + 2] = gray; dst[d + 3] = 255;
                    }
                    break;

                case PixelFormat.Rgb24:
                    for (int i = 0, s = 0, d = 0; i < pixelCount; i++, s += 3, d += 4)
                    {
                        dst[d] = src[s]; dst[d + 1] = src[s + 1]; dst[d + 2] = src[s + 2]; dst[d + 3] = 255;
                    }
                    break;

                case PixelFormat.Cmyk32:
                    for (int i = 0, s = 0, d = 0; i < pixelCount; i++, s += 4, d += 4)
                    {
                        int c = src[s], m = src[s + 1], y = src[s + 2], k = src[s + 3];
                        dst[d] = (byte)(255 - Math.Min(255, c + k));
                        dst[d + 1] = (byte)(255 - Math.Min(255, m + k));
                        dst[d + 2] = (byte)(255 - Math.Min(255, y + k));
                        dst[d + 3] = 255;
                    }
                    break;

                case PixelFormat.Gray16:
                    DownsampleTo32(src, dst, pixelCount, channels: 1);
                    break;

                case PixelFormat.Rgb48:
                    DownsampleTo32(src, dst, pixelCount, channels: 3);
                    break;

                case PixelFormat.Rgba64:
                    DownsampleTo32(src, dst, pixelCount, channels: 4);
                    break;

                default:
                    throw new NotSupportedException($"Cannot normalize pixel format {source.PixelFormat} to Rgba32.");
            }

            source.Dispose();
            return dest;
        }

        // Shared shape for Gray16/Rgb48/Rgba64 -> Rgba32: downsample `channels` 16-bit source channels
        // per pixel to 8-bit. A 1-channel (Gray16) source replicates into R/G/B; a 4-channel (Rgba64)
        // source carries its own alpha; anything else (Rgb48) is opaque.
        private static void DownsampleTo32(ReadOnlySpan<byte> src, Span<byte> dst, int pixelCount, int channels)
        {
            int srcStride = channels * 2;
            for (int i = 0, s = 0, d = 0; i < pixelCount; i++, s += srcStride, d += 4)
            {
                byte r = DownsampleChannel(src, s);
                dst[d] = r;
                dst[d + 1] = channels == 1 ? r : DownsampleChannel(src, s + 2);
                dst[d + 2] = channels == 1 ? r : DownsampleChannel(src, s + 4);
                dst[d + 3] = channels == 4 ? DownsampleChannel(src, s + 6) : (byte)255;
            }
        }

        // Each 16-bit channel is stored as a native-endian ushort (per PixelFormat's doc comment);
        // BitConverter reads in that same native order, so this is portable regardless of host endianness.
        private static byte DownsampleChannel(ReadOnlySpan<byte> src, int byteOffset) =>
            (byte)(BitConverter.ToUInt16(src.Slice(byteOffset, 2)) >> 8);
    }
}
#endif

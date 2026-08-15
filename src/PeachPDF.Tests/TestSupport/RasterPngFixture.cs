using System;
using System.IO;
#if NET10_0_OR_GREATER
using PeachImage;
#else
using StbImageWriteSharp;
#endif

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Synthesizes small RGBA PNGs for tests that need a real, decodable PNG fixture - a hand-picked
    /// minimal PNG byte array isn't reliably decodable by either raster codec this fork uses
    /// (StbImageSharp on net8.0, PeachImage on net10.0), so writing one with the matching real encoder is.
    /// </summary>
    internal static class RasterPngFixture
    {
        /// <summary>
        /// A real, spec-valid 1x1 opaque red PNG (produced by <see cref="MakeSolidRgbaPngBytes"/> and
        /// captured as a literal), for the handful of call sites - e.g. xunit <c>[InlineData]</c> -
        /// that need a compile-time constant rather than a value computed at test run time. Used as a
        /// stand-in wherever a fixture only needs *some* decodable raster image (most fragmentation/layout
        /// tests exercising a replaced element): GIF previously served this role, but PeachImage - the
        /// net10.0 raster codec - doesn't implement GIF yet, so a PNG stand-in keeps those tests decode
        /// format-agnostic and TFM-neutral instead of exercising the very gap they don't intend to test.
        /// </summary>
        public const string OnePixelDataUri = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==";

        public static byte[] MakeRgbaPngBytes(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixelAt)
        {
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var (r, g, b, a) = pixelAt(x, y);
                    int i = (y * width + x) * 4;
                    pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b; pixels[i + 3] = a;
                }
            }

#if NET10_0_OR_GREATER
            using var image = Image.Create(width, height, PixelFormat.Rgba32);
            pixels.CopyTo(image.GetPixelSpan());
            using var ms = new MemoryStream();
            image.Save(ms, "png");
            return ms.ToArray();
#else
            using var ms = new MemoryStream();
            new ImageWriter().WritePng(pixels, width, height, ColorComponents.RedGreenBlueAlpha, ms);
            return ms.ToArray();
#endif
        }

        public static byte[] MakeSolidRgbaPngBytes(int width, int height, byte r, byte g, byte b, byte a = 255) =>
            MakeRgbaPngBytes(width, height, (_, _) => (r, g, b, a));
    }
}

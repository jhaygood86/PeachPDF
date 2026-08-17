using PeachImage;
using System;
using System.IO;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Synthesizes small RGBA PNGs for tests that need a real, decodable PNG fixture - a hand-picked
    /// minimal PNG byte array isn't reliably decodable by the raster codec this fork uses (PeachImage),
    /// so writing one with the matching real encoder is.
    /// </summary>
    internal static class RasterPngFixture
    {
        /// <summary>
        /// A real, spec-valid 1x1 opaque red PNG (produced by <see cref="MakeSolidRgbaPngBytes"/> and
        /// captured as a literal), for the handful of call sites - e.g. xunit <c>[InlineData]</c> -
        /// that need a compile-time constant rather than a value computed at test run time. Used as a
        /// stand-in wherever a fixture only needs *some* decodable raster image (most fragmentation/layout
        /// tests exercising a replaced element): GIF previously served this role in some of these, but a
        /// PNG stand-in keeps those tests decode-format-agnostic instead of exercising a specific codec.
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

            using var image = Image.Create(width, height, PixelFormat.Rgba32);
            pixels.CopyTo(image.GetPixelSpan());
            using var ms = new MemoryStream();
            image.Save(ms, "png");
            return ms.ToArray();
        }

        public static byte[] MakeSolidRgbaPngBytes(int width, int height, byte r, byte g, byte b, byte a = 255) =>
            MakeRgbaPngBytes(width, height, (_, _) => (r, g, b, a));
    }
}

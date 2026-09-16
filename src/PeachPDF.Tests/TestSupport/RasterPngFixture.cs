using PeachImage;
using PeachImage.Formats.Png;
using System;
using System.IO;
using System.IO.Compression;
using System.Text;

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

        /// <summary>
        /// A real, forced-TruecolorAlpha (color type 6) opaque PNG - same shape as
        /// <c>PeachImageSourceTests.FromBinary_OpaqueTruecolorPng_IsTransparent</c>'s fixture: every pixel
        /// is fully opaque (a=255), but the declared alpha channel still makes <c>Image.HasAlpha</c> (and
        /// so <c>Transparent</c>) true, which - unlike <see cref="MakeSolidRgbaPngBytes"/>'s auto-indexed
        /// equivalent - makes it ineligible for byte-for-byte pass-through regardless of how few distinct
        /// colors it has. For tests that need a plain "some opaque, resizable raster image" fixture
        /// without incidentally exercising the pass-through carve-out (which, by design, is never
        /// resized/downscaled).
        /// </summary>
        public static byte[] MakeOpaqueTruecolorAlphaPngBytes(int width, int height, byte r, byte g, byte b)
        {
            using var image = Image.Create(width, height, PixelFormat.Rgba32);
            var pixels = image.GetPixelSpan();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b; pixels[i + 3] = 255;
            }

            using var ms = new MemoryStream();
            image.Save(ms, "png", new PngEncoderOptions { ColorMode = PngColorMode.Truecolor });
            return ms.ToArray();
        }

        /// <summary>
        /// A real, forced-indexed (PLTE, color type 3) opaque PNG with more than one distinct color -
        /// unlike <see cref="MakeSolidRgbaPngBytes"/>'s auto-indexed uniform fixture, this needs
        /// <see cref="PngColorMode.Indexed"/> explicitly since a >1-color source wouldn't necessarily
        /// auto-index under <see cref="PngColorMode.Auto"/>'s own distinct-color threshold logic.
        /// </summary>
        public static byte[] MakeIndexedPngBytes(int width, int height, Func<int, int, (byte R, byte G, byte B)> pixelAt)
        {
            using var image = Image.Create(width, height, PixelFormat.Rgb24);
            var pixels = image.GetPixelSpan();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var (r, g, b) = pixelAt(x, y);
                    int i = (y * width + x) * 3;
                    pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b;
                }
            }

            using var ms = new MemoryStream();
            image.Save(ms, "png", new PngEncoderOptions { ColorMode = PngColorMode.Indexed });
            return ms.ToArray();
        }

        /// <summary>
        /// A real, opaque (no alpha channel, no <c>tRNS</c>), plain truecolor (color type 2) PNG - the
        /// same pixel shape whether or not <paramref name="interlace"/> is set, so a test can hold
        /// everything else about the fixture constant while isolating <c>IsInterlaced</c> as the sole
        /// difference between two otherwise-identical sources.
        /// </summary>
        public static byte[] MakeInterlacedPngBytes(int width, int height, byte r, byte g, byte b, bool interlace = true, (byte R, byte G, byte B)? transparentColor = null)
        {
            using var image = Image.Create(width, height, PixelFormat.Rgb24);
            var pixels = image.GetPixelSpan();
            for (int i = 0; i < pixels.Length; i += 3)
            {
                pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b;
            }

            using var ms = new MemoryStream();
            image.Save(ms, "png", new PngEncoderOptions { Interlace = interlace, ColorMode = PngColorMode.Truecolor, TransparentColor = transparentColor });
            return ms.ToArray();
        }

        /// <summary>
        /// A real truecolor PNG with a <c>tRNS</c> color-key chunk (no per-pixel alpha channel) - a
        /// Truecolor <c>tRNS</c> is always a single exact chroma-key value, so this is eligible for
        /// byte-for-byte pass-through (with a PDF <c>/Mask</c> color-key array built from
        /// <paramref name="transparentColor"/>), not disqualified by <c>HasTrns</c> the way a real alpha
        /// channel is.
        /// </summary>
        public static byte[] MakeTrnsPngBytes(int width, int height, byte r, byte g, byte b, (byte R, byte G, byte B) transparentColor)
        {
            using var image = Image.Create(width, height, PixelFormat.Rgb24);
            var pixels = image.GetPixelSpan();
            for (int i = 0; i < pixels.Length; i += 3)
            {
                pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b;
            }

            using var ms = new MemoryStream();
            image.Save(ms, "png", new PngEncoderOptions { ColorMode = PngColorMode.Truecolor, TransparentColor = transparentColor });
            return ms.ToArray();
        }

        /// <summary>
        /// Same shape as <see cref="MakeTrnsPngBytes"/>, but with the left half of the image painted the
        /// transparent key color instead of every pixel being the opaque fill - a plain-embed pass-through
        /// test only needs the mask array's declared values (built straight from the <c>tRNS</c> chunk
        /// bytes, independent of actual pixel content), but a test that forces a full decode (e.g. a
        /// resize under <see cref="PeachPDF.ImageCompression.Lossless"/>) needs at least one real pixel of
        /// the key color, or there's nothing for the decoder to actually find transparent.
        /// </summary>
        public static byte[] MakeTrnsPngBytesWithTransparentRegion(int width, int height, byte r, byte g, byte b, (byte R, byte G, byte B) transparentColor)
        {
            using var image = Image.Create(width, height, PixelFormat.Rgb24);
            var pixels = image.GetPixelSpan();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var (pr, pg, pb) = x < width / 2 ? transparentColor : (r, g, b);
                    int i = (y * width + x) * 3;
                    pixels[i] = pr; pixels[i + 1] = pg; pixels[i + 2] = pb;
                }
            }

            using var ms = new MemoryStream();
            image.Save(ms, "png", new PngEncoderOptions { ColorMode = PngColorMode.Truecolor, TransparentColor = transparentColor });
            return ms.ToArray();
        }

        /// <summary>
        /// A real Grayscale (color type 0) PNG with a <c>tRNS</c> chroma-key chunk. PeachImage's own
        /// encoder (<c>PngImageEncoder.WriteTrns</c>) uses just the tuple's <c>R</c> component as the gray
        /// key for a <see cref="PixelFormat.Gray8"/> source - confirmed by reading its source - so
        /// <paramref name="transparentGray"/> alone is enough here, unlike the RGB triple
        /// <see cref="MakeTrnsPngBytes"/> needs for a truecolor source.
        /// </summary>
        public static byte[] MakeGrayscaleTrnsPngBytes(int width, int height, byte gray, byte transparentGray)
        {
            using var image = Image.Create(width, height, PixelFormat.Gray8);
            image.GetPixelSpan().Fill(gray);

            using var ms = new MemoryStream();
            image.Save(ms, "png", new PngEncoderOptions { TransparentColor = (transparentGray, 0, 0) });
            return ms.ToArray();
        }

        /// <summary>
        /// A hand-built, minimal but spec-valid indexed (color type 3) PNG carrying a per-palette-entry
        /// <c>tRNS</c> chunk. PeachImage's own encoder can't produce this combination -
        /// <see cref="PngEncoderOptions.TransparentColor"/>'s own doc comment says it explicitly disables
        /// indexed encoding when set - so this assembles the PNG chunks directly instead: signature,
        /// IHDR (color type 3, 8-bit), PLTE, tRNS, one zlib-compressed (filter-type-0 scanlines, one byte
        /// per pixel) IDAT, IEND - each chunk length-prefixed and CRC32-suffixed per the PNG spec. Same
        /// "hand-build a minimal real file since the encoder can't produce this shape" precedent as
        /// <c>ShowcaseCmykTiffFixture</c>'s TIFF builder.
        /// </summary>
        public static byte[] MakeIndexedPngBytesWithTrns(
            int width, int height, (byte R, byte G, byte B)[] palette, byte[] paletteAlphas, Func<int, int, byte> indexAt)
        {
            using var ms = new MemoryStream();
            ms.Write([137, 80, 78, 71, 13, 10, 26, 10]);

            var ihdr = new byte[13];
            WriteUInt32BigEndian(ihdr, 0, (uint)width);
            WriteUInt32BigEndian(ihdr, 4, (uint)height);
            ihdr[8] = 8;  // bit depth
            ihdr[9] = 3;  // color type: palette
            ihdr[10] = 0; // compression method
            ihdr[11] = 0; // filter method
            ihdr[12] = 0; // interlace method
            WriteChunk(ms, "IHDR", ihdr);

            var plte = new byte[palette.Length * 3];
            for (int i = 0; i < palette.Length; i++)
            {
                plte[i * 3] = palette[i].R;
                plte[i * 3 + 1] = palette[i].G;
                plte[i * 3 + 2] = palette[i].B;
            }
            WriteChunk(ms, "PLTE", plte);

            WriteChunk(ms, "tRNS", paletteAlphas);

            var raw = new byte[height * (1 + width)];
            int offset = 0;
            for (int y = 0; y < height; y++)
            {
                raw[offset++] = 0; // filter type: None
                for (int x = 0; x < width; x++)
                {
                    raw[offset++] = indexAt(x, y);
                }
            }

            using var idatMs = new MemoryStream();
            using (var zlib = new ZLibStream(idatMs, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(raw);
            }
            WriteChunk(ms, "IDAT", idatMs.ToArray());

            WriteChunk(ms, "IEND", []);

            return ms.ToArray();
        }

        private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            var lengthBytes = new byte[4];
            WriteUInt32BigEndian(lengthBytes, 0, (uint)data.Length);
            stream.Write(lengthBytes);

            var typeBytes = Encoding.ASCII.GetBytes(type);
            stream.Write(typeBytes);
            stream.Write(data);

            var crcInput = new byte[typeBytes.Length + data.Length];
            typeBytes.CopyTo(crcInput, 0);
            data.CopyTo(crcInput, typeBytes.Length);

            var crcBytes = new byte[4];
            WriteUInt32BigEndian(crcBytes, 0, Crc32(crcInput));
            stream.Write(crcBytes);
        }

        // Standard PNG/zlib CRC-32 (polynomial 0xEDB88320, reflected) - hand-rolled here rather than
        // pulling in System.IO.Hashing for one test-fixture helper.
        private static readonly uint[] Crc32Table = BuildCrc32Table();

        private static uint[] BuildCrc32Table()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                }
                table[n] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (var b in data)
            {
                crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }
            return crc ^ 0xFFFFFFFF;
        }
    }
}

#if NET10_0_OR_GREATER
using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using PeachImage;
using PeachImage.Formats.Bmp;
using PeachImage.Formats.Jpeg;
using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Tests.TestSupport;
using System.IO;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    /// <summary>
    /// net10.0-only counterpart to <c>StbImageSharpImageSourceTests</c> (net8.0-only), covering
    /// <see cref="PeachImageSource"/> - the PeachImage-backed <c>ImageSource</c> implementation used
    /// on net10.0 instead of StbImageSharp/StbImageWriteSharp. Builds JPEG/BMP/PNG fixtures with
    /// PeachImage's own encoders (a hand-picked minimal file isn't reliably decodable by any real
    /// codec - see <c>RasterPngFixture</c>) and, beyond the header/magic-byte checks
    /// <c>StbImageSharpImageSourceTests</c> does, decodes the encoded output back through
    /// <see cref="Image.Load(System.IO.Stream, DecoderOptions?)"/> and asserts on actual pixel colors -
    /// per this repo's testing convention, a passing header check alone isn't proof a raster pipeline
    /// round-trips real pixel data correctly.
    /// </summary>
    public class PeachImageSourceTests : IDisposable
    {
        private readonly ImageSource _source;
        private readonly List<string> _tempFiles = [];

        public PeachImageSourceTests()
        {
            _source = new PeachImageSource();
            ImageSource.ImageSourceImpl = _source;
        }

        public void Dispose()
        {
            foreach (var f in _tempFiles)
                if (File.Exists(f)) File.Delete(f);
        }

        // --- fixture helpers ---

        private static byte[] MakePngBytes(int width, int height, byte r, byte g, byte b, byte a = 255) =>
            RasterPngFixture.MakeSolidRgbaPngBytes(width, height, r, g, b, a);

        private static byte[] MakeJpegBytes(int width, int height, byte r, byte g, byte b)
        {
            using var image = MakeSolidImage(width, height, r, g, b, 255);
            using var ms = new MemoryStream();
            image.Save(ms, "jpeg", new JpegEncoderOptions { Quality = 90 });
            return ms.ToArray();
        }

        private static byte[] MakeBmpBytes(int width, int height, byte r, byte g, byte b, byte a = 255)
        {
            using var image = MakeSolidImage(width, height, r, g, b, a);
            using var ms = new MemoryStream();
            image.Save(ms, "bmp", new BmpEncoderOptions());
            return ms.ToArray();
        }

        private static Image MakeSolidImage(int width, int height, byte r, byte g, byte b, byte a)
        {
            var image = Image.Create(width, height, PixelFormat.Rgba32);
            var pixels = image.GetPixelSpan();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b; pixels[i + 3] = a;
            }
            return image;
        }

        private string WriteTempFile(byte[] bytes, string extension)
        {
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{extension}");
            File.WriteAllBytes(path, bytes);
            _tempFiles.Add(path);
            return path;
        }

        // Reads the first pixel's R/G/B - the leading 3 bytes at the same offsets whether the decoder
        // hands back Rgb24 or Rgba32, which is all a solid-color fixture needs to verify.
        private static void AssertApproxColor(byte expectedR, byte expectedG, byte expectedB, byte[] roundTripBytes, int tolerance = 0)
        {
            using var decoded = Image.Load(new MemoryStream(roundTripBytes));
            var span = decoded.GetPixelSpan();
            AssertClose(expectedR, span[0], tolerance);
            AssertClose(expectedG, span[1], tolerance);
            AssertClose(expectedB, span[2], tolerance);
        }

        private static void AssertClose(byte expected, byte actual, int tolerance) =>
            Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected}, got {actual} (tolerance {tolerance}).");

        // --- FromBinary: dimensions / Transparent heuristic ---

        [Fact]
        public void FromBinary_Png_IsTransparent()
        {
            var bytes = MakePngBytes(4, 4, 255, 0, 0);
            var img = ImageSource.FromBinary("test.png", () => bytes);

            Assert.True(img.Transparent);
        }

        [Fact]
        public void FromBinary_Jpeg_IsNotTransparent()
        {
            var bytes = MakeJpegBytes(4, 4, 255, 0, 0);
            var img = ImageSource.FromBinary("test.jpg", () => bytes);

            Assert.False(img.Transparent);
        }

        [Fact]
        public void FromBinary_Bmp_IsNotTransparent()
        {
            var bytes = MakeBmpBytes(4, 4, 255, 0, 0);
            var img = ImageSource.FromBinary("test.bmp", () => bytes);

            Assert.False(img.Transparent);
        }

        [Fact]
        public void FromBinary_ReturnsCorrectDimensions()
        {
            var bytes = MakePngBytes(7, 13, 0, 255, 0);
            var img = ImageSource.FromBinary("test.png", () => bytes);

            Assert.Equal(7, img.Width);
            Assert.Equal(13, img.Height);
        }

        [Fact]
        public void FromBinary_InvalidData_ThrowsException()
        {
            var garbage = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

            Assert.ThrowsAny<Exception>(() => ImageSource.FromBinary("bad", () => garbage));
        }

        [Fact]
        public void FromBinary_Gif_DecodesCorrectly()
        {
            // A real, valid 1x1, fully transparent GIF (a graphic control extension with the
            // transparency flag set, transparent color index 0). PeachImage added GIF decode support
            // in 0.1.2 - this pins that it actually works end-to-end through PeachImageSource, not
            // just that the package reference resolves.
            var gifBytes = Convert.FromBase64String(
                "R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==");

            var img = ImageSource.FromBinary("pixel.gif", () => gifBytes);

            Assert.Equal(1, img.Width);
            Assert.Equal(1, img.Height);
        }

        [Fact]
        public void SaveAsPdfBitmap_RoundTripsGifTransparencyExactly()
        {
            var gifBytes = Convert.FromBase64String(
                "R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==");
            var img = ImageSource.FromBinary("pixel.gif", () => gifBytes);
            var ms = new MemoryStream();
            img.SaveAsPdfBitmap(ms);

            using var decoded = Image.Load(new MemoryStream(ms.ToArray()));
            var pixel = decoded.GetPixelSpan();

            Assert.Equal(PixelFormat.Rgba32, decoded.PixelFormat);
            Assert.Equal(0, pixel[3]);
        }

        // --- FromFile ---

        [Fact]
        public void FromFile_Png_LoadsCorrectly()
        {
            var path = WriteTempFile(MakePngBytes(3, 5, 0, 0, 255), ".png");
            var img = ImageSource.FromFile(path);

            Assert.Equal(3, img.Width);
            Assert.Equal(5, img.Height);
            Assert.True(img.Transparent);
        }

        [Fact]
        public void FromFile_Jpeg_LoadsCorrectly()
        {
            var path = WriteTempFile(MakeJpegBytes(6, 8, 255, 255, 0), ".jpg");
            var img = ImageSource.FromFile(path);

            Assert.Equal(6, img.Width);
            Assert.Equal(8, img.Height);
            Assert.False(img.Transparent);
        }

        [Fact]
        public void FromFile_Bmp_LoadsCorrectly()
        {
            var path = WriteTempFile(MakeBmpBytes(6, 8, 128, 64, 32), ".bmp");
            var img = ImageSource.FromFile(path);

            Assert.Equal(6, img.Width);
            Assert.Equal(8, img.Height);
            Assert.False(img.Transparent);
        }

        // --- FromStream ---

        [Fact]
        public void FromStream_Png_LoadsCorrectly()
        {
            var bytes = MakePngBytes(2, 2, 10, 20, 30);
            var img = ImageSource.FromStream("test.png", () => new MemoryStream(bytes));

            Assert.Equal(2, img.Width);
            Assert.Equal(2, img.Height);
            Assert.True(img.Transparent);
        }

        [Fact]
        public void FromStream_Jpeg_LoadsCorrectly()
        {
            var bytes = MakeJpegBytes(4, 4, 40, 50, 60);
            var img = ImageSource.FromStream("test.jpg", () => new MemoryStream(bytes));

            Assert.Equal(4, img.Width);
            Assert.False(img.Transparent);
        }

        [Fact]
        public void FromStream_Bmp_LoadsCorrectly()
        {
            var bytes = MakeBmpBytes(4, 4, 70, 80, 90);
            var img = ImageSource.FromStream("test.bmp", () => new MemoryStream(bytes));

            Assert.Equal(4, img.Width);
            Assert.False(img.Transparent);
        }

        // --- Output encoding: format validity ---

        [Fact]
        public void SaveAsJpeg_ProducesValidJpegBytes()
        {
            var img = ImageSource.FromBinary("test.png", () => MakePngBytes(4, 4, 255, 0, 0));
            var ms = new MemoryStream();
            img.SaveAsJpeg(ms);
            var result = ms.ToArray();

            // JPEG SOI marker
            Assert.True(result.Length > 2);
            Assert.Equal(0xFF, result[0]);
            Assert.Equal(0xD8, result[1]);
        }

        [Fact]
        public void SaveAsPdfBitmap_ProducesValidBmpBytes()
        {
            var img = ImageSource.FromBinary("test.png", () => MakePngBytes(4, 4, 255, 0, 0));
            var ms = new MemoryStream();
            img.SaveAsPdfBitmap(ms);
            var result = ms.ToArray();

            // BMP magic bytes "BM"
            Assert.True(result.Length > 2);
            Assert.Equal((byte)'B', result[0]);
            Assert.Equal((byte)'M', result[1]);
        }

        [Fact]
        public void SaveAsJpeg_PreservesApproximateDimensions()
        {
            var img = ImageSource.FromBinary("src.png", () => MakePngBytes(10, 12, 0, 128, 255));
            var ms = new MemoryStream();
            img.SaveAsJpeg(ms);

            ms.Position = 0;
            var reloaded = ImageSource.FromStream("out.jpg", () => new MemoryStream(ms.ToArray()));

            Assert.Equal(10, reloaded.Width);
            Assert.Equal(12, reloaded.Height);
        }

        // --- Output encoding: actual pixel-data correctness, not just header bytes ---
        // (this repo's testing convention: a header/magic-byte match is not proof the raster pipeline
        // round-trips real pixel data correctly - see CLAUDE.md's "Testing conventions".)

        [Fact]
        public void SaveAsPdfBitmap_RoundTripsExactPixelColorsFromPng()
        {
            var img = ImageSource.FromBinary("test.png", () => MakePngBytes(4, 4, 12, 200, 77));
            var ms = new MemoryStream();
            img.SaveAsPdfBitmap(ms);

            // BMP is lossless - the round-tripped color must match exactly.
            AssertApproxColor(12, 200, 77, ms.ToArray());
        }

        [Fact]
        public void SaveAsPdfBitmap_RoundTripsPartialAlphaExactly()
        {
            // Every other fixture in this file uses a=255 (opaque). The BMP embedding path exists
            // specifically to carry a PDF SMask's alpha channel through (see PdfImage.
            // ReadTrueColorMemoryBitmap, which reads byte offset+3 of each pixel as alpha) - a fixture
            // that's always fully opaque would never actually exercise that.
            var img = ImageSource.FromBinary("test.png", () => MakePngBytes(4, 4, 12, 200, 77, a: 128));
            var ms = new MemoryStream();
            img.SaveAsPdfBitmap(ms);

            using var decoded = Image.Load(new MemoryStream(ms.ToArray()));
            var pixel = decoded.GetPixelSpan();

            Assert.Equal(PixelFormat.Rgba32, decoded.PixelFormat);
            Assert.Equal(12, pixel[0]);
            Assert.Equal(200, pixel[1]);
            Assert.Equal(77, pixel[2]);
            Assert.Equal(128, pixel[3]);
        }

        [Fact]
        public void SaveAsPdfBitmap_RoundTripsExactPixelColorsFromBmp()
        {
            var img = ImageSource.FromBinary("test.bmp", () => MakeBmpBytes(4, 4, 220, 30, 90));
            var ms = new MemoryStream();
            img.SaveAsPdfBitmap(ms);

            AssertApproxColor(220, 30, 90, ms.ToArray());
        }

        [Fact]
        public void SaveAsJpeg_RoundTripsApproximatePixelColors()
        {
            var img = ImageSource.FromBinary("test.png", () => MakePngBytes(8, 8, 200, 60, 10));
            var ms = new MemoryStream();
            img.SaveAsJpeg(ms);

            // JPEG is lossy (quality 90 here) - allow a generous tolerance for quantization/chroma error.
            AssertApproxColor(200, 60, 10, ms.ToArray(), tolerance: 20);
        }

        [Fact]
        public void FromBinary_ExposesTheGivenName()
        {
            var img = ImageSource.FromBinary("my-image.png", () => MakePngBytes(1, 1, 0, 0, 0));

            Assert.Equal("my-image.png", img.Name);
        }
    }
}
#endif

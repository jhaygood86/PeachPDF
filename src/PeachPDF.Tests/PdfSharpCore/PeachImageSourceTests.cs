using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using PeachImage;
using PeachImage.Formats.Bmp;
using PeachImage.Formats.Jpeg;
using PeachImage.Formats.Webp;
using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Tests.TestSupport;
using System.IO;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    /// <summary>
    /// Covers <see cref="PeachImageSource"/> - the PeachImage-backed <c>ImageSource</c> implementation
    /// used on both target frameworks - across every format PeachImage can decode (JPEG, PNG, BMP, GIF,
    /// WebP, AVIF; see <c>PeachImage.Image.Codecs</c>). JPEG/BMP/PNG/GIF fixtures are built with
    /// PeachImage's own encoders (a hand-picked minimal file isn't reliably decodable by any real codec
    /// - see <c>RasterPngFixture</c>); WebP/AVIF have no such encoder available (AVIF has none in
    /// PeachImage at all) so those fixtures are small real files instead (see
    /// <see cref="WebpTestImageBase64"/>/<see cref="AvifTestImageBase64"/>). Beyond header/magic-byte
    /// checks, every format's test decodes the encoded/fixture output back through
    /// <see cref="Image.Load(System.IO.Stream, DecoderOptions?)"/> and asserts on actual pixel colors -
    /// per this repo's testing convention, a passing header check alone isn't proof a raster pipeline
    /// round-trips real pixel data correctly. <see cref="PeachImageSource"/> itself no longer normalizes
    /// pixel formats (PeachImage 0.2.1+ guarantees <c>TargetPixelFormat = Rgba32</c> succeeds in one hop
    /// for every native format its decoders produce), so the 16-bit-per-channel/Cmyk32 coverage this
    /// file used to delegate to a separate <c>PixelFormatNormalizerTests</c> now lives upstream in
    /// PeachImage's own test suite - <see cref="FromBinary_Png16Bit_RoundTripsExactly"/> below is this
    /// repo's own spot-check that the guarantee actually holds through PeachPDF's real pipeline, not a
    /// full re-test of PeachImage's conversion matrix.
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

        // Real, small (32x24) WebP/AVIF files, copied byte-for-byte from PeachImage's own repo
        // (bench/PeachImage.Benchmarks/Assets/small_32x24_lossless.webp and small_32x24.avif). Unlike
        // JPEG/BMP/PNG, PeachImage has no AVIF encoder at all (decode-only) and a hand-constructed
        // minimal WebP isn't reliably valid either (RIFF container + VP8/VP8L bitstream), so reusing a
        // known-good real file is the only practical way to fixture these two formats.
        private const string WebpTestImageBase64 = "UklGRpoAAABXRUJQVlA4TI4AAAAvH8AFAM10IaL/AUZtI0nye/kTnrtqEQgJEiP/x5rNtG3TduYPeFcwcNu2Ufdu76RXKAAAAAAAAAAAAAAA4Pv7MBptMQfLzlrVDW9HVEuOy4pt57X5QdcfGP73G4GUy4tBwu0JKeBaUkqeEby0BT3+RGjC1atYhcvfzl05aTQ27bwr3UofTmlbIVsl/3Aa";
        private const string AvifTestImageBase64 = "AAAAIGZ0eXBhdmlmAAAAAGF2aWZtaWYxbWlhZk1BMUIAAAD5bWV0YQAAAAAAAAAvaGRscgAAAAAAAAAAcGljdAAAAAAAAAAAAAAAAFBpY3R1cmVIYW5kbGVyAAAAAA5waXRtAAAAAAABAAAAHmlsb2MAAAAARAAAAQABAAAAAQAAASEAAAA5AAAAKGlpbmYAAAAAAAEAAAAaaW5mZQIAAAAAAQAAYXYwMUNvbG9yAAAAAGppcHJwAAAAS2lwY28AAAAUaXNwZQAAAAAAAAAgAAAAGAAAABBwaXhpAAAAAAMICAgAAAAMYXYxQ4EADAAAAAATY29scm5jbHgAAQANAAEAAAAAF2lwbWEAAAAAAAAAAQABBAECgwQAAABBbWRhdAoJGBE/dogIaAggMiwWAAEEAQSAXQC00ChJHYsFdRuDLZqybjk69Sd222v0QwL3wUXfjW/9Ioqk4Q==";

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
        public void FromBinary_OpaquePng_IsNotTransparent()
        {
            // Transparent reflects real alpha content (a full pixel scan), not source format - an
            // opaque PNG (a=255 everywhere) takes the lossy JPEG embed path exactly like an opaque
            // JPEG/BMP/WebP/AVIF source would, since there's no alpha channel to lose.
            var bytes = MakePngBytes(4, 4, 255, 0, 0);
            var img = ImageSource.FromBinary("test.png", () => bytes);

            Assert.False(img.Transparent);
        }

        [Fact]
        public void FromBinary_PngWithRealAlpha_IsTransparent()
        {
            var bytes = MakePngBytes(4, 4, 255, 0, 0, a: 128);
            var img = ImageSource.FromBinary("test.png", () => bytes);

            Assert.True(img.Transparent);
        }

        [Fact]
        public void FromBinary_GifWithTransparency_IsTransparent()
        {
            // Previously (PNG-magic-byte sniff): this GIF would have been mis-routed to the lossy JPEG
            // path, silently dropping its transparency, since only a PNG signature counted. A real
            // pixel-alpha scan catches it regardless of source format.
            var gifBytes = Convert.FromBase64String(
                "R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==");

            var img = ImageSource.FromBinary("pixel.gif", () => gifBytes);

            Assert.True(img.Transparent);
        }

        [Fact]
        public void FromBinary_WebpWithRealAlpha_IsTransparent()
        {
            // Same bug as the GIF case above, for WebP: a PNG-magic-byte sniff would never flag this as
            // transparent no matter how much real alpha it carries. Built with PeachImage's own lossless
            // (VP8L) encoder, which preserves alpha, rather than a fixture file (unlike the decode-only
            // WebpTestImageBase64 fixture used elsewhere in this file).
            using var image = MakeSolidImage(4, 4, 10, 20, 30, a: 128);
            using var ms = new MemoryStream();
            image.Save(ms, "webp", new WebpEncoderOptions());

            var img = ImageSource.FromBinary("test.webp", () => ms.ToArray());

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

        [Fact]
        public void FromBinary_Webp_DecodesCorrectly()
        {
            var webpBytes = Convert.FromBase64String(WebpTestImageBase64);

            var img = ImageSource.FromBinary("test.webp", () => webpBytes);

            Assert.Equal(32, img.Width);
            Assert.Equal(24, img.Height);
            Assert.False(img.Transparent);
        }

        [Fact]
        public void SaveAsPdfBitmap_RoundTripsWebpPixelColorsExactly()
        {
            var webpBytes = Convert.FromBase64String(WebpTestImageBase64);
            var img = ImageSource.FromBinary("test.webp", () => webpBytes);
            var ms = new MemoryStream();
            img.SaveAsPdfBitmap(ms);

            using var decoded = Image.Load(new MemoryStream(ms.ToArray()));
            var pixel = decoded.GetPixelSpan();

            // The fixture's top-left pixel is pure black - verified once against PeachImage 0.2.0
            // directly. BMP is lossless, so the round-tripped value must match exactly regardless of
            // whether the source WebP bitstream itself was lossy.
            Assert.Equal(0, pixel[0]);
            Assert.Equal(0, pixel[1]);
            Assert.Equal(0, pixel[2]);
        }

        [Fact]
        public void FromBinary_Avif_DecodesCorrectly()
        {
            var avifBytes = Convert.FromBase64String(AvifTestImageBase64);

            var img = ImageSource.FromBinary("test.avif", () => avifBytes);

            Assert.Equal(32, img.Width);
            Assert.Equal(24, img.Height);
            Assert.False(img.Transparent);
        }

        [Fact]
        public void SaveAsPdfBitmap_RoundTripsAvifPixelColorsExactly()
        {
            var avifBytes = Convert.FromBase64String(AvifTestImageBase64);
            var img = ImageSource.FromBinary("test.avif", () => avifBytes);
            var ms = new MemoryStream();
            img.SaveAsPdfBitmap(ms);

            using var decoded = Image.Load(new MemoryStream(ms.ToArray()));
            var pixel = decoded.GetPixelSpan();

            // The fixture's top-left pixel is (3,3,3) - verified once against PeachImage 0.2.0 directly.
            Assert.Equal(3, pixel[0]);
            Assert.Equal(3, pixel[1]);
            Assert.Equal(3, pixel[2]);
        }

        [Fact]
        public void FromBinary_Png16Bit_RoundTripsExactly()
        {
            // PeachImageSource no longer has its own pixel-format normalizer (PeachImage 0.2.1 handles
            // every native PixelFormat -> Rgba32 conversion itself, including 16-bit-per-channel PNGs -
            // see the class doc comment above). This is PeachPDF's own spot-check that requesting
            // Rgba32 through ImageSource.FromBinary actually gets a correctly-downsampled result
            // end-to-end, rather than just trusting PeachImage's upstream completeness tests.
            using var source = Image.Create(1, 1, PixelFormat.Rgb48);
            var src = source.GetPixelSpan();
            BitConverter.GetBytes((ushort)(10 * 256)).CopyTo(src);
            BitConverter.GetBytes((ushort)(20 * 256)).CopyTo(src[2..]);
            BitConverter.GetBytes((ushort)(30 * 256)).CopyTo(src[4..]);
            using var pngMs = new MemoryStream();
            source.Save(pngMs, "png");
            var pngBytes = pngMs.ToArray();

            var img = ImageSource.FromBinary("16bit.png", () => pngBytes);
            var bmpMs = new MemoryStream();
            img.SaveAsPdfBitmap(bmpMs);

            using var decoded = Image.Load(new MemoryStream(bmpMs.ToArray()));
            var pixel = decoded.GetPixelSpan();

            Assert.Equal(PixelFormat.Rgba32, decoded.PixelFormat);
            Assert.Equal(10, pixel[0]);
            Assert.Equal(20, pixel[1]);
            Assert.Equal(30, pixel[2]);
            Assert.Equal(255, pixel[3]);
        }

        // --- FromFile ---

        [Fact]
        public void FromFile_Png_LoadsCorrectly()
        {
            var path = WriteTempFile(MakePngBytes(3, 5, 0, 0, 255), ".png");
            var img = ImageSource.FromFile(path);

            Assert.Equal(3, img.Width);
            Assert.Equal(5, img.Height);
            Assert.False(img.Transparent);
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
            Assert.False(img.Transparent);
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

        [Fact]
        public void SaveAsJpeg_WithTargetSize_ResizesOutput()
        {
            var img = ImageSource.FromBinary("src.png", () => MakePngBytes(40, 30, 0, 128, 255));
            var ms = new MemoryStream();
            img.SaveAsJpeg(ms, targetWidth: 10, targetHeight: 8);

            ms.Position = 0;
            var reloaded = ImageSource.FromStream("out.jpg", () => new MemoryStream(ms.ToArray()));

            Assert.Equal(10, reloaded.Width);
            Assert.Equal(8, reloaded.Height);
        }

        [Fact]
        public void SaveAsPdfBitmap_WithTargetSize_ResizesOutput()
        {
            var img = ImageSource.FromBinary("src.png", () => MakePngBytes(40, 30, 0, 128, 255, a: 128));
            var ms = new MemoryStream();
            img.SaveAsPdfBitmap(ms, targetWidth: 10, targetHeight: 8);

            using var decoded = Image.Load(new MemoryStream(ms.ToArray()));

            Assert.Equal(10, decoded.Width);
            Assert.Equal(8, decoded.Height);
        }

        [Fact]
        public void SaveAsJpeg_WithMatchingTargetSize_DoesNotResize()
        {
            // targetWidth/targetHeight equal to the natural size must take the cheap no-resize path -
            // this is what PdfImageTable relies on to skip PeachImage.Resize entirely when a caller
            // (e.g. DownscaleImages = false, or a display size that's already smaller than the source)
            // determined no actual shrink is needed.
            var img = ImageSource.FromBinary("src.png", () => MakePngBytes(10, 8, 0, 128, 255));
            var ms = new MemoryStream();
            img.SaveAsJpeg(ms, targetWidth: 10, targetHeight: 8);

            ms.Position = 0;
            var reloaded = ImageSource.FromStream("out.jpg", () => new MemoryStream(ms.ToArray()));

            Assert.Equal(10, reloaded.Width);
            Assert.Equal(8, reloaded.Height);
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

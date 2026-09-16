using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using PeachImage;
using PeachImage.Formats.Avif;
using PeachImage.Formats.Bmp;
using PeachImage.Formats.Jpeg;
using PeachImage.Formats.Png;
using PeachImage.Formats.Webp;
using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Tests.TestSupport;
using System.IO;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    /// <summary>
    /// Covers <see cref="PeachImageSource"/> - the PeachImage-backed <c>ImageSource</c> implementation
    /// used on both target frameworks - across every format PeachImage can decode (JPEG, PNG, BMP, GIF,
    /// WebP, AVIF; see <c>PeachImage.Image.Codecs</c>). JPEG/BMP/PNG/GIF/WebP fixtures are built with
    /// PeachImage's own encoders (a hand-picked minimal file isn't reliably decodable by any real codec
    /// - see <c>RasterPngFixture</c>); the decode/round-trip tests below still use the small real
    /// <see cref="AvifTestImageBase64"/> fixture file for AVIF rather than PeachImage's own AVIF
    /// encoder (added in PeachImage 0.4.6, alongside <see cref="AvifEncoderOptions"/>, and used only by
    /// the <c>IsLosslessSourceFormat</c> tests below, which need to control lossless-vs-lossy encoding
    /// directly). Beyond header/magic-byte
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

        // Rgb24, not the Rgba32 MakeSolidImage below builds - BmpEncoder always writes an explicit
        // alpha mask for an Rgba32 source (no opaque-content auto-downgrade like PNG's indexed mode
        // has), so routing through MakeSolidImage would make every "opaque BMP" fixture declare an
        // alpha channel it doesn't need, which Image.HasAlpha (unlike the old per-pixel scan) now takes
        // at face value. Every caller here wants a genuinely non-alpha-declaring opaque BMP.
        private static byte[] MakeBmpBytes(int width, int height, byte r, byte g, byte b)
        {
            using var image = Image.Create(width, height, PixelFormat.Rgb24);
            var pixels = image.GetPixelSpan();
            for (int i = 0; i < pixels.Length; i += 3)
            {
                pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b;
            }

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
            // Transparent reflects Image.HasAlpha - whether the source format declares an alpha
            // channel, not a per-pixel scan. This fixture is small and uniform-color, so PeachImage's
            // encoder auto-picks indexed color (no alpha chunk) for it; it takes the lossy JPEG embed
            // path exactly like an opaque JPEG/BMP/WebP/AVIF source would.
            var bytes = MakePngBytes(4, 4, 255, 0, 0);
            var img = ImageSource.FromBinary("test.png", () => bytes);

            Assert.False(img.Transparent);
        }

        [Fact]
        public void FromBinary_OpaqueTruecolorAlphaPng_IsAlphaSplitEligible_NotTransparent()
        {
            // A PNG explicitly encoded with a real alpha channel (color type 6) is alpha-split-eligible
            // (issue #1109) regardless of whether any pixel is actually translucent - PngAlphaSplit
            // doesn't inspect pixel values, only structural eligibility (color type/bit depth/interlace).
            // Transparent is false for it, same as the existing opaque/chroma-key pass-through precedent
            // (PeachPngPassthroughImageSourceImpl) - alpha is carried via PngPassthroughData.AlphaIdatData
            // and embedded as a child /SMask directly, not via the Transparent-driven XImageFormat.Png
            // route. See FromBinary_InterlacedAlphaPng_IsNotAlphaSplitEligible_StillTransparent below for
            // the still-Transparent=true fallback case.
            using var image = MakeSolidImage(4, 4, 255, 0, 0, a: 255);
            using var ms = new MemoryStream();
            image.Save(ms, "png", new PngEncoderOptions { ColorMode = PngColorMode.Truecolor });

            var img = ImageSource.FromBinary("test.png", () => ms.ToArray());

            Assert.False(img.Transparent);
            Assert.NotNull(img.PngPassthrough);
            Assert.NotNull(img.PngPassthrough!.Value.AlphaIdatData);
        }

        [Fact]
        public void FromBinary_InterlacedAlphaPng_IsNotAlphaSplitEligible_StillTransparent()
        {
            // PngAlphaSplit excludes interlaced sources (Adam7 sub-image data can't be de-interleaved by
            // its simple row loop) - falls back to the existing full decode+SMask path, where Transparent
            // still correctly reflects Image.HasAlpha (the source's declared alpha channel, not a
            // per-pixel scan - see this class's own remarks on that heuristic).
            using var image = MakeSolidImage(4, 4, 255, 0, 0, a: 128);
            using var ms = new MemoryStream();
            image.Save(ms, "png", new PngEncoderOptions { ColorMode = PngColorMode.Truecolor, Interlace = true });
            var bytes = ms.ToArray();

            var img = ImageSource.FromBinary("test.png", () => bytes);

            Assert.Null(img.PngPassthrough);
            Assert.True(img.Transparent);
        }

        [Fact]
        public void FromBinary_PngWithRealAlpha_IsAlphaSplitEligible_NotTransparent()
        {
            // Whichever shape PeachImage's encoder auto-picks for a uniform partial-alpha color (color
            // type 6, or an indexed palette with a partial-alpha tRNS entry), it's alpha-split-eligible
            // (issue #1109) either way - see PngPassthrough_RealAlphaPng_IsPopulatedWithAlphaSplit and
            // PngPassthrough_PalettePartialAlphaTrns_IsPopulatedWithAlphaSplit for the two shapes split
            // out explicitly.
            var bytes = MakePngBytes(4, 4, 255, 0, 0, a: 128);
            var img = ImageSource.FromBinary("test.png", () => bytes);

            Assert.False(img.Transparent);
            Assert.NotNull(img.PngPassthrough);
            Assert.NotNull(img.PngPassthrough!.Value.AlphaIdatData);
        }

        [Fact]
        public void FromBinary_GifWithTransparency_IsTransparent()
        {
            // Previously (PNG-magic-byte sniff): this GIF would have been mis-routed to the lossy JPEG
            // path, silently dropping its transparency, since only a PNG signature counted.
            // Image.HasAlpha (source-format-declared alpha, from GIF's own transparent-index
            // declaration) catches it regardless of source format.
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

        // --- CMYK / ICC pass-through (issue #1085) ---
        // See CmykJpegFixture for the fixture itself and why it's a real, reused-byte-for-byte file
        // rather than a synthesized one.
        private static byte[] CmykJpegNoIccBytes => CmykJpegFixture.NoIccBytes;

        [Fact]
        public void FromBinary_CmykJpeg_IsCmyk()
        {
            var img = ImageSource.FromBinary("cmyk.jpg", () => CmykJpegNoIccBytes);

            Assert.True(img.IsCmyk);
        }

        [Fact]
        public void FromBinary_RgbJpeg_IsNotCmyk()
        {
            var img = ImageSource.FromBinary("test.jpg", () => MakeJpegBytes(4, 4, 255, 0, 0));

            Assert.False(img.IsCmyk);
        }

        [Fact]
        public void JpegPassthrough_CmykJpegWithoutIcc_ReturnsOriginalBytesInvertedNoIcc()
        {
            var bytes = CmykJpegNoIccBytes;
            var img = ImageSource.FromBinary("cmyk.jpg", () => bytes);

            var passthrough = img.JpegPassthrough;

            Assert.NotNull(passthrough);
            Assert.Equal(JpegPassthroughColorSpace.Cmyk, passthrough.Value.ColorSpace);
            Assert.Equal(bytes, passthrough.Value.Data);
            Assert.True(passthrough.Value.NeedsInvertedDecode);
            Assert.Null(passthrough.Value.IccProfile);
            Assert.Null(img.PngPassthrough);
            Assert.Null(img.GifPassthrough);
            Assert.False(img.IsLosslessSourceFormat);
        }

        [Fact]
        public void SaveAsJpeg_OnCmykSource_Throws()
        {
            // A CMYK source always embeds via JpegPassthrough (see PdfImage.EmbedJpegPassthrough,
            // reached via InitializeJpeg's fast path) - SaveAsJpeg is a defensive path that should never
            // actually be reached for one.
            var img = ImageSource.FromBinary("cmyk.jpg", () => CmykJpegNoIccBytes);

            Assert.Throws<InvalidOperationException>(() => img.SaveAsJpeg(new MemoryStream()));
        }

        [Fact]
        public void SaveAsPdfBitmap_OnCmykSource_Throws()
        {
            var img = ImageSource.FromBinary("cmyk.jpg", () => CmykJpegNoIccBytes);

            Assert.Throws<InvalidOperationException>(() => img.SaveAsPdfBitmap(new MemoryStream()));
        }

        [Fact]
        public void JpegPassthrough_CmykJpegWithIcc_IncludesIccProfile()
        {
            var icc = IccProfileFixture.BuildCmykProfile();
            var bytes = IccProfileFixture.InsertIccProfileIntoJpeg(CmykJpegNoIccBytes, icc);
            var img = ImageSource.FromBinary("cmyk-icc.jpg", () => bytes);

            var passthrough = img.JpegPassthrough;

            Assert.NotNull(passthrough);
            Assert.Equal(JpegPassthroughColorSpace.Cmyk, passthrough.Value.ColorSpace);
            Assert.Equal(bytes, passthrough.Value.Data);
            Assert.Equal(icc, passthrough.Value.IccProfile);
        }

        [Fact]
        public void JpegPassthrough_RgbJpegWithoutIcc_IsNull()
        {
            var img = ImageSource.FromBinary("test.jpg", () => MakeJpegBytes(4, 4, 255, 0, 0));

            Assert.Null(img.JpegPassthrough);
        }

        [Fact]
        public void JpegPassthrough_RgbJpegWithIcc_ReturnsOriginalBytesWithIccProfile()
        {
            var icc = IccProfileFixture.BuildRgbProfile();
            var bytes = IccProfileFixture.InsertIccProfileIntoJpeg(MakeJpegBytes(4, 4, 255, 0, 0), icc);
            var img = ImageSource.FromBinary("rgb-icc.jpg", () => bytes);

            var passthrough = img.JpegPassthrough;

            Assert.NotNull(passthrough);
            Assert.Equal(JpegPassthroughColorSpace.Rgb, passthrough.Value.ColorSpace);
            Assert.Equal(bytes, passthrough.Value.Data);
            Assert.False(passthrough.Value.NeedsInvertedDecode);
            Assert.Equal(icc, passthrough.Value.IccProfile);
            Assert.False(img.IsCmyk);
        }

        private static byte[] MakeGrayJpegBytes(int width, int height, byte gray)
        {
            using var image = Image.Create(width, height, PixelFormat.Gray8);
            image.GetPixelSpan().Fill(gray);
            using var ms = new MemoryStream();
            image.Save(ms, "jpeg", new JpegEncoderOptions { Quality = 90 });
            return ms.ToArray();
        }

        [Fact]
        public void JpegPassthrough_GrayJpegWithIcc_ReturnsOriginalBytesWithIccProfile()
        {
            var icc = IccProfileFixture.BuildGrayProfile();
            var bytes = IccProfileFixture.InsertIccProfileIntoJpeg(MakeGrayJpegBytes(4, 4, 128), icc);
            var img = ImageSource.FromBinary("gray-icc.jpg", () => bytes);

            var passthrough = img.JpegPassthrough;

            Assert.NotNull(passthrough);
            Assert.Equal(JpegPassthroughColorSpace.Gray, passthrough.Value.ColorSpace);
            Assert.Equal(bytes, passthrough.Value.Data);
            Assert.Equal(icc, passthrough.Value.IccProfile);
        }

        [Fact]
        public void JpegPassthrough_PngWithNoIcc_IsNull()
        {
            // Pins the "JPEG-only for now" boundary: a non-JPEG source never populates JpegPassthrough,
            // regardless of any other metadata it might carry (PeachImage's PNG encoder has no ICC/iCCP
            // support to attach one here, but the routing check in PeachImageSource.Decode is a plain
            // `info.FormatName == "jpeg"` gate that doesn't depend on that either way).
            var img = ImageSource.FromBinary("test.png", () => MakePngBytes(4, 4, 255, 0, 0));

            Assert.Null(img.JpegPassthrough);
        }

        // --- PngPassthrough ---

        [Fact]
        public void PngPassthrough_OpaqueIndexedPng_IsPopulated()
        {
            // Same fixture shape as FromBinary_OpaquePng_IsNotTransparent above (PeachImage's encoder
            // auto-indexes a small uniform-color source) - now also proving it populates PngPassthrough,
            // not just that it reports Transparent = false.
            var bytes = MakePngBytes(4, 4, 255, 0, 0);
            var img = ImageSource.FromBinary("test.png", () => bytes);

            var passthrough = img.PngPassthrough;

            Assert.NotNull(passthrough);
            Assert.Equal(PngPassthroughColorSpace.Indexed, passthrough.Value.ColorSpace);
            Assert.NotNull(passthrough.Value.PaletteRgb);
            Assert.False(img.Transparent);
            Assert.True(img.IsLosslessSourceFormat);
            Assert.Null(img.JpegPassthrough);
            Assert.Null(img.CmykRaster);
            Assert.False(img.IsGrayscale);
            Assert.False(img.IsCmyk);
        }

        [Fact]
        public void PngPassthrough_OpaqueTruecolorPng_IsPopulatedAsRgb()
        {
            // An Rgb24 source (not Rgba32 - that would carry real alpha and encode as color type 6
            // TruecolorAlpha even under ColorMode.Truecolor, same shape as
            // FromBinary_OpaqueTruecolorPng_IsTransparent above) with ColorMode.Truecolor forces a real
            // color type 2 (no palette, no alpha) encode, isolating the plain RGB case from the
            // auto-indexed shape MakePngBytes/PngPassthrough_OpaqueIndexedPng_IsPopulated covers.
            var bytes = RasterPngFixture.MakeInterlacedPngBytes(4, 4, 10, 20, 30, interlace: false);

            var img = ImageSource.FromBinary("test.png", () => bytes);

            var passthrough = img.PngPassthrough;

            Assert.NotNull(passthrough);
            Assert.Equal(PngPassthroughColorSpace.Rgb, passthrough.Value.ColorSpace);
            Assert.Null(passthrough.Value.PaletteRgb);
        }

        [Fact]
        public void PngPassthrough_OpaqueGrayscalePng_IsPopulatedAsGray()
        {
            using var image = Image.Create(4, 4, PixelFormat.Gray8);
            image.GetPixelSpan().Fill(128);
            using var ms = new MemoryStream();
            image.Save(ms, "png");

            var img = ImageSource.FromBinary("test.png", () => ms.ToArray());

            var passthrough = img.PngPassthrough;

            Assert.NotNull(passthrough);
            Assert.Equal(PngPassthroughColorSpace.Gray, passthrough.Value.ColorSpace);
        }

        [Fact]
        public void PngPassthrough_RealAlphaPng_IsPopulatedWithAlphaSplit()
        {
            // Issue #1109: a real per-pixel alpha channel (color type 6, TruecolorAlpha) is now
            // alpha-split-eligible instead of falling back to the full decode+SMask path - see
            // PngAlphaSplitTests.cs for the split transform's own correctness coverage.
            var bytes = MakePngBytes(4, 4, 255, 0, 0, a: 128);
            var img = ImageSource.FromBinary("test.png", () => bytes);

            var passthrough = img.PngPassthrough;

            Assert.NotNull(passthrough);
            Assert.Equal(PngPassthroughColorSpace.Rgb, passthrough.Value.ColorSpace);
            Assert.NotNull(passthrough.Value.AlphaIdatData);
            Assert.Equal((byte)8, passthrough.Value.AlphaBitDepth);
            Assert.Null(passthrough.Value.ColorKeyMask);
            Assert.True(img.IsLosslessSourceFormat);
        }

        [Fact]
        public void PngPassthrough_TruecolorTrnsPng_IsPopulatedWithChromaKeyMask()
        {
            // A Truecolor tRNS is always a single exact chroma-key value - eligible for byte-for-byte
            // pass-through with a PDF /Mask color-key array, not disqualified by HasTrns the way a real
            // per-pixel alpha channel is.
            var bytes = RasterPngFixture.MakeTrnsPngBytes(4, 4, 255, 0, 0, (10, 20, 30));
            var img = ImageSource.FromBinary("test.png", () => bytes);

            var passthrough = img.PngPassthrough;

            Assert.NotNull(passthrough);
            Assert.Equal(PngPassthroughColorSpace.Rgb, passthrough.Value.ColorSpace);
            Assert.Equal([10, 10, 20, 20, 30, 30], passthrough.Value.ColorKeyMask!);
        }

        [Fact]
        public void PngPassthrough_GrayscaleTrnsPng_IsPopulatedWithChromaKeyMask()
        {
            var bytes = RasterPngFixture.MakeGrayscaleTrnsPngBytes(4, 4, gray: 128, transparentGray: 64);
            var img = ImageSource.FromBinary("test.png", () => bytes);

            var passthrough = img.PngPassthrough;

            Assert.NotNull(passthrough);
            Assert.Equal(PngPassthroughColorSpace.Gray, passthrough.Value.ColorSpace);
            Assert.Equal([64, 64], passthrough.Value.ColorKeyMask!);
        }

        [Fact]
        public void PngPassthrough_PaletteTrnsAllBinary_IsPopulatedWithIndexMask()
        {
            (byte, byte, byte)[] palette = [(255, 0, 0), (0, 255, 0), (0, 0, 255)];
            byte[] alphas = [255, 0, 255]; // index 1 (green) is the transparent entry
            var bytes = RasterPngFixture.MakeIndexedPngBytesWithTrns(2, 2, palette, alphas, (x, y) => (byte)((x + y) % 3));

            var img = ImageSource.FromBinary("test.png", () => bytes);
            var passthrough = img.PngPassthrough;

            Assert.NotNull(passthrough);
            Assert.Equal(PngPassthroughColorSpace.Indexed, passthrough.Value.ColorSpace);
            Assert.Equal([1, 1], passthrough.Value.ColorKeyMask!);
        }

        [Fact]
        public void PngPassthrough_PaletteTrnsContiguousMultiIndex_IsPopulatedWithRangeMask()
        {
            // Two transparent indices that ARE contiguous (1 and 2) still collapse to a single [min max]
            // range, which is all PDF's colour-key mask supports for an Indexed colour space (one
            // component, ISO 32000-1 8.9.6.4) - not one pair per index.
            (byte, byte, byte)[] palette = [(255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 0)];
            byte[] alphas = [255, 0, 0, 255];
            var bytes = RasterPngFixture.MakeIndexedPngBytesWithTrns(2, 2, palette, alphas, (x, y) => (byte)((x + y) % 4));

            var img = ImageSource.FromBinary("test.png", () => bytes);
            var passthrough = img.PngPassthrough;

            Assert.NotNull(passthrough);
            Assert.Equal([1, 2], passthrough.Value.ColorKeyMask!);
        }

        [Fact]
        public void PngPassthrough_PaletteTrnsNonContiguousIndices_IsNull()
        {
            // Indices 0 and 2 are transparent but 1 is opaque - a single [min max] range can't express
            // "these two, but not the one in between" without also masking index 1, so this falls back to
            // the existing decode+SMask path instead of emitting an incorrect mask.
            (byte, byte, byte)[] palette = [(255, 0, 0), (0, 255, 0), (0, 0, 255)];
            byte[] alphas = [0, 255, 0];
            var bytes = RasterPngFixture.MakeIndexedPngBytesWithTrns(2, 2, palette, alphas, (x, y) => (byte)((x + y) % 3));

            var img = ImageSource.FromBinary("test.png", () => bytes);

            Assert.Null(img.PngPassthrough);
            Assert.True(img.IsLosslessSourceFormat);
        }

        [Fact]
        public void PngPassthrough_PaletteTrnsAllOpaque_IsPopulatedWithNoMask()
        {
            // A tRNS chunk with only fully-opaque entries is a legal (if wasteful) PNG - still a valid
            // pass-through, just with nothing to mask.
            (byte, byte, byte)[] palette = [(255, 0, 0), (0, 255, 0)];
            byte[] alphas = [255, 255];
            var bytes = RasterPngFixture.MakeIndexedPngBytesWithTrns(2, 2, palette, alphas, (x, y) => (byte)((x + y) % 2));

            var img = ImageSource.FromBinary("test.png", () => bytes);
            var passthrough = img.PngPassthrough;

            Assert.NotNull(passthrough);
            Assert.Null(passthrough.Value.ColorKeyMask);
        }

        [Fact]
        public void PngPassthrough_PalettePartialAlphaTrns_IsPopulatedWithAlphaSplit()
        {
            // A partial (neither 0 nor 255) palette alpha entry can't be expressed as a binary color-key
            // mask, but is alpha-split-eligible (issue #1109) - PngAlphaSplit's palette branch splits just
            // the alpha plane out (ColorData stays null; the original indexed IdatData/PaletteData are
            // reused unchanged for the color side - see BuildAlphaSplitPassthroughData's own remarks).
            (byte, byte, byte)[] palette = [(255, 0, 0), (0, 255, 0)];
            byte[] alphas = [255, 128];
            var bytes = RasterPngFixture.MakeIndexedPngBytesWithTrns(2, 2, palette, alphas, (x, y) => (byte)((x + y) % 2));

            var img = ImageSource.FromBinary("test.png", () => bytes);
            var passthrough = img.PngPassthrough;

            Assert.NotNull(passthrough);
            Assert.Equal(PngPassthroughColorSpace.Indexed, passthrough.Value.ColorSpace);
            Assert.NotNull(passthrough.Value.PaletteRgb);
            Assert.NotNull(passthrough.Value.AlphaIdatData);
            Assert.Null(passthrough.Value.ColorKeyMask);
            Assert.True(img.IsLosslessSourceFormat);
        }

        [Fact]
        public void PngPassthrough_InterlacedPng_IsNull()
        {
            var bytes = RasterPngFixture.MakeInterlacedPngBytes(4, 4, 10, 20, 30);
            var img = ImageSource.FromBinary("test.png", () => bytes);

            Assert.Null(img.PngPassthrough);
            Assert.False(img.Transparent);
            Assert.True(img.IsLosslessSourceFormat);
        }

        [Fact]
        public void PngPassthrough_InterlacedTrnsPng_IsNull()
        {
            // Interlace wins regardless of whether the tRNS itself would otherwise be a valid chroma key.
            var bytes = RasterPngFixture.MakeInterlacedPngBytes(4, 4, 10, 20, 30, transparentColor: (10, 20, 30));
            var img = ImageSource.FromBinary("test.png", () => bytes);

            Assert.Null(img.PngPassthrough);
        }

        [Fact]
        public void PngPassthrough_JpegSource_IsNull()
        {
            var bytes = MakeJpegBytes(4, 4, 255, 0, 0);
            var img = ImageSource.FromBinary("test.jpg", () => bytes);

            Assert.Null(img.PngPassthrough);
        }

        [Fact]
        public void IsLosslessSourceFormat_Bmp_IsTrue()
        {
            var bytes = MakeBmpBytes(4, 4, 255, 0, 0);
            var img = ImageSource.FromBinary("test.bmp", () => bytes);

            Assert.True(img.IsLosslessSourceFormat);
        }

        [Fact]
        public void IsLosslessSourceFormat_Jpeg_IsFalse()
        {
            var bytes = MakeJpegBytes(4, 4, 255, 0, 0);
            var img = ImageSource.FromBinary("test.jpg", () => bytes);

            Assert.False(img.IsLosslessSourceFormat);
        }

        [Fact]
        public void IsLosslessSourceFormat_LosslessWebp_IsTrue()
        {
            // WebpEncoderOptions defaults to Lossless = true (VP8L) - issue #1107's
            // ImageInfo.IsLosslessEncoding now lets PeachPDF tell a lossless WebP source apart from a
            // lossy one, so it gets the same ImageCompression.Auto/Lossless protection an opaque
            // PNG/BMP/GIF already does.
            using var image = MakeSolidImage(4, 4, 10, 20, 30, a: 255);
            using var ms = new MemoryStream();
            image.Save(ms, "webp", new WebpEncoderOptions());

            var img = ImageSource.FromBinary("test.webp", () => ms.ToArray());

            Assert.True(img.IsLosslessSourceFormat);
        }

        [Fact]
        public void IsLosslessSourceFormat_LossyWebp_IsFalse()
        {
            using var image = MakeSolidImage(4, 4, 10, 20, 30, a: 255);
            using var ms = new MemoryStream();
            image.Save(ms, "webp", new WebpEncoderOptions { Lossless = false });

            var img = ImageSource.FromBinary("test.webp", () => ms.ToArray());

            Assert.False(img.IsLosslessSourceFormat);
        }

        [Fact]
        public void IsLosslessSourceFormat_LosslessAvif_IsTrue()
        {
            using var image = MakeSolidImage(4, 4, 10, 20, 30, a: 255);
            using var ms = new MemoryStream();
            image.Save(ms, "avif", new AvifEncoderOptions { Lossless = true });

            var img = ImageSource.FromBinary("test.avif", () => ms.ToArray());

            Assert.True(img.IsLosslessSourceFormat);
        }

        [Fact]
        public void IsLosslessSourceFormat_LossyAvif_IsFalse()
        {
            // AvifEncoderOptions defaults to Lossless = false.
            using var image = MakeSolidImage(4, 4, 10, 20, 30, a: 255);
            using var ms = new MemoryStream();
            image.Save(ms, "avif", new AvifEncoderOptions());

            var img = ImageSource.FromBinary("test.avif", () => ms.ToArray());

            Assert.False(img.IsLosslessSourceFormat);
        }

        [Fact]
        public void IsLosslessSourceFormat_Tiff_IsTrue()
        {
            // PeachImage's TIFF decoder only ever supports lossless compression tags (none/LZW/PackBits)
            // - IsLosslessEncoding is always true for any TIFF that decodes successfully today (see
            // ImageInfo.IsLosslessEncoding's own remarks), so there's no "lossy TIFF" counterpart to
            // test here yet. A non-CMYK TIFF (unlike CmykTiffFixture's fixtures) reaches this generic
            // fallback rather than DecodeCmykRaster.
            var bytes = RgbTiffFixture.Build(4, 4, 10, 20, 30);

            var img = ImageSource.FromBinary("test.tiff", () => bytes);

            Assert.True(img.IsLosslessSourceFormat);
        }

        // --- GifPassthrough (issue #1110) ---

        [Fact]
        public void GifPassthrough_FullPaletteOpaqueGif_IsPopulated()
        {
            var bytes = RasterGifFixture.MakeFullPaletteGifBytes(16, 16);
            var img = ImageSource.FromBinary("test.gif", () => bytes);

            var passthrough = img.GifPassthrough;

            Assert.NotNull(passthrough);
            Assert.Equal(768, passthrough.Value.Palette.Length); // 256 entries x 3
            Assert.Null(passthrough.Value.ColorKeyMask);
            Assert.False(img.Transparent);
            Assert.True(img.IsLosslessSourceFormat);
            Assert.Null(img.PngPassthrough);
            Assert.Null(img.JpegPassthrough);
            Assert.Null(img.CmykRaster);
        }

        [Fact]
        public void GifPassthrough_TransparentGif_IsPopulatedWithColorKeyMask()
        {
            var bytes = RasterGifFixture.MakeTransparentGifBytes(16, 16);
            var img = ImageSource.FromBinary("test.gif", () => bytes);

            var passthrough = img.GifPassthrough;

            Assert.NotNull(passthrough);
            Assert.NotNull(passthrough.Value.ColorKeyMask);
            Assert.Equal(2, passthrough.Value.ColorKeyMask!.Length);
            Assert.Equal(passthrough.Value.ColorKeyMask[0], passthrough.Value.ColorKeyMask[1]);
        }

        [Fact]
        public void GifPassthrough_SmallPaletteGif_IsNull()
        {
            // MinCodeSize < 8 - GIF's and PDF's LZW conventions are only byte-compatible when
            // MinCodeSize == 8 (see GifPassthroughData's own remarks).
            var bytes = RasterGifFixture.MakeSmallPaletteGifBytes(8, 8, maxColors: 4);
            var img = ImageSource.FromBinary("test.gif", () => bytes);

            Assert.Null(img.GifPassthrough);
            Assert.True(img.IsLosslessSourceFormat);
        }

        [Fact]
        public void GifPassthrough_InterlacedGif_IsNull()
        {
            var bytes = RasterGifFixture.MakeInterlacedGifBytes(8, 8);
            var img = ImageSource.FromBinary("test.gif", () => bytes);

            Assert.Null(img.GifPassthrough);
            Assert.True(img.IsLosslessSourceFormat);
        }

        [Fact]
        public void GifPassthrough_PartialCanvasGif_IsNull()
        {
            // A frame smaller than its own logical screen - byte-for-byte pass-through has no
            // canvas-compositing step the way the existing full decode does.
            var bytes = RasterGifFixture.MakePartialCanvasGifBytes(canvasWidth: 16, canvasHeight: 16, frameWidth: 8, frameHeight: 8);
            var img = ImageSource.FromBinary("test.gif", () => bytes);

            Assert.Null(img.GifPassthrough);
            Assert.True(img.IsLosslessSourceFormat);
        }

        [Fact]
        public void GifPassthrough_JpegSource_IsNull()
        {
            var bytes = MakeJpegBytes(4, 4, 255, 0, 0);
            var img = ImageSource.FromBinary("test.jpg", () => bytes);

            Assert.Null(img.GifPassthrough);
        }

        // A minimal, hand-built 2x2 uncompressed CMYK TIFF (PhotometricInterpretation=5/Separated,
        // SamplesPerPixel=4, 8 bits/sample). Real CMYK TIFF corpus fixtures are 90KB+ (strip-based, pixel
        // data before the IFD, not truncatable), so this is hand-built instead, the same "smallest legal
        // file" approach IccProfileFixture takes for ICC profiles.
        private const string SyntheticCmykTiffBase64 =
            "SUkqAAgAAAAJAAABAwABAAAAAgAAAAEBAwABAAAAAgAAAAIBAwAEAAAAegAAAAMBAwABAAAAAQAAAAYBAwABAAAA" +
            "BQAAABEBBAABAAAAggAAABUBAwABAAAABAAAABYBAwABAAAAAgAAABcBBAABAAAAEAAAAAAAAAAIAAgACAAIAAoU" +
            "HigyPEZQWmRueIKMlqA=";

        // --- CMYK TIFF raw-raster embed (issue #1096) ---
        // Unlike CMYK JPEG's byte-for-byte JpegPassthrough (there's no PDF-native pass-through filter for
        // TIFF the way /DCTDecode gives JPEG), a CMYK TIFF's decoded pixel buffer reaches PdfImage via
        // CmykRaster instead - see PdfImage.InitializeCmykRaster and CmykTiffIntegrationTests for the
        // full HTML->PDF coverage (including a byte-for-byte decompressed-pixel verification).

        [Fact]
        public void FromBinary_CmykTiff_IsCmyk()
        {
            var bytes = Convert.FromBase64String(SyntheticCmykTiffBase64);

            var img = ImageSource.FromBinary("cmyk.tiff", () => bytes);

            Assert.True(img.IsCmyk);
        }

        [Fact]
        public void FromBinary_CmykTiff_JpegPassthroughIsNull()
        {
            // TIFF has no byte-for-byte pass-through path - CmykRaster (below) carries its data instead.
            var bytes = Convert.FromBase64String(SyntheticCmykTiffBase64);

            var img = ImageSource.FromBinary("cmyk.tiff", () => bytes);

            Assert.Null(img.JpegPassthrough);
        }

        [Fact]
        public void CmykRaster_CmykTiffWithoutIcc_ReturnsDecodedPixelsNoIcc()
        {
            var width = 4;
            var height = 4;
            var tiffBytes = CmykTiffFixture.Build(width, height, 11, 22, 33, 44);
            var img = ImageSource.FromBinary("cmyk.tiff", () => tiffBytes);

            var raster = img.CmykRaster;

            Assert.NotNull(raster);
            Assert.Null(raster.Value.IccProfile);
            Assert.Equal(width * height * 4, raster.Value.Data.Length);
            for (var i = 0; i < raster.Value.Data.Length; i += 4)
            {
                Assert.Equal(11, raster.Value.Data[i]);
                Assert.Equal(22, raster.Value.Data[i + 1]);
                Assert.Equal(33, raster.Value.Data[i + 2]);
                Assert.Equal(44, raster.Value.Data[i + 3]);
            }
        }

        [Fact]
        public void CmykRaster_CmykTiffWithIcc_IncludesIccProfile()
        {
            var icc = IccProfileFixture.BuildCmykProfile();
            var tiffBytes = CmykTiffFixture.Build(4, 4, 10, 20, 30, 40, icc);
            var img = ImageSource.FromBinary("cmyk-icc.tiff", () => tiffBytes);

            var raster = img.CmykRaster;

            Assert.NotNull(raster);
            Assert.Equal(icc, raster.Value.IccProfile);
        }

        [Fact]
        public void CmykRaster_RgbTiff_IsNull()
        {
            // Pins the boundary the other way: a non-CMYK TIFF never populates CmykRaster (it takes the
            // ordinary SaveAsPdfBitmap path like any other RGB/Gray raster source).
            var img = ImageSource.FromBinary("test.png", () => MakePngBytes(4, 4, 255, 0, 0));

            Assert.Null(img.CmykRaster);
        }

        [Fact]
        public void SaveAsJpeg_OnCmykRasterSource_Throws()
        {
            var tiffBytes = CmykTiffFixture.Build(2, 2, 1, 2, 3, 4);
            var img = ImageSource.FromBinary("cmyk.tiff", () => tiffBytes);

            Assert.Throws<InvalidOperationException>(() => img.SaveAsJpeg(new MemoryStream()));
        }

        [Fact]
        public void SaveAsPdfBitmap_OnCmykRasterSource_Throws()
        {
            var tiffBytes = CmykTiffFixture.Build(2, 2, 1, 2, 3, 4);
            var img = ImageSource.FromBinary("cmyk.tiff", () => tiffBytes);

            Assert.Throws<InvalidOperationException>(() => img.SaveAsPdfBitmap(new MemoryStream()));
        }
    }
}

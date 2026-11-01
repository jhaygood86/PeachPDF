using PeachImage;
using PeachImage.Formats.Png;
using PeachImage.Formats.Webp;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end coverage for issue #1106: a PNG's embedded <c>iCCP</c> profile now rides its pass-through
    /// embed as an <c>/ICCBased</c> color space (instead of a bare Device* one), and a WebP/AVIF source's
    /// embedded profile is now extracted and wired into its raw-bitmap embed the same way - see
    /// <c>PeachImageSourceTests</c> for the lower-level <c>PngPassthroughData.IccProfile</c>/
    /// <c>IImageSource.RgbIccProfile</c> plumbing this exercises through a real render, and
    /// <c>CmykImageIntegrationTests</c> for the equivalent JPEG/CMYK ICC coverage (issue #1085) this
    /// mirrors closely.
    /// </summary>
    public class IccPassthroughIntegrationTests
    {
        private static string DataUri(byte[] bytes, string mime) => $"data:{mime};base64,{Convert.ToBase64String(bytes)}";

        private static async Task<string> GetPdfText(string html, PdfGenerateConfig config)
        {
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        private static void AssertContainsRawBytes(string pdfText, byte[] rawBytes) =>
            Assert.Contains(Encoding.Latin1.GetString(rawBytes), pdfText);

        [Fact]
        public async Task TruecolorPngWithIcc_EmbedsIccBasedColorSpace()
        {
            var withoutIcc = RasterPngFixture.MakeInterlacedPngBytes(6, 6, 200, 100, 50, interlace: false);
            var iccBytes = IccProfileFixture.BuildRgbProfile();
            var bytes = IccProfileFixture.InsertIccProfileIntoPng(withoutIcc, iccBytes);
            var html = $"<html><body><img src=\"{DataUri(bytes, "image/png")}\" width=\"6\" height=\"6\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/FlateDecode", pdfText);
            Assert.Matches(new Regex(@"/ICCBased\s+\d+\s+0\s+R"), pdfText);
            Assert.Matches(new Regex(@"/N\s+3\b"), pdfText);
            Assert.Matches(new Regex(@"/Alternate\s*/DeviceRGB"), pdfText);
            AssertContainsRawBytes(pdfText, iccBytes);
        }

        [Fact]
        public async Task IndexedPngWithIcc_IndexedBaseIsIccBased()
        {
            (byte, byte, byte)[] palette = [(255, 0, 0), (0, 255, 0), (0, 0, 255)];
            var withoutIcc = RasterPngFixture.MakeIndexedPngBytes(4, 4, (x, y) => palette[(x + y) % 3]);
            var iccBytes = IccProfileFixture.BuildRgbProfile();
            var bytes = IccProfileFixture.InsertIccProfileIntoPng(withoutIcc, iccBytes);
            var html = $"<html><body><img src=\"{DataUri(bytes, "image/png")}\" width=\"4\" height=\"4\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Matches(new Regex(@"/Indexed\s*\[\s*/ICCBased"), pdfText);
            AssertContainsRawBytes(pdfText, iccBytes);
        }

        private static byte[] MakeTruecolorAlphaPngBytes(int width, int height)
        {
            using var image = Image.Create(width, height, PixelFormat.Rgba32);
            var pixels = image.GetPixelSpan();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 200; pixels[i + 1] = 80; pixels[i + 2] = 40; pixels[i + 3] = 128;
            }
            using var ms = new MemoryStream();
            image.Save(ms, "png", new PngEncoderOptions { ColorMode = PngColorMode.Truecolor });
            return ms.ToArray();
        }

        private static byte[] MakeOpaqueRgbWebpBytes(int width, int height)
        {
            using var image = Image.Create(width, height, PixelFormat.Rgb24);
            var pixels = image.GetPixelSpan();
            for (int i = 0; i < pixels.Length; i += 3)
            {
                pixels[i] = 200; pixels[i + 1] = 80; pixels[i + 2] = 40;
            }
            using var ms = new MemoryStream();
            image.Save(ms, "webp", new WebpEncoderOptions());
            return ms.ToArray();
        }

        [Fact]
        public async Task AlphaSplitPngWithIcc_ColorPlaneIsIccBased()
        {
            var withoutIcc = MakeTruecolorAlphaPngBytes(6, 6);
            var iccBytes = IccProfileFixture.BuildRgbProfile();
            var bytes = IccProfileFixture.InsertIccProfileIntoPng(withoutIcc, iccBytes);
            var html = $"<html><body><img src=\"{DataUri(bytes, "image/png")}\" width=\"6\" height=\"6\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/SMask", pdfText);
            Assert.Matches(new Regex(@"/ICCBased\s+\d+\s+0\s+R"), pdfText);
            AssertContainsRawBytes(pdfText, iccBytes);
        }

        [Fact]
        public async Task PngWithoutIcc_StaysBareDeviceRgb()
        {
            var bytes = RasterPngFixture.MakeInterlacedPngBytes(6, 6, 200, 100, 50, interlace: false);
            var html = $"<html><body><img src=\"{DataUri(bytes, "image/png")}\" width=\"6\" height=\"6\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.DoesNotContain("/ICCBased", pdfText);
            Assert.Contains("/DeviceRGB", pdfText);
        }

        [Fact]
        public async Task OpaqueWebpWithIcc_EmbedsIccBasedColorSpace()
        {
            var withoutIcc = MakeOpaqueRgbWebpBytes(6, 6);
            var iccBytes = IccProfileFixture.BuildRgbProfile();
            var bytes = IccProfileFixture.InsertIccProfileIntoWebp(withoutIcc, iccBytes);
            var html = $"<html><body><img src=\"{DataUri(bytes, "image/webp")}\" width=\"6\" height=\"6\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Matches(new Regex(@"/ICCBased\s+\d+\s+0\s+R"), pdfText);
            Assert.Matches(new Regex(@"/N\s+3\b"), pdfText);
            AssertContainsRawBytes(pdfText, iccBytes);
        }

        [Fact]
        public async Task WebpWithoutIcc_StaysBareDeviceRgb()
        {
            var bytes = MakeOpaqueRgbWebpBytes(6, 6);
            var html = $"<html><body><img src=\"{DataUri(bytes, "image/webp")}\" width=\"6\" height=\"6\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.DoesNotContain("/ICCBased", pdfText);
            Assert.Contains("/DeviceRGB", pdfText);
        }

        // The three tests below cover sources that don't take the ordinary pass-through/raw-bitmap-with-
        // ICC embed path: a lossy-encoded WebP and a pass-through-eligible PNG under
        // ImageCompression.Lossy both fall back to a *re-encoded JPEG* embed instead, which still needs
        // to carry the already-parsed ICC profile onto its own ColorSpace (re-encoding only
        // recompresses already-decoded samples - it doesn't change what color space they're in). An
        // interlaced PNG never qualifies for pass-through at all, so it needs its own ICC-preserving
        // path through the ordinary full-decode-plus-raw-bitmap embed instead.

        private static byte[] MakeLossyOpaqueRgbWebpBytes(int width, int height)
        {
            using var image = Image.Create(width, height, PixelFormat.Rgb24);
            var pixels = image.GetPixelSpan();
            for (int i = 0; i < pixels.Length; i += 3)
            {
                pixels[i] = 200; pixels[i + 1] = 80; pixels[i + 2] = 40;
            }
            using var ms = new MemoryStream();
            image.Save(ms, "webp", new WebpEncoderOptions { Lossless = false });
            return ms.ToArray();
        }

        [Fact]
        public async Task LossyEncodedWebpWithIcc_JpegReencodeStillEmbedsIccBasedColorSpace()
        {
            var withoutIcc = MakeLossyOpaqueRgbWebpBytes(6, 6);
            var iccBytes = IccProfileFixture.BuildRgbProfile();
            var bytes = IccProfileFixture.InsertIccProfileIntoWebp(withoutIcc, iccBytes);
            var html = $"<html><body><img src=\"{DataUri(bytes, "image/webp")}\" width=\"6\" height=\"6\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/DCTDecode", pdfText);
            Assert.Matches(new Regex(@"/ICCBased\s+\d+\s+0\s+R"), pdfText);
            AssertContainsRawBytes(pdfText, iccBytes);
        }

        [Fact]
        public async Task PngWithIcc_ImageCompressionLossy_JpegReencodeStillEmbedsIccBasedColorSpace()
        {
            var withoutIcc = RasterPngFixture.MakeInterlacedPngBytes(6, 6, 200, 100, 50, interlace: false);
            var iccBytes = IccProfileFixture.BuildRgbProfile();
            var bytes = IccProfileFixture.InsertIccProfileIntoPng(withoutIcc, iccBytes);
            var html = $"<html><body><img src=\"{DataUri(bytes, "image/png")}\" width=\"6\" height=\"6\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ImageCompression = ImageCompression.Lossy,
            });

            Assert.Contains("/DCTDecode", pdfText);
            Assert.Matches(new Regex(@"/ICCBased\s+\d+\s+0\s+R"), pdfText);
            AssertContainsRawBytes(pdfText, iccBytes);
        }

        [Fact]
        public async Task InterlacedPngWithIcc_FullDecodeFallbackStillPreservesIcc()
        {
            var withoutIcc = RasterPngFixture.MakeInterlacedPngBytes(6, 6, 200, 100, 50, interlace: true);
            var iccBytes = IccProfileFixture.BuildRgbProfile();
            var bytes = IccProfileFixture.InsertIccProfileIntoPng(withoutIcc, iccBytes);
            var html = $"<html><body><img src=\"{DataUri(bytes, "image/png")}\" width=\"6\" height=\"6\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Matches(new Regex(@"/ICCBased\s+\d+\s+0\s+R"), pdfText);
            AssertContainsRawBytes(pdfText, iccBytes);
        }
    }
}

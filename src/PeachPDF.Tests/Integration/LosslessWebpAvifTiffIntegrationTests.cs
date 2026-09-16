using PeachImage;
using PeachImage.Formats.Avif;
using PeachImage.Formats.Webp;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end coverage for issue #1107: <see cref="PdfGenerateConfig.ImageCompression"/>'s
    /// <c>Auto</c>/<c>Lossless</c> protection against a silent lossy JPEG re-encode now extends to a
    /// WebP/AVIF/TIFF source that was itself losslessly encoded (<see cref="ImageInfo.IsLosslessEncoding"/>),
    /// the same way an opaque PNG/BMP/GIF is already protected - see
    /// <c>PngPassthroughIntegrationTests</c> for that existing PNG/BMP coverage. A lossy-encoded
    /// WebP/AVIF source is unaffected and keeps re-encoding as JPEG regardless of the setting - proving
    /// the flag is genuinely per-source-encoding, not just per-format. See <c>PeachImageSourceTests</c>
    /// for the lower-level <c>IImageSource.IsLosslessSourceFormat</c> plumbing this exercises through a
    /// real render.
    /// </summary>
    public class LosslessWebpAvifTiffIntegrationTests
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

        private static byte[] MakeOpaqueWebpBytes(int width, int height, byte r, byte g, byte b, bool lossless)
        {
            using var image = Image.Create(width, height, PixelFormat.Rgb24);
            var pixels = image.GetPixelSpan();
            for (int i = 0; i < pixels.Length; i += 3)
            {
                pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b;
            }
            using var ms = new MemoryStream();
            image.Save(ms, "webp", new WebpEncoderOptions { Lossless = lossless });
            return ms.ToArray();
        }

        private static byte[] MakeOpaqueAvifBytes(int width, int height, byte r, byte g, byte b, bool lossless)
        {
            using var image = Image.Create(width, height, PixelFormat.Rgb24);
            var pixels = image.GetPixelSpan();
            for (int i = 0; i < pixels.Length; i += 3)
            {
                pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b;
            }
            using var ms = new MemoryStream();
            image.Save(ms, "avif", new AvifEncoderOptions { Lossless = lossless });
            return ms.ToArray();
        }

        [Fact]
        public async Task LosslessWebp_NaturalSize_EmbedsAsFlateDecodeNotJpeg()
        {
            var bytes = MakeOpaqueWebpBytes(6, 6, 200, 100, 50, lossless: true);
            var html = $"<html><body><img src=\"{DataUri(bytes, "image/webp")}\" width=\"6\" height=\"6\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/FlateDecode", pdfText);
            Assert.DoesNotContain("/DCTDecode", pdfText);
        }

        [Fact]
        public async Task LossyWebp_NaturalSize_StillEmbedsAsJpeg()
        {
            // Proves IsLosslessSourceFormat is genuinely per-encoding, not just per-format: an otherwise
            // identical WebP source encoded lossy still takes the JPEG re-encode path.
            var bytes = MakeOpaqueWebpBytes(6, 6, 200, 100, 50, lossless: false);
            var html = $"<html><body><img src=\"{DataUri(bytes, "image/webp")}\" width=\"6\" height=\"6\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/DCTDecode", pdfText);
        }

        [Fact]
        public async Task LosslessWebp_Downscaled_StillJpegEncodesUnderAuto()
        {
            // Mirrors PngPassthroughIntegrationTests.ImageCompressionAuto_StillJpegEncodesDownscaledBmp:
            // Auto only protects a lossless source at its own natural size.
            var bytes = MakeOpaqueWebpBytes(40, 40, 200, 100, 50, lossless: true);
            var html = $"<html><body><img src=\"{DataUri(bytes, "image/webp")}\" width=\"10\" height=\"10\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/DCTDecode", pdfText);
        }

        [Fact]
        public async Task LosslessWebp_Downscaled_AvoidsJpegUnderLossless()
        {
            var bytes = MakeOpaqueWebpBytes(40, 40, 200, 100, 50, lossless: true);
            var html = $"<html><body><img src=\"{DataUri(bytes, "image/webp")}\" width=\"10\" height=\"10\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ImageCompression = ImageCompression.Lossless,
            });

            Assert.Contains("/FlateDecode", pdfText);
            Assert.DoesNotContain("/DCTDecode", pdfText);
        }

        [Fact]
        public async Task LosslessAvif_NaturalSize_EmbedsAsFlateDecodeNotJpeg()
        {
            var bytes = MakeOpaqueAvifBytes(6, 6, 200, 100, 50, lossless: true);
            var html = $"<html><body><img src=\"{DataUri(bytes, "image/avif")}\" width=\"6\" height=\"6\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/FlateDecode", pdfText);
            Assert.DoesNotContain("/DCTDecode", pdfText);
        }

        [Fact]
        public async Task LossyAvif_NaturalSize_StillEmbedsAsJpeg()
        {
            var bytes = MakeOpaqueAvifBytes(6, 6, 200, 100, 50, lossless: false);
            var html = $"<html><body><img src=\"{DataUri(bytes, "image/avif")}\" width=\"6\" height=\"6\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/DCTDecode", pdfText);
        }

        [Fact]
        public async Task Tiff_NaturalSize_EmbedsAsFlateDecodeNotJpeg()
        {
            // PeachImage's TIFF decoder only ever supports lossless compression (see
            // PeachImageSourceTests.IsLosslessSourceFormat_Tiff_IsTrue) - every successfully-decoded TIFF
            // is lossless, so there's no lossy-TIFF counterpart to this test.
            var bytes = RgbTiffFixture.Build(6, 6, 200, 100, 50);
            var html = $"<html><body><img src=\"{DataUri(bytes, "image/tiff")}\" width=\"6\" height=\"6\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/FlateDecode", pdfText);
            Assert.DoesNotContain("/DCTDecode", pdfText);
        }
    }
}

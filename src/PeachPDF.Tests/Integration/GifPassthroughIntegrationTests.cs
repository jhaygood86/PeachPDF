using PeachImage.Formats.Gif;
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
    /// End-to-end coverage for issue #1110: an eligible GIF (not interlaced, <c>MinCodeSize == 8</c>,
    /// frame covers the full logical canvas) is embedded as <c>/LZWDecode</c> - the same LZW code values
    /// and widths its own encoder produced, re-packed from GIF's bit order/growth timing into PDF's (see
    /// <c>PdfImage.RepackGifLzwForPdf</c>'s own remarks for why a literal verbatim-bytes copy corrupts -
    /// GIF and PDF/TIFF LZW are not byte-compatible despite using the same algorithm) - rather than
    /// decoded and re-embedded as a raw <c>/FlateDecode</c> RGB stream. See <c>PeachImageSourceTests</c>
    /// for the lower-level <c>IImageSource.GifPassthrough</c> plumbing this exercises through a real
    /// render, <c>GifLzwRepackTests</c> for the repack transform's own correctness coverage, and
    /// <c>PngPassthroughIntegrationTests</c> for the equivalent PNG coverage this mirrors closely.
    /// </summary>
    public class GifPassthroughIntegrationTests
    {
        private static string DataUri(byte[] bytes) => $"data:image/gif;base64,{Convert.ToBase64String(bytes)}";

        private static async Task<string> GetPdfText(string html, PdfGenerateConfig config)
        {
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        /// <summary>Same Latin1 round-trip technique as PngPassthroughIntegrationTests.AssertContainsRawBytes.</summary>
        private static void AssertContainsRawBytes(string pdfText, byte[] rawBytes) =>
            Assert.Contains(Encoding.Latin1.GetString(rawBytes), pdfText);

        [Fact]
        public async Task FullPaletteOpaqueGif_EmbedsAsLzwDecodeWithIndexedColorSpace()
        {
            var bytes = RasterGifFixture.MakeFullPaletteGifBytes(16, 16);
            GifPassthrough.TryRead(new MemoryStream(bytes), out var info);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"16\" height=\"16\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/LZWDecode", pdfText);
            Assert.DoesNotContain("/DCTDecode", pdfText);
            Assert.Matches(new Regex(@"/Indexed\s*/DeviceRGB\s+\d+"), pdfText);
            Assert.DoesNotContain("/Mask", pdfText);
            AssertContainsRawBytes(pdfText, PeachPDF.PdfSharpCore.Pdf.Advanced.PdfImage.RepackGifLzwForPdf(info.LzwData));
        }

        [Fact]
        public async Task TransparentGif_EmbedsWithColorKeyMask()
        {
            var bytes = RasterGifFixture.MakeTransparentGifBytes(16, 16);
            GifPassthrough.TryRead(new MemoryStream(bytes), out var info);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"16\" height=\"16\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/LZWDecode", pdfText);
            Assert.NotNull(info.TransparentColorIndex);
            // Structural check on the actual array value, not just "/Mask present" - the same pitfall
            // PngPassthroughIntegrationTests' own tRNS tests warn about for token-presence-only checks.
            Assert.Matches(new Regex($@"/Mask\s*\[\s*{info.TransparentColorIndex}\s+{info.TransparentColorIndex}\s*\]"), pdfText);
            AssertContainsRawBytes(pdfText, PeachPDF.PdfSharpCore.Pdf.Advanced.PdfImage.RepackGifLzwForPdf(info.LzwData));
        }

        [Fact]
        public async Task SmallPaletteGif_FallsBackToFlateDecode_NotPassthrough()
        {
            var bytes = RasterGifFixture.MakeSmallPaletteGifBytes(8, 8, maxColors: 4);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"8\" height=\"8\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            // Still avoids lossy JPEG at natural size under the default Auto (GIF has no lossy encoding
            // mode at all - IsLosslessSourceFormat, unrelated to pass-through eligibility) - just via the
            // existing raw-RGB /FlateDecode path instead of /LZWDecode pass-through.
            Assert.DoesNotContain("/LZWDecode", pdfText);
            Assert.DoesNotContain("/DCTDecode", pdfText);
            Assert.Contains("/FlateDecode", pdfText);
        }

        [Fact]
        public async Task InterlacedGif_FallsBackToFlateDecode_NotPassthrough()
        {
            var bytes = RasterGifFixture.MakeInterlacedGifBytes(8, 8);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"8\" height=\"8\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.DoesNotContain("/LZWDecode", pdfText);
            Assert.Contains("/FlateDecode", pdfText);
        }

        [Fact]
        public async Task ImageCompressionLossy_ForcesJpegEvenForEligibleGif()
        {
            var bytes = RasterGifFixture.MakeFullPaletteGifBytes(16, 16);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"16\" height=\"16\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ImageCompression = ImageCompression.Lossy,
            });

            Assert.Contains("/DCTDecode", pdfText);
            Assert.DoesNotContain("/LZWDecode", pdfText);
        }

        [Fact]
        public async Task ImageCompressionLossy_StillUsesPassthroughForTransparentGif()
        {
            // JPEG can't represent GIF's transparency at all, so Lossy doesn't apply to a transparent
            // source - mirrors PngPassthroughIntegrationTests.ImageCompressionLossy_StillUsesPassthroughForTrnsTransparentPng.
            var bytes = RasterGifFixture.MakeTransparentGifBytes(16, 16);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"16\" height=\"16\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ImageCompression = ImageCompression.Lossy,
            });

            Assert.DoesNotContain("/DCTDecode", pdfText);
            Assert.Contains("/LZWDecode", pdfText);
        }

        [Fact]
        public async Task ImageCompressionAuto_StaysPassthroughEvenWhenDisplayedSmaller()
        {
            // Mirrors IsPngPinnedToNaturalSize's Auto behavior: every pass-through-eligible source is
            // pinned to its natural size under Auto - unlike the plain "lossless format, no pass-through
            // mechanism" case (PngPassthroughIntegrationTests.ImageCompressionAuto_StillJpegEncodesDownscaledBmp),
            // a source with an actual pass-through mechanism keeps it regardless of on-page display size.
            var bytes = RasterGifFixture.MakeFullPaletteGifBytes(40, 40);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"10\" height=\"10\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/LZWDecode", pdfText);
            Assert.DoesNotContain("/DCTDecode", pdfText);
            Assert.Contains("/Width 40", pdfText);
            Assert.Contains("/Height 40", pdfText);
        }

        [Fact]
        public async Task ImageCompressionLossless_ResizesEligibleGifInsteadOfPassthrough()
        {
            var bytes = RasterGifFixture.MakeFullPaletteGifBytes(40, 40);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"10\" height=\"10\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ImageCompression = ImageCompression.Lossless,
            });

            Assert.Contains("/FlateDecode", pdfText);
            Assert.DoesNotContain("/DCTDecode", pdfText);
            Assert.DoesNotContain("/LZWDecode", pdfText);
        }
    }
}

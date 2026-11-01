using PeachPDF.Tests.TestSupport;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Raster
{
    /// <summary>The shadows written into an actual PDF: what is a bitmap, what stays live text, and what a conformance level allows.</summary>
    public class ShadowIntegrationTests
    {
        private static PdfGenerateConfig Config(PdfAConformance conformance = PdfAConformance.None) => new()
        {
            PageSize = PageSize.A4,
            CompressContentStreams = false,
            PdfAConformance = conformance,
            MarginLeft = 20,
            MarginTop = 20,
            Metadata = new PdfDocumentMetadata { CreationDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) },
        };

        private static int Placed(string pdf) =>
            Regex.Matches(pdf, @"q\s+[-\d.]+\s+0\s+0\s+[-\d.]+\s+[-\d.]+\s+[-\d.]+\s+cm\s+/I\d+\s+Do\s+Q").Count;

        private static string Page(string style) =>
            $"<html><body><p style=\"font:24pt Arial;{style}\">Shadowed text</p></body></html>";

        [Fact]
        public async Task BlurredTextShadow_IsABitmap_ButTheTextStaysLiveText()
        {
            var pdf = await PdfObjectReader.GeneratePdf(Page("text-shadow: 2px 2px 4px #888"), Config());

            // One bitmap per word ("Shadowed", "text"): the shadow. The text itself is still shown with Tj/TJ operators.
            Assert.Equal(2, Placed(pdf));
            Assert.Matches(@"\b(Tj|TJ)\b", pdf);
        }

        [Fact]
        public async Task HardTextShadow_StaysVector()
        {
            var pdf = await PdfObjectReader.GeneratePdf(Page("text-shadow: 2px 2px 0 #888"), Config());

            Assert.Equal(0, Placed(pdf));
        }

        [Fact]
        public async Task NoShadow_IsUnchanged()
        {
            var pdf = await PdfObjectReader.GeneratePdf(Page(""), Config());

            Assert.Equal(0, Placed(pdf));
        }

        [Fact]
        public async Task BlurredBoxShadow_IsOneBitmapUnderAVectorBox()
        {
            const string html = "<html><body><div style=\"margin:30px;width:100px;height:60px;background:#fff;border:1px solid #000;box-shadow:0 4px 12px rgba(0,0,0,.4)\"></div></body></html>";

            var pdf = await PdfObjectReader.GeneratePdf(html, Config());

            Assert.Equal(1, Placed(pdf));
        }

        [Fact]
        public async Task ZeroBlurBoxShadow_StaysVector_AndIsLegalUnderPdfA1()
        {
            const string html = "<html><body><div style=\"margin:30px;width:100px;height:60px;background:#fff;box-shadow:6px 6px 0 #000\"></div></body></html>";

            var pdf = await PdfObjectReader.GeneratePdf(html, Config(PdfAConformance.PdfA1B));

            Assert.Equal(0, Placed(pdf));
        }

        [Theory]
        [InlineData("text-shadow: 0 0 4px #000")]
        [InlineData("filter: drop-shadow(2px 2px 0 #000)")]
        [InlineData("box-shadow: 0 0 8px #000")]
        public async Task BitmapShadows_AreRejectedUnderPdfA1(string style)
        {
            var html = $"<html><body><div style=\"width:60px;height:40px;background:#fff;font:14pt Arial;{style}\">x</div></body></html>";

            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => PdfObjectReader.GeneratePdf(html, Config(PdfAConformance.PdfA1B)));
        }

        [Fact]
        public async Task BitmapShadows_AreAllowedUnderPdfA2()
        {
            var pdf = await PdfObjectReader.GeneratePdf(Page("text-shadow: 0 0 4px #000"), Config(PdfAConformance.PdfA2B));

            Assert.True(Placed(pdf) > 0);
        }
    }
}

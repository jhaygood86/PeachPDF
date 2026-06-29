using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class BackgroundImageLayersIntegrationTests
    {
        private static string BoxHtml(string css) =>
            $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }} div {{ width: 200px; height: 100px; {css} }}</style></head><body><div></div></body></html>";

        private static async Task<string> GetPdfText(string html)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Fact]
        public async Task TwoGradientLayers_BothRenderShading()
        {
            var pdfText = await GetPdfText(BoxHtml(
                "background-image: linear-gradient(to right, red, blue), linear-gradient(to bottom, green, yellow);"));

            // Both gradient layers should produce shading entries
            var count = 0;
            var idx = 0;
            while ((idx = pdfText.IndexOf("/ShadingType", idx)) >= 0) { count++; idx++; }
            Assert.True(count >= 2, $"Expected at least 2 /ShadingType entries, found {count}");
        }

        [Fact]
        public async Task GradientLayerOverMissingUrl_GradientStillRenders()
        {
            // The URL image doesn't exist — the gradient layer should still paint
            var pdfText = await GetPdfText(BoxHtml(
                "background-image: linear-gradient(to right, red, blue), url('nonexistent.png');"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ThreeGradientLayers_RendersWithoutError()
        {
            var pdfText = await GetPdfText(BoxHtml(
                "background-image: linear-gradient(red, blue), radial-gradient(circle, green, yellow), conic-gradient(from 0deg, red, blue);"));

            Assert.NotEmpty(pdfText);
            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task BackgroundImageNone_ProducesNoGradient()
        {
            var pdfText = await GetPdfText(BoxHtml("background-image: none;"));

            Assert.DoesNotContain("/ShadingType", pdfText);
        }

        [Fact]
        public async Task GradientLayerMixedWithNone_GradientStillRenders()
        {
            var pdfText = await GetPdfText(BoxHtml(
                "background-image: linear-gradient(to right, red, blue), none;"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task MissingUrlOnly_RendersWithoutCrash()
        {
            // A single URL image that doesn't exist should produce a valid PDF with no crash
            var pdfText = await GetPdfText(BoxHtml("background-image: url('nonexistent.png');"));

            Assert.NotEmpty(pdfText);
        }

        [Fact]
        public async Task HtmlBackgroundAttribute_RendersWithoutCrash()
        {
            // The HTML background="" attribute is set via DomParser as CssImage.Url
            var html = "<!DOCTYPE html><html><body><div background=\"nonexistent.png\" style=\"width:200px;height:100px;\"></div></body></html>";

            var pdfText = await GetPdfText(html);

            Assert.NotEmpty(pdfText);
        }
    }
}

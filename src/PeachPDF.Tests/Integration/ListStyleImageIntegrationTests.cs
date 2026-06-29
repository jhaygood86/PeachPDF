using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class ListStyleImageIntegrationTests
    {
        private static string ListHtml(string css) =>
            $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }} ul {{ {css} }}</style></head><body><ul><li>Item one</li><li>Item two</li></ul></body></html>";

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
        public async Task ListStyleImage_MissingUrl_RendersWithoutCrash()
        {
            // list-style-image is now CssImage? — a missing URL should not throw
            var pdfText = await GetPdfText(ListHtml("list-style-image: url('nonexistent.png');"));

            Assert.NotEmpty(pdfText);
        }

        [Fact]
        public async Task ListStyleImage_None_RendersDefaultBullet()
        {
            var pdfText = await GetPdfText(ListHtml("list-style-image: none;"));

            Assert.NotEmpty(pdfText);
        }

        [Fact]
        public async Task ListStyleShorthand_WithUrl_RendersWithoutCrash()
        {
            // list-style shorthand parsing was updated to store CssImage? on the box
            var pdfText = await GetPdfText(ListHtml("list-style: inside url('nonexistent.png');"));

            Assert.NotEmpty(pdfText);
        }

        [Fact]
        public async Task ListStyleShorthand_InsideDisc_RendersNormally()
        {
            var pdfText = await GetPdfText(ListHtml("list-style: inside disc;"));

            Assert.NotEmpty(pdfText);
        }

        [Fact]
        public async Task ListStyleImage_LinearGradient_RendersShading()
        {
            var pdfText = await GetPdfText(ListHtml("list-style-image: linear-gradient(to right, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ListStyleImage_RadialGradient_RendersShading()
        {
            var pdfText = await GetPdfText(ListHtml("list-style-image: radial-gradient(circle, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ListStyleImage_ConicGradient_RendersShading()
        {
            var pdfText = await GetPdfText(ListHtml("list-style-image: conic-gradient(red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ListStyleShorthand_LinearGradient_RendersShading()
        {
            var pdfText = await GetPdfText(ListHtml("list-style: inside linear-gradient(to bottom, green, yellow);"));

            Assert.Contains("/ShadingType", pdfText);
        }
    }
}

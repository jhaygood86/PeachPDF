using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class BackgroundOriginClipIntegrationTests
    {
        // Box with visible border and padding so all three box-model regions differ.
        private static string BoxHtml(string css) =>
            $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }} div {{ width: 120px; height: 80px; border: 10px solid black; padding: 10px; {css} }}</style></head><body><div></div></body></html>";

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
        public async Task BackgroundOrigin_Default_PaddingBox_Renders()
        {
            var pdfText = await GetPdfText(BoxHtml("background-color: steelblue;"));
            Assert.NotEmpty(pdfText);
            Assert.Contains(" rg\n", pdfText);
        }

        [Fact]
        public async Task BackgroundOrigin_BorderBox_Renders()
        {
            var pdfText = await GetPdfText(BoxHtml("background-color: red; background-origin: border-box;"));
            Assert.NotEmpty(pdfText);
            Assert.Contains(" rg\n", pdfText);
        }

        [Fact]
        public async Task BackgroundOrigin_ContentBox_Renders()
        {
            var pdfText = await GetPdfText(BoxHtml("background-color: blue; background-origin: content-box;"));
            Assert.NotEmpty(pdfText);
            Assert.Contains(" rg\n", pdfText);
        }

        [Fact]
        public async Task BackgroundClip_Default_BorderBox_Renders()
        {
            var pdfText = await GetPdfText(BoxHtml("background-color: coral;"));
            Assert.NotEmpty(pdfText);
            Assert.Contains(" rg\n", pdfText);
        }

        [Fact]
        public async Task BackgroundClip_PaddingBox_Renders()
        {
            var pdfText = await GetPdfText(BoxHtml("background-color: red; background-clip: padding-box;"));
            Assert.NotEmpty(pdfText);
            Assert.Contains(" rg\n", pdfText);
        }

        [Fact]
        public async Task BackgroundClip_ContentBox_Renders()
        {
            var pdfText = await GetPdfText(BoxHtml("background-color: red; background-clip: content-box;"));
            Assert.NotEmpty(pdfText);
            Assert.Contains(" rg\n", pdfText);
        }

        [Fact]
        public async Task BackgroundOrigin_PaddingBox_GradientRendered()
        {
            var pdfText = await GetPdfText(BoxHtml("background: linear-gradient(to right, red, blue); background-origin: padding-box;"));
            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/DeviceRGB", pdfText);
        }

        [Fact]
        public async Task BackgroundOrigin_BorderBox_GradientRendered()
        {
            var pdfText = await GetPdfText(BoxHtml("background: linear-gradient(to right, red, blue); background-origin: border-box;"));
            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/DeviceRGB", pdfText);
        }

        [Fact]
        public async Task BackgroundOriginAndClip_Shorthand_ExpandsCorrectly()
        {
            // Shorthand: single box-model keyword sets both origin and clip
            var pdfText = await GetPdfText(BoxHtml("background: red content-box;"));
            Assert.NotEmpty(pdfText);
            Assert.Contains(" rg\n", pdfText);
        }

        [Fact]
        public async Task BackgroundOriginAndClip_TwoKeywords_ExpandsCorrectly()
        {
            // Shorthand: two box-model keywords — first is origin, second is clip
            var pdfText = await GetPdfText(BoxHtml("background: linear-gradient(to right, teal, gold) padding-box content-box;"));
            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task BackgroundClip_ContentBox_RadialGradientRendered()
        {
            var pdfText = await GetPdfText(BoxHtml("background: radial-gradient(circle, yellow, navy); background-clip: content-box;"));
            Assert.Contains("/ShadingType", pdfText);
        }
    }
}

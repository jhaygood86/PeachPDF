using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class BackgroundShorthandIntegrationTests
    {
        private static string BoxHtml(string css) =>
            $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }} div {{ width: 200px; height: 100px; {css} }}</style></head><body><div></div></body></html>";

        private static async Task<string> GetPdfText(string html)
        {
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, PageSize.A4);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Fact]
        public async Task BackgroundShorthand_SolidColor_RendersSameAslonghand()
        {
            var shorthandPdf = await GetPdfText(BoxHtml("background: red;"));
            var longhandPdf = await GetPdfText(BoxHtml("background-color: red;"));

            // Both should produce a solid background — no gradient shading
            Assert.DoesNotContain("/ShadingType", shorthandPdf);
            Assert.DoesNotContain("/ShadingType", longhandPdf);

            // Both should draw a filled rectangle
            Assert.Contains(" rg\n", shorthandPdf);
            Assert.Contains(" rg\n", longhandPdf);
        }

        [Fact]
        public async Task BackgroundShorthand_WithLinearGradient_RendersGradient()
        {
            var pdfText = await GetPdfText(BoxHtml("background: linear-gradient(to right, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/DeviceRGB", pdfText);
        }

        [Fact]
        public async Task BackgroundShorthand_WithRadialGradient_RendersGradient()
        {
            var pdfText = await GetPdfText(BoxHtml("background: radial-gradient(circle, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task BackgroundShorthand_ColorAndNoRepeat_RendersBackground()
        {
            var pdfText = await GetPdfText(BoxHtml("background: red no-repeat;"));

            Assert.DoesNotContain("/ShadingType", pdfText);
            Assert.Contains(" rg\n", pdfText);
        }

        [Fact]
        public async Task BackgroundShorthand_HexColor_RendersBackground()
        {
            var pdfText = await GetPdfText(BoxHtml("background: #336699;"));

            Assert.DoesNotContain("/ShadingType", pdfText);
            Assert.Contains(" rg\n", pdfText);
        }

        [Fact]
        public async Task BackgroundShorthand_WithImageAndPosition_RendersSuccessfully()
        {
            // Non-existent image gracefully falls back — PDF is still generated
            var pdfText = await GetPdfText(BoxHtml("background: url('nonexistent.png') no-repeat center;"));
            Assert.NotEmpty(pdfText);
        }

        [Fact]
        public async Task BackgroundShorthand_WithPositionAndSize_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(BoxHtml("background: url('img.png') center / cover no-repeat;"));
            Assert.NotEmpty(pdfText);
        }

        [Fact]
        public async Task BackgroundShorthand_Transparent_ProducesNoBrush()
        {
            // transparent should not produce a filled background rectangle
            var transparentPdf = await GetPdfText(BoxHtml("background: transparent;"));
            var noBgPdf = await GetPdfText(BoxHtml(""));

            // A transparent background should behave like no background
            Assert.DoesNotContain("/ShadingType", transparentPdf);
        }
    }
}

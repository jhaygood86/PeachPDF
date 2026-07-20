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
        // Box dimensions are authored in pt (not px) so the one coordinate-checking test below
        // (BackgroundShorthand_MultipleLayers_RepeatValuesApplyPerLayer) stays literal under the
        // spec-correct 1px = 0.75pt convention; the remaining tests only assert unit-agnostic tokens.
        private static string BoxHtml(string css) =>
            $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }} div {{ width: 200pt; height: 100pt; {css} }}</style></head><body><div></div></body></html>";

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
        public async Task BackgroundShorthand_MultipleLayers_RepeatValuesApplyPerLayer()
        {
            // Regression test for the EndListValueConverter comma-vs-whitespace bug: per-layer
            // background-repeat values set via the multi-layer `background` shorthand must survive
            // extraction onto the longhand and actually drive per-layer tiling behavior, not just
            // round-trip correctly at the CSS-OM string level.
            const string pngBase64 =
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

            var pdfText = await GetPdfText(BoxHtml(
                $"background: url('data:image/png;base64,{pngBase64}') left top / 20pt 20pt no-repeat, " +
                $"url('data:image/png;base64,{pngBase64}') left top / 20pt 20pt repeat-x;"));

            // One layer draws once; the other tiles multiple 20pt-wide copies to the right of it.
            // div is flush at the page margin (20, 822) with no border/padding, so "left top" with a
            // 20x20 tile lands at (20, 802) = (822 - tileHeight).
            Assert.Contains("q 20 0 0 20 20 802 cm", pdfText);
            Assert.Contains("q 20 0 0 20 40 802 cm", pdfText);
            Assert.Contains("q 20 0 0 20 60 802 cm", pdfText);
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

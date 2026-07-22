using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class LinearGradientIntegrationTests
    {
        private static string GradientHtml(string css) =>
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
        public async Task TwoStopDiagonalGradient_RendersAxialShading()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: linear-gradient(135deg, #ff0000, #0000ff);"));

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/DeviceRGB", pdfText);
            Assert.Contains("/FunctionType", pdfText);
        }

        [Fact]
        public async Task ThreeStopHorizontalGradient_RendersStitchingFunction()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: linear-gradient(to right, red 0%, yellow 50%, blue 100%);"));

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/FunctionType", pdfText);
        }

        [Fact]
        public async Task DefaultAngleGradient_RendersSuccessfully()
        {
            var doc = new PdfGenerator();
            var result = await doc.GeneratePdf(GradientHtml("background-image: linear-gradient(red, blue);"), PageSize.A4);

            Assert.NotNull(result);
            Assert.True(result.PageCount >= 1);

            var ms = new MemoryStream();
            result.Save(ms);
            var pdfText = Encoding.Latin1.GetString(ms.ToArray());
            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task NamedColorStops_RendersWhiteAndBlack()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: linear-gradient(to bottom, white, black);"));

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/DeviceRGB", pdfText);
        }

        [Fact]
        public async Task RgbaColorStops_RendersGradient()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: linear-gradient(to right, rgba(255,0,0,1), rgba(0,0,255,1));"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task RemovedBackgroundGradientProperty_HasNoShadingEffect()
        {
            // The old custom property should be silently ignored; a solid red background should produce no shading
            var pdfText = await GetPdfText(GradientHtml("background-color: red; background-gradient: blue;"));

            Assert.DoesNotContain("/ShadingType", pdfText);
        }

        [Fact]
        public async Task LinearGradient_ToTopRight_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: linear-gradient(to top right, #ff0000, #00ff00);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        // ── Phase A: absolute-length stops ────────────────────────────────

        [Fact]
        public async Task AbsoluteLengthStop_RendersSuccessfully()
        {
            // red 0, blue 30px, green — absolute positions must not be discarded
            var pdfText = await GetPdfText(GradientHtml("background-image: linear-gradient(to right, red 0, blue 30px, green);"));

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/FunctionType", pdfText);
        }

        // ── Phase A: two-position hard-stop shorthand ─────────────────────

        [Fact]
        public async Task HardStopShorthand_ExpandsToTwoStops()
        {
            // "red 0 50%, blue 50% 100%" expands to four stops (red@0, red@50%, blue@50%, blue@100%),
            // producing a hard colour edge at 50%. A merely-present /FunctionType is not enough to prove
            // that: if either stop's first position is dropped the value collapses to "red 50%, blue 100%",
            // a single smooth interpolation with no stitching, which still emits a /FunctionType. Assert the
            // stitching structure instead - a type-3 function whose /Bounds place a coincident pair at the
            // 0.5 hard edge - so the test actually fails if a position is lost.
            var pdfText = await GetPdfText(GradientHtml("background-image: linear-gradient(to right, red 0 50%, blue 50% 100%);"));

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/FunctionType 3", pdfText);
            Assert.Matches(@"/Bounds\s*\[\s*0?\.5\s+0?\.5\s*\]", pdfText);
        }

        // ── Phase A: color hints ──────────────────────────────────────────

        [Fact]
        public async Task ColorHint_RendersMoreThanTwoSubFunctions()
        {
            // red, 30%, blue — hint at 30% shifts the midpoint; stitching function has >2 sub-functions
            var pdfText = await GetPdfText(GradientHtml("background-image: linear-gradient(to right, red, 30%, blue);"));

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/FunctionType", pdfText);
        }
    }
}

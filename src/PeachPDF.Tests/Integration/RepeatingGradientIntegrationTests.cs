using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class RepeatingGradientIntegrationTests
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

        // ── repeating-linear-gradient ──────────────────────────────────────

        [Fact]
        public async Task RepeatingLinearGradient_RendersShading()
        {
            var pdfText = await GetPdfText(GradientHtml(
                "background-image: repeating-linear-gradient(to right, red 0, red 10px, blue 10px, blue 20px);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task RepeatingLinearGradient_HasNoExtend()
        {
            // Repeating gradients must emit /Extend [false false], not [true true]
            var pdfText = await GetPdfText(GradientHtml(
                "background-image: repeating-linear-gradient(to right, red 0, blue 20px);"));

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("[false false]", pdfText);
            Assert.DoesNotContain("[true true]", pdfText);
        }

        [Fact]
        public async Task RepeatingLinearGradient_MultipleSubFunctions()
        {
            // Tiled stops produce a stitching function with many sub-functions
            var pdfText = await GetPdfText(GradientHtml(
                "background-image: repeating-linear-gradient(to right, red 0, red 10px, blue 10px, blue 20px);"));

            Assert.Contains("/FunctionType", pdfText);
        }

        [Fact]
        public async Task RepeatingLinearGradient_FullSpan_RendersLikeNormal()
        {
            // Stops spanning 0–100% — no tiling needed; should still render
            var pdfText = await GetPdfText(GradientHtml(
                "background-image: repeating-linear-gradient(to right, red 0%, blue 100%);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        // ── repeating-radial-gradient ──────────────────────────────────────

        [Fact]
        public async Task RepeatingRadialGradient_RendersShading()
        {
            var pdfText = await GetPdfText(GradientHtml(
                "background-image: repeating-radial-gradient(circle, red 0, blue 30px);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task RepeatingRadialGradient_HasNoExtend()
        {
            var pdfText = await GetPdfText(GradientHtml(
                "background-image: repeating-radial-gradient(circle, red 0, blue 30px);"));

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("[false false]", pdfText);
            Assert.DoesNotContain("[true true]", pdfText);
        }

        [Fact]
        public async Task RepeatingRadialGradient_WithAlpha_RendersSoftMask()
        {
            var pdfText = await GetPdfText(GradientHtml(
                "background-image: repeating-radial-gradient(circle, rgba(255,0,0,1) 0, rgba(255,0,0,0) 20px);"));

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/SMask", pdfText);
        }

        [Fact]
        public async Task RepeatingRadialGradient_NoShapeKeyword_HardStops_RendersShading()
        {
            // Regression: the CSS validator expands named colors to rgb() function form, so
            // ParseRadialGradient receives "rgb(255, 0, 0) 8px" as the first stop group.
            // The FunctionToken for rgb() was not recognised as a color indicator, so "8px"
            // was mistakenly collected as an explicit gradient radius, leaving fewer than
            // 2 stop groups and causing ParseRadialGradient to return null (blank render).
            var pdfText = await GetPdfText(GradientHtml(
                "background-image: repeating-radial-gradient(red 0 8px, white 8px 16px);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task RepeatingRadialGradient_RgbNotation_HardStops_RendersShading()
        {
            // Regression (direct): explicit rgb() notation in the original CSS hits the same
            // code path as the validator-expanded form above.  Without the FunctionToken guard
            // in ParseRadialGradient, "8px" after "rgb(...)" was read as an explicit radius.
            var pdfText = await GetPdfText(GradientHtml(
                "background-image: repeating-radial-gradient(rgb(255,0,0) 0 8px, rgb(255,255,255) 8px 16px);"));

            Assert.Contains("/ShadingType", pdfText);
        }
    }
}

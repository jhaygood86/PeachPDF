using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class ConicGradientIntegrationTests
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

        // ── Basic conic-gradient ───────────────────────────────────────────

        [Fact]
        public async Task SimpleConicGradient_RendersType4Shading()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: conic-gradient(red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ConicGradientThreeStops_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: conic-gradient(red, yellow, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ConicGradient_FromAngle_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: conic-gradient(from 90deg, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ConicGradient_AtPosition_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: conic-gradient(at 25% 75%, red, green, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ConicGradient_FromAndAt_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: conic-gradient(from 45deg at 30% 70%, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        // ── Angular stop positions ─────────────────────────────────────────

        [Fact]
        public async Task ConicGradient_AngleStops_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: conic-gradient(red 0deg, blue 180deg, green 360deg);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ConicGradient_PercentStops_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: conic-gradient(red 0%, blue 50%, green 100%);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ConicGradient_HardStop_RendersSuccessfully()
        {
            // Hard stop: same angle for end of one slice and start of next
            var pdfText = await GetPdfText(GradientHtml("background-image: conic-gradient(red 0 90deg, blue 90deg 180deg, green 180deg 360deg);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        // ── Alpha transparency ─────────────────────────────────────────────

        [Fact]
        public async Task ConicGradient_WithAlpha_RendersSoftMask()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: conic-gradient(rgba(255,0,0,0), red);"));

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/SMask", pdfText);
        }

        [Fact]
        public async Task ConicGradient_FullyOpaque_HasNoSoftMask()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: conic-gradient(red, blue);"));

            Assert.DoesNotContain("/SMask", pdfText);
        }

        // ── Repeating conic-gradient ───────────────────────────────────────

        [Fact]
        public async Task RepeatingConicGradient_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: repeating-conic-gradient(red 0deg 30deg, blue 30deg 60deg);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task RepeatingConicGradient_FromAngle_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: repeating-conic-gradient(from 45deg, red 0deg, blue 60deg);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task RepeatingConicGradient_BareZeroPosition_RendersShading()
        {
            // Regression: bare "0" (no unit) is valid CSS for a zero angle/position but was
            // not recognised by TryParseConicAngle, causing "#000 0 25%" to leave "0" in the
            // color token list, producing an invalid color string and dropping the stop.
            var pdfText = await GetPdfText(GradientHtml(
                "background-image: repeating-conic-gradient(#000 0 25%, #fff 25% 50%);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        // ── Regression / co-existence ──────────────────────────────────────

        [Fact]
        public async Task ConicAndLinearOnSamePage_BothRender()
        {
            var html =
                "<!DOCTYPE html><html><head><style>body { margin: 0; }" +
                ".a { width: 200px; height: 80px; background-image: linear-gradient(to right, red, blue); }" +
                ".b { width: 200px; height: 80px; background-image: conic-gradient(red, blue); }" +
                "</style></head><body><div class=\"a\"></div><div class=\"b\"></div></body></html>";

            var pdfText = await GetPdfText(html);

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ConicGradientDocument_HasAtLeastOnePage()
        {
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(
                GradientHtml("background-image: conic-gradient(red, green, blue);"),
                PageSize.A4);

            Assert.NotNull(doc);
            Assert.True(doc.PageCount >= 1);
        }
    }
}

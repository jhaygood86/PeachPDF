using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class RadialGradientIntegrationTests
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

        // ── Basic rendering ────────────────────────────────────────────────

        [Fact]
        public async Task SimpleEllipseGradient_RendersRadialShading()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/DeviceRGB", pdfText);
            Assert.Contains("/FunctionType", pdfText);
        }

        [Fact]
        public async Task CircleGradient_RendersRadialShading()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(circle, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/DeviceRGB", pdfText);
        }

        [Fact]
        public async Task EllipseGradient_UsesNonIdentityPatternMatrix()
        {
            // A 200×100 box produces an ellipse with radiusX ≠ radiusY,
            // so the pattern matrix must not be the identity [1 0 0 1 0 0].
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(ellipse, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
            // The identity matrix would appear as [1 0 0 1 0 0]; a scaled ellipse matrix
            // has non-unit diagonal entries that show up as decimal numbers.
            Assert.DoesNotContain("/Matrix [1 0 0 1 0 0]", pdfText);
        }

        [Fact]
        public async Task CircleGradient_UsesCircularCoords()
        {
            // A circle gradient for a 200×100 box should use identical radii in x and y.
            // The shading coords for a circle are [cx cy 0 cx cy r] (not unit-circle).
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(circle, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
            // The unit-circle string [0 0 0 0 0 1] is only used for ellipses.
            // For circles the coords contain actual page-space coordinates.
            Assert.DoesNotContain("[0 0 0 0 0 1]", pdfText);
        }

        // ── Position ───────────────────────────────────────────────────────

        [Fact]
        public async Task GradientWithPercentPosition_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(at 25% 25%, yellow, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task GradientWithKeywordPosition_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(circle at center, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task GradientWithCornerPosition_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(circle at left bottom, gold, crimson);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        // ── Multi-stop ─────────────────────────────────────────────────────

        [Fact]
        public async Task ThreeStopRadialGradient_RendersStitchingFunction()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(red, yellow, blue);"));

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/FunctionType", pdfText);
        }

        [Fact]
        public async Task FiveStopRadialGradient_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(circle, red, orange, yellow, green, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task PositionedStopRadialGradient_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(#00FFFF 0%, rgba(0,0,255,0) 50%, #0000FF 95%);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        // ── Alpha / transparency ───────────────────────────────────────────

        [Fact]
        public async Task TransparentCenterRadialGradient_RendersSoftMask()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(rgba(255,0,0,0), rgba(255,0,0,1));"));

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/SMask", pdfText);
        }

        [Fact]
        public async Task OpaqueToTransparentRadialGradient_RendersSoftMask()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(rgba(0,0,255,1), rgba(0,0,255,0));"));

            Assert.Contains("/SMask", pdfText);
        }

        [Fact]
        public async Task CircleWithAlpha_RendersSoftMask()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(circle, rgba(255,0,0,0), rgba(255,0,0,1));"));

            Assert.Contains("/SMask", pdfText);
        }

        [Fact]
        public async Task MultiStopWithAlpha_RendersSoftMask()
        {
            var pdfText = await GetPdfText(GradientHtml(
                "background-image: radial-gradient(circle, rgba(255,0,0,1), rgba(255,255,0,0.5), rgba(0,0,255,0));"));

            Assert.Contains("/SMask", pdfText);
        }

        [Fact]
        public async Task FullyOpaqueRadialGradient_HasNoSoftMask()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(red, blue);"));

            Assert.DoesNotContain("/SMask", pdfText);
        }

        // ── Regression: no false rendering ────────────────────────────────

        [Fact]
        public async Task SolidColorBackground_ProducesNoShading()
        {
            var pdfText = await GetPdfText(GradientHtml("background-color: red;"));

            Assert.DoesNotContain("/ShadingType", pdfText);
        }

        [Fact]
        public async Task LinearGradient_StillRendersAfterRadialAdded()
        {
            var html =
                "<!DOCTYPE html><html><head><style>body { margin: 0; }" +
                ".lin { width: 200px; height: 80px; background-image: linear-gradient(to right, red, blue); }" +
                ".rad { width: 200px; height: 80px; background-image: radial-gradient(yellow, green); }" +
                "</style></head><body><div class=\"lin\"></div><div class=\"rad\"></div></body></html>";

            var pdfText = await GetPdfText(html);

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/DeviceRGB", pdfText);
        }

        [Fact]
        public async Task RepeatingRadialGradient_RendersLikeSingleCycle()
        {
            // repeating-radial-gradient is rendered as a single-cycle gradient (repetition not yet implemented)
            var pdfText = await GetPdfText(GradientHtml("background-image: repeating-radial-gradient(circle, red, blue 40%);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        // ── Size keywords ─────────────────────────────────────────────────

        [Fact]
        public async Task SizeKeyword_FarthestCorner_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(farthest-corner, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task SizeKeyword_ClosestCorner_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(closest-corner, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task SizeKeyword_FarthestSide_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(farthest-side, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task SizeKeyword_ClosestSide_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(closest-side at 20% 30%, red, yellow, green);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task SizeKeyword_Circle_ClosestSide_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(circle closest-side at 50% 50%, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task SizeKeyword_Circle_FarthestSide_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(circle farthest-side at 50% 50%, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task SizeKeyword_Ellipse_ClosestSide_RendersNonIdentityMatrix()
        {
            // Off-center closest-side ellipse: radii differ from farthest-corner so the
            // pattern matrix must reflect a different (smaller) scale.
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(ellipse closest-side at 30% 40%, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
            Assert.DoesNotContain("/Matrix [1 0 0 1 0 0]", pdfText);
        }

        // ── Phase A: explicit radii ───────────────────────────────────────

        [Fact]
        public async Task ExplicitCircleRadius_RendersSuccessfully()
        {
            // radial-gradient(20px at center, red, blue) — explicit circle radius
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(20px at center, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ExplicitEllipseRadii_RendersSuccessfully()
        {
            // radial-gradient(40px 20px at center, red, blue) — explicit ellipse radii
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(40px 20px at center, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task AbsoluteLengthStops_RadialRendersSuccessfully()
        {
            // absolute-length stop positions in radial gradient
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(circle, red 0, blue 30px, green);"));

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/FunctionType", pdfText);
        }

        [Fact]
        public async Task ColorHint_RadialRendersSuccessfully()
        {
            // color hint between radial gradient stops
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(circle, red, 30%, blue);"));

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/FunctionType", pdfText);
        }

        // ── Edge cases ────────────────────────────────────────────────────

        [Fact]
        public async Task NamedColorStops_RendersCorrectly()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(gold, crimson);"));

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/DeviceRGB", pdfText);
        }

        [Fact]
        public async Task RadialGradientDocument_HasAtLeastOnePage()
        {
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(
                GradientHtml("background-image: radial-gradient(circle, #fff, #000);"),
                PageSize.A4);

            Assert.NotNull(doc);
            Assert.True(doc.PageCount >= 1);
        }
    }
}

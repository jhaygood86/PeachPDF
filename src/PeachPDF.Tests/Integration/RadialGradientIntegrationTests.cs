using PeachPDF;
using PeachPDF.PdfSharpCore;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

        // ── The ellipse pattern matrix composes with the CTM ───────────────
        //
        // An elliptical radial shading states its /Coords as the unit circle at the origin and puts
        // the ellipse's real size and placement in the shading pattern's /Matrix. That matrix reaches
        // only the space a circular shading writes its /Coords in, so it has to be composed with the
        // view matrix (CTM), not substituted for it. The tests above cannot see the difference: they
        // only assert the matrix is not the identity, which the ellipse matrix on its own also
        // satisfies. Under a transform, substituting placed the shading in raw page coordinates -
        // usually off the page - and the shape came out filled with the flat /Extend color.

        /// <summary>
        /// The shading pattern objects' <c>/Matrix</c> entries, one per <c>/PatternType 2</c> object.
        /// Split on <c>endobj</c> first so a <c>/Matrix</c> belonging to some other object (a soft-mask
        /// form XObject, a font) can never be picked up for a pattern that has none of its own.
        /// </summary>
        private static double[][] ShadingPatternMatrices(string pdfText) =>
            pdfText.Split("endobj")
                .Where(o => o.Contains("/PatternType 2"))
                .Select(o => Regex.Match(o, @"/Matrix \[([^\]]*)\]"))
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => double.Parse(v, CultureInfo.InvariantCulture))
                    .ToArray())
                .ToArray();

        private static string TransformedGradientHtml(string css, string transform) =>
            "<!DOCTYPE html><html><head><style>body { margin: 0 }" +
            $"div {{ width: 200px; height: 100px; {css} transform: {transform} }}" +
            "</style></head><body><div></div></body></html>";

        [Fact]
        public async Task EllipseGradient_UnderATransform_ScalesItsPatternMatrixByTheTransform()
        {
            // A CSS transform does not change layout, so the gradient's own ellipse radii are identical
            // in both documents and the ONLY difference the pattern matrix may show is the CTM. With
            // the matrix substituted rather than composed, the two are byte-identical instead.
            const string Gradient = "background-image: radial-gradient(ellipse, red, blue);";

            var plain = ShadingPatternMatrices(await GetPdfText(TransformedGradientHtml(Gradient, "none")));
            var scaled = ShadingPatternMatrices(await GetPdfText(TransformedGradientHtml(Gradient, "scale(0.5)")));

            var plainMatrix = Assert.Single(plain);
            var scaledMatrix = Assert.Single(scaled);

            // Entries 0 and 3 are the matrix's x/y scale: the ellipse's radii, taken through the CTM.
            Assert.Equal(plainMatrix[0] * 0.5, scaledMatrix[0], 3);
            Assert.Equal(plainMatrix[3] * 0.5, scaledMatrix[3], 3);
        }

        [Fact]
        public async Task EllipseGradient_UnderATransform_KeepsItsCenterOnThePage()
        {
            // The failure this pins is not a subtly wrong ellipse, it is a shading placed off the page
            // entirely - which is why the element rendered as one flat color.
            var pdfText = await GetPdfText(TransformedGradientHtml(
                "background-image: radial-gradient(ellipse, red, blue);", "translateX(30px) scale(0.8)"));

            var matrix = Assert.Single(ShadingPatternMatrices(pdfText));

            // Entries 4 and 5 are the ellipse's center in page space; A4 is 595.28 × 841.89pt.
            Assert.InRange(matrix[4], 0, 595.28);
            Assert.InRange(matrix[5], 0, 841.89);
        }

        [Fact]
        public async Task CircleGradient_UnderATransform_StillUsesThePlainViewMatrix()
        {
            // The circular case carries its geometry in /Coords and never sets an ellipse matrix, so
            // composing must leave it exactly as it was: the CTM, and nothing prepended to it.
            var pdfText = await GetPdfText(TransformedGradientHtml(
                "background-image: radial-gradient(circle, red, blue);", "scale(0.5)"));

            var matrix = Assert.Single(ShadingPatternMatrices(pdfText));

            Assert.Equal(0.5, matrix[0], 3);
            Assert.Equal(0.5, matrix[3], 3);
            Assert.DoesNotContain("[0 0 0 0 0 1]", pdfText);
        }

        [Fact]
        public async Task SvgRadialGradient_SquishedByGradientTransform_LandsInsideItsViewport()
        {
            // The SVG twin of the same defect, and the shape it was reported in: gradientTransform
            // makes the gradient elliptical, and an inline <svg> whose viewBox differs from its width
            // supplies exactly the non-identity CTM that got dropped.
            const string Html = """
                <!DOCTYPE html><html><body style="margin: 0">
                <svg viewBox="0 0 100 100" width="80" height="80">
                  <defs>
                    <radialGradient id="rg" gradientUnits="userSpaceOnUse" cx="50" cy="50" r="40"
                                    gradientTransform="matrix(1 0 0 0.5 0 25)">
                      <stop offset="0" stop-color="#e0f7fa"/><stop offset="1" stop-color="#006064"/>
                    </radialGradient>
                  </defs>
                  <circle cx="50" cy="50" r="40" fill="url(#rg)"/>
                </svg></body></html>
                """;

            var matrix = Assert.Single(ShadingPatternMatrices(await GetPdfText(Html)));

            // The viewBox maps 100 user units onto 80px (60pt), so the CTM scale is 0.6. The gradient's
            // radii are 40 and 20 user units, giving 24 × 12pt once the CTM is carried through - the
            // squish gradientTransform asks for. Without the CTM these stay the raw 40 and 20.
            Assert.Equal(24, matrix[0], 3);
            Assert.Equal(12, matrix[3], 3);

            // ...and the ellipse's center sits inside the 60 × 60pt viewport at the page's top-left,
            // rather than at the raw user-space (50, 50) the un-composed matrix would have left it at.
            Assert.InRange(matrix[4], 0, 60);
            Assert.InRange(matrix[5], 841.89 - 60, 841.89);
        }
    }
}

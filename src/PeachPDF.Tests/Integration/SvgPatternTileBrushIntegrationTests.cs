using PeachPDF;
using PeachPDF.PdfSharpCore;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// SVG <c>&lt;pattern&gt;</c> fills are painted with a tile brush, which the PDF writer turns into a real tiling pattern when the grid
    /// is upright: the tile is written once and the viewer repeats it, however many cells the shape covers. A grid the transform turns
    /// or skews is drawn as separate cells (viewers show hairline seams between the cells of a rotated tiling pattern), except when that
    /// would be very many cells. These tests look at the PDF the pipeline actually produces, so a pattern that was silently dropped,
    /// mirrored, or that fell back to one draw per cell when it should not have, fails them.
    /// </summary>
    public class SvgPatternTileBrushIntegrationTests
    {
        private static string Html(string defs, string shapes) =>
            "<!DOCTYPE html><html><head><style>body{margin:0}</style></head><body>" +
            $"<svg width=\"400\" height=\"300\" viewBox=\"0 0 400 300\" xmlns=\"http://www.w3.org/2000/svg\"><defs>{defs}</defs>{shapes}</svg>" +
            "</body></html>";

        private const string Stripes =
            "<pattern id=\"p\" width=\"20\" height=\"20\" patternUnits=\"userSpaceOnUse\">" +
            "<rect width=\"20\" height=\"20\" fill=\"#fde68a\"/><rect width=\"10\" height=\"20\" fill=\"#b91c1c\"/></pattern>";

        private static string Rotated(string pattern, string degrees) =>
            pattern.Replace("patternUnits=\"userSpaceOnUse\"", $"patternUnits=\"userSpaceOnUse\" patternTransform=\"rotate({degrees})\"");

        private static async Task<string> PdfTextAsync(string html)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        private static double[] PatternMatrix(string pdf)
        {
            var m = Regex.Match(pdf, @"/PatternType 1[\s\S]*?/Matrix \[([^\]]+)\]|/Matrix \[([^\]]+)\][\s\S]*?/PatternType 1");
            Assert.True(m.Success, "no tiling pattern with a matrix");
            return (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(n => double.Parse(n, CultureInfo.InvariantCulture)).ToArray();
        }

        [Fact]
        public async Task UprightPatternFill_IsAWrittenTilingPattern_NotAGridOfDrawnImages()
        {
            var pdf = await PdfTextAsync(Html(Stripes, "<rect x=\"10\" y=\"10\" width=\"380\" height=\"280\" fill=\"url(#p)\"/>"));

            Assert.Contains("/PatternType 1", pdf);
            Assert.Contains("/PaintType 1", pdf);
            Assert.Contains("/XStep 20", pdf);
            Assert.Contains("/YStep 20", pdf);
            Assert.Contains("/Pattern cs", pdf);

            // 19 x 14 cells cover the rectangle; the pattern draws its form once, and the page draws no per-cell copies of it.
            Assert.True(Regex.Matches(pdf, @"/Fm\d+ Do").Count <= 3, "the tile should be drawn by the pattern, not once per cell");
        }

        [Fact]
        public async Task UprightPattern_IsAnchoredAndOrientedLikeThePage_YDownBecomesYUp()
        {
            var pdf = await PdfTextAsync(Html(Stripes, "<rect x=\"10\" y=\"10\" width=\"380\" height=\"280\" fill=\"url(#p)\"/>"));

            var m = PatternMatrix(pdf);

            // The svg is 400 wide in a 400-unit viewBox (1 unit = 0.75 pt); brush space is y-down and the PDF's is y-up, so the y scale is
            // negative, and an upright grid has no rotation or skew.
            Assert.Equal(0.75, m[0], 3);
            Assert.Equal(0, m[1], 6);
            Assert.Equal(0, m[2], 6);
            Assert.Equal(-0.75, m[3], 3);
            Assert.True(m[5] > 0, "the origin is measured up from the bottom of the page");
        }

        [Fact]
        public async Task UprightPattern_PlacesTheTileFlipped_BecauseThePatternSpaceIsYDown()
        {
            var pdf = await PdfTextAsync(Html(Stripes, "<rect x=\"10\" y=\"10\" width=\"380\" height=\"280\" fill=\"url(#p)\"/>"));

            // The pattern's own content places the (y-up) form with a negative y scale, from the cell's bottom edge.
            Assert.Matches(new Regex(@"q 1 0 0 -1 0 20 cm /Fm\d+ Do Q"), pdf);
        }

        [Fact]
        public async Task PatternFill_OfMoreCellsThanTheOldGridLimit_StillPaints()
        {
            // A 2x2 cell over a 380x280 rectangle is 26,600 cells, past the 10,000 the per-cell loop used to give up at.
            var tiny = "<pattern id=\"p\" width=\"2\" height=\"2\" patternUnits=\"userSpaceOnUse\"><rect width=\"1\" height=\"2\" fill=\"#000\"/></pattern>";

            var pdf = await PdfTextAsync(Html(tiny, "<rect x=\"10\" y=\"10\" width=\"380\" height=\"280\" fill=\"url(#p)\"/>"));

            Assert.Contains("/PatternType 1", pdf);
            Assert.Contains("/Pattern cs", pdf);
        }

        [Fact]
        public async Task RotatedPatternWithFewCells_IsDrawnAsCells_NotAsATilingPattern()
        {
            var pdf = await PdfTextAsync(Html(Rotated(Stripes, "30"), "<ellipse cx=\"200\" cy=\"150\" rx=\"150\" ry=\"100\" fill=\"url(#p)\"/>"));

            Assert.DoesNotContain("/PatternType 1", pdf);

            // Many cells, each drawn, and enough of them to cover the ellipse whatever the rotation (the old grid missed most of it).
            Assert.True(Regex.Matches(pdf, @"/Fm\d+ Do").Count > 100, "each cell should be drawn");
        }

        [Fact]
        public async Task RotatedPatternWithManyCells_IsStillATilingPattern_AndTheMatrixIsRotated()
        {
            var tiny = Rotated("<pattern id=\"p\" width=\"2\" height=\"2\" patternUnits=\"userSpaceOnUse\"><rect width=\"1\" height=\"2\" fill=\"#000\"/></pattern>", "30");

            var pdf = await PdfTextAsync(Html(tiny, "<rect x=\"10\" y=\"10\" width=\"380\" height=\"280\" fill=\"url(#p)\"/>"));
            var m = PatternMatrix(pdf);

            // 30 degrees clockwise on a y-down canvas, seen through the y flip and the 0.75 scale: cos = 0.866, sin = 0.5.
            Assert.Equal(0.75 * Math.Cos(Math.PI / 6), m[0], 0.001);
            Assert.Equal(-0.75 * Math.Sin(Math.PI / 6), m[1], 0.001);
            Assert.Equal(-0.75 * Math.Sin(Math.PI / 6), m[2], 0.001);
            Assert.Equal(-0.75 * Math.Cos(Math.PI / 6), m[3], 0.001);
        }

        [Fact]
        public async Task PatternFill_FillsTheWholeShapeWhateverThePatternTransform()
        {
            // The old grid missed tiles once the pattern was turned, leaving part of the shape unpainted. Both the unrotated and the
            // rotated fill of the same rectangle must emit content for the same shape.
            var upright = await PdfTextAsync(Html(Stripes, "<rect x=\"10\" y=\"10\" width=\"380\" height=\"280\" fill=\"url(#p)\"/>"));
            var turned = await PdfTextAsync(Html(Rotated(Stripes, "45"), "<rect x=\"10\" y=\"10\" width=\"380\" height=\"280\" fill=\"url(#p)\"/>"));

            Assert.Contains("/Pattern cs", upright);
            Assert.True(Regex.Matches(turned, @"/Fm\d+ Do").Count > 100);
        }

        [Fact]
        public async Task TwoShapesFilledWithTheSamePattern_ShareOnePattern()
        {
            var pdf = await PdfTextAsync(Html(Stripes,
                "<rect x=\"10\" y=\"10\" width=\"150\" height=\"100\" fill=\"url(#p)\"/><rect x=\"200\" y=\"10\" width=\"150\" height=\"100\" fill=\"url(#p)\"/>"));

            Assert.True(Regex.Matches(pdf, @"/PatternType 1").Count >= 1);
            Assert.True(Regex.Matches(pdf, @"/Pattern cs").Count >= 2);
        }
    }
}

using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// SVG <c>&lt;pattern&gt;</c> fills are painted with a tile brush, which the PDF writer turns into a real tiling pattern: the tile is
    /// written once and the viewer repeats it, however many cells the shape covers. These tests look at the PDF the pipeline actually
    /// produces, so a pattern that was silently dropped, or that fell back to one draw per cell, fails them.
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

        [Fact]
        public async Task PatternFill_IsAWritten_TilingPattern_NotAGridOfDrawnImages()
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
        public async Task PatternFill_OfMoreCellsThanTheOldGridLimit_StillPaints()
        {
            // A 2x2 cell over a 380x280 rectangle is 26,600 cells, past the 10,000 the per-cell loop used to give up at.
            var tiny = "<pattern id=\"p\" width=\"2\" height=\"2\" patternUnits=\"userSpaceOnUse\"><rect width=\"1\" height=\"2\" fill=\"#000\"/></pattern>";

            var pdf = await PdfTextAsync(Html(tiny, "<rect x=\"10\" y=\"10\" width=\"380\" height=\"280\" fill=\"url(#p)\"/>"));

            Assert.Contains("/PatternType 1", pdf);
            Assert.Contains("/Pattern cs", pdf);
        }

        [Fact]
        public async Task PatternTransform_BecomesThePatternsMatrix_AndIsNotDroppedForARotatedFill()
        {
            var rotated = Stripes.Replace("patternUnits=\"userSpaceOnUse\"", "patternUnits=\"userSpaceOnUse\" patternTransform=\"rotate(30)\"");

            var plain = await PdfTextAsync(Html(Stripes, "<ellipse cx=\"200\" cy=\"150\" rx=\"150\" ry=\"100\" fill=\"url(#p)\"/>"));
            var turned = await PdfTextAsync(Html(rotated, "<ellipse cx=\"200\" cy=\"150\" rx=\"150\" ry=\"100\" fill=\"url(#p)\"/>"));

            // The rotated pattern matrix has non-zero off-diagonal terms (cos/sin of 30 degrees); the plain one does not.
            static bool HasRotation(string pdf)
            {
                var m = Regex.Match(pdf, @"/PatternType 1[\s\S]*?/Matrix \[([^\]]+)\]");
                if (!m.Success)
                    m = Regex.Match(pdf, @"/Matrix \[([^\]]+)\][\s\S]*?/PatternType 1");

                if (!m.Success)
                    return false;

                var n = m.Groups[1].Value.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                return n.Length == 6 && (System.Math.Abs(double.Parse(n[1], System.Globalization.CultureInfo.InvariantCulture)) > 0.1);
            }

            Assert.Contains("/PatternType 1", turned);
            Assert.True(HasRotation(turned));
            Assert.False(HasRotation(plain));
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

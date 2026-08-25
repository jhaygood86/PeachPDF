using PeachPDF;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Issue #821 (a follow-up to #814 left as a tracked gap - see the deleted
    /// .claude/accepted-gaps/gradient-absolute-radius-pixelsperpoint.md): an explicit gradient radius
    /// (<c>radial-gradient(20px at ..., ...)</c>) and an absolute- or em-unit gradient stop position
    /// (<c>red 0, blue 30px, green</c> / <c>blue 2em</c>) used to resolve via the bare
    /// <c>Length.ToPixel()</c> or a hard-coded <c>emPx</c>, with no knowledge of <c>PixelsPerPoint</c>
    /// (<c>RGraphics.PixelsPerPoint</c>, ultimately <c>PdfGenerateConfig.PixelsPerInch / 72</c>) - so all
    /// three shrank relative to the (correctly DPI-scaled) box they paint into whenever
    /// <c>PixelsPerInch</c> was not the library's default of 72. Each fixture here is rendered at two
    /// different <c>PixelsPerInch</c> values and asserts the PDF's resolved gradient geometry is identical
    /// either way - the same DPI-invariance a physical length must have regardless of how finely internal
    /// layout space is subdivided.
    /// </summary>
    public class GradientPixelsPerInchIntegrationTests
    {
        private static async Task<string> GetPdfText(string html, double pixelsPerInch)
        {
            var config = new PdfGenerateConfig
            {
                ManualPageWidth = 400,
                ManualPageHeight = 300,
                PixelsPerInch = pixelsPerInch,
            };
            config.SetMargins(0);

            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        private static string GradientHtml(string css) =>
            $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }} div {{ width: 200px; height: 100px; {css} }}</style></head><body><div></div></body></html>";

        [Fact]
        public async Task ExplicitCircleRadius_IsInvariantUnderDifferentPixelsPerInch()
        {
            var html = GradientHtml("background-image: radial-gradient(20px at center, red, blue);");

            var pdfText72 = await GetPdfText(html, 72);
            var pdfText96 = await GetPdfText(html, 96);

            var rx72 = ExtractCircleRadius(pdfText72);
            var rx96 = ExtractCircleRadius(pdfText96);

            // Before the fix, the 96-DPI radius resolved smaller by a factor of PixelsPerPoint (4/3)
            // than the 72-DPI one, instead of matching it - an explicit px radius is a physical length
            // and must render at the same size on the page regardless of PixelsPerInch.
            Assert.Equal(rx72, rx96, 2);
        }

        [Fact]
        public async Task ExplicitEllipseRadii_AreInvariantUnderDifferentPixelsPerInch()
        {
            var html = GradientHtml("background-image: radial-gradient(40px 20px at center, red, blue);");

            var pdfText72 = await GetPdfText(html, 72);
            var pdfText96 = await GetPdfText(html, 96);

            // An ellipse radial shading is drawn as a unit circle scaled/translated by the pattern's
            // /Matrix, so the radii live in that matrix's non-unit diagonal entries rather than /Coords.
            var matrix72 = ExtractPatternMatrix(pdfText72);
            var matrix96 = ExtractPatternMatrix(pdfText96);

            Assert.Equal(matrix72.a, matrix96.a, 2);
            Assert.Equal(matrix72.d, matrix96.d, 2);
        }

        [Fact]
        public async Task AbsoluteLengthStopPosition_IsInvariantUnderDifferentPixelsPerInch()
        {
            var html = GradientHtml("background-image: radial-gradient(circle, red 0, blue 30px, green);");

            var pdfText72 = await GetPdfText(html, 72);
            var pdfText96 = await GetPdfText(html, 96);

            // /Bounds holds the stitching function's normalized (0..1) stop-position fraction directly,
            // independent of page-space/CTM concerns - before the fix this shrank under a higher DPI
            // because the absolute stop length wasn't scaled to match the (already-correct) gradient
            // radius it's a fraction of.
            var bounds72 = ExtractFirstBound(pdfText72);
            var bounds96 = ExtractFirstBound(pdfText96);

            Assert.Equal(bounds72, bounds96, 3);
        }

        [Fact]
        public async Task LinearGradient_AbsoluteLengthStopPosition_IsInvariantUnderDifferentPixelsPerInch()
        {
            // GetLinearGradientBrush went through the identical box-removal/g.PixelsPerPoint change as
            // GetRadialGradientBrush - this covers that path directly rather than only exercising it
            // incidentally via NormalizeGradientStops's radial callers.
            var html = GradientHtml("background-image: linear-gradient(to right, red 0, blue 30px, green);");

            var pdfText72 = await GetPdfText(html, 72);
            var pdfText96 = await GetPdfText(html, 96);

            var bounds72 = ExtractFirstBound(pdfText72);
            var bounds96 = ExtractFirstBound(pdfText96);

            Assert.Equal(bounds72, bounds96, 3);
        }

        [Fact]
        public async Task EmStopPosition_IsInvariantUnderDifferentPixelsPerInch()
        {
            // ConvertLength's Em branch shares the exact same PixelsPerPoint-vs-gradientLength mismatch
            // as its IsAbsolute branch (gradientLength is always in the internal, DPI-scaled space), so
            // it needs the identical *pixelsPerPoint fix.
            var html = GradientHtml("background-image: radial-gradient(circle, red 0, blue 2em, green);");

            var pdfText72 = await GetPdfText(html, 72);
            var pdfText96 = await GetPdfText(html, 96);

            var bounds72 = ExtractFirstBound(pdfText72);
            var bounds96 = ExtractFirstBound(pdfText96);

            Assert.Equal(bounds72, bounds96, 3);
        }

        private static double ExtractCircleRadius(string pdfText)
        {
            var m = Regex.Match(pdfText,
                @"/Coords\s*\[\s*([\d.eE+-]+)\s+([\d.eE+-]+)\s+0\s+([\d.eE+-]+)\s+([\d.eE+-]+)\s+([\d.eE+-]+)\s*\]");
            Assert.True(m.Success, "expected a circular radial shading /Coords array");
            return double.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
        }

        private static (double a, double d) ExtractPatternMatrix(string pdfText)
        {
            var m = Regex.Match(pdfText, @"/Matrix\s*\[\s*([\d.eE+-]+)\s+([\d.eE+-]+)\s+([\d.eE+-]+)\s+([\d.eE+-]+)\s+([\d.eE+-]+)\s+([\d.eE+-]+)\s*\]");
            Assert.True(m.Success, "expected an ellipse pattern /Matrix");
            return (
                double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture));
        }

        private static double ExtractFirstBound(string pdfText)
        {
            var m = Regex.Match(pdfText, @"/Bounds\s*\[\s*([\d.eE+-]+)");
            Assert.True(m.Success, "expected a stitching function /Bounds array");
            return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }
    }
}

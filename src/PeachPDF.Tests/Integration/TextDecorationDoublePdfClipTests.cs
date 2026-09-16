using PeachPDF;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>text-decoration-style: double</c> grows an overline's pair upward, so unlike every other
    /// decoration it can reach above the box it belongs to. These tests generate a real PDF and read the
    /// stroke coordinates back out of the content stream, against the page's own clip rectangle -
    /// <c>TestRecordingGraphics</c> records draw calls and never applies clipping, so a stroke that
    /// leaves the page is invisible to every other test in this area.
    /// </summary>
    public class TextDecorationDoublePdfClipTests
    {
        /// <summary>
        /// With ordinary headroom above the text, both strokes of a double overline land inside the
        /// clip and survive into the page.
        /// </summary>
        [Fact]
        public async Task DoubleOverline_WithHeadroomAboveIt_KeepsBothStrokesInsideThePageClip()
        {
            var (clipTop, clipBottom, strokes) = await StrokesAndClipAsync(marginTop: 20);

            Assert.Equal(2, strokes.Count);
            Assert.All(strokes, y => Assert.InRange(y, clipBottom, clipTop));
        }

        /// <summary>
        /// The contrast: the same document with no headroom at all. Both strokes are still emitted - the
        /// painter does not move or drop either - but the upper one falls above the clip and so does not
        /// appear on the page. This is disclosed rather than worked around; see
        /// <c>docs/html-css-support.md</c>. Chrome 141 given the same markup loses the overline entirely,
        /// both strokes and the single-stroke case alike, so nothing here is more lossy than a browser.
        /// </summary>
        [Fact]
        public async Task DoubleOverline_FlushAgainstTheTopOfThePage_EmitsBothButTheUpperFallsOutsideTheClip()
        {
            var (clipTop, _, strokes) = await StrokesAndClipAsync(marginTop: 0);

            Assert.Equal(2, strokes.Count);
            Assert.True(strokes.Max() > clipTop,
                "the upper stroke should be the one above the clip, which is what the docs disclose");
            Assert.True(strokes.Min() <= clipTop,
                "the lower stroke should still be on the page");
        }

        /// <summary>
        /// An underline grows the other way, so it has the whole descender area beneath it and never
        /// raises this question - the control that keeps the test above about the overline's direction
        /// rather than about doubling in general.
        /// </summary>
        [Fact]
        public async Task DoubleUnderline_FlushAgainstTheTopOfThePage_KeepsBothStrokesInsideThePageClip()
        {
            var (clipTop, clipBottom, strokes) = await StrokesAndClipAsync(marginTop: 0, line: "underline");

            Assert.Equal(2, strokes.Count);
            Assert.All(strokes, y => Assert.InRange(y, clipBottom, clipTop));
        }

        /// <summary>
        /// Generates a one-page PDF and returns the page clip's vertical bounds and the y of every
        /// decoration stroke, all in PDF user space (y grows upward, so the clip's top is the larger).
        /// </summary>
        private static async Task<(double ClipTop, double ClipBottom, System.Collections.Generic.List<double> Strokes)>
            StrokesAndClipAsync(int marginTop, string line = "overline")
        {
            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: {marginTop}pt 0 0 0 }}</style></head>" +
                       $"<body><span style='text-decoration:{line} double'>total</span></body></html>";

            var config = new PdfGenerateConfig { PageSize = PageSize.Letter, CompressContentStreams = false };
            config.SetMargins(0);

            var document = await new PdfGenerator().GeneratePdf(html, config);
            using var buffer = new MemoryStream();
            document.Save(buffer);
            var pdf = Encoding.Latin1.GetString(buffer.ToArray());

            var stream = Regex.Match(pdf, @"stream\r?\n(.*?)\r?\nendstream", RegexOptions.Singleline);
            Assert.True(stream.Success, "no content stream in the generated PDF");
            var content = stream.Groups[1].Value;

            // The page's own content clip is a path closed with "W* n", not a "re" rectangle, so its
            // bounds are the extent of the moveto/lineto points that precede the operator.
            var clipEnd = content.IndexOf("W* n", System.StringComparison.Ordinal);
            Assert.True(clipEnd > 0, "no page clip path in the content stream");

            var clipPoints = Regex.Matches(content[..clipEnd], @"[-\d.]+ ([-\d.]+) [ml]\b")
                .Select(m => Number(m.Groups[1].Value))
                .ToList();
            Assert.NotEmpty(clipPoints);

            var clipTop = clipPoints.Max();
            var clipBottom = clipPoints.Min();

            // Each stroke is a horizontal "x y m / x y l / S"; both ends share the y this returns. Read
            // from after the clip so the clip path's own linetos are not mistaken for strokes.
            var strokes = Regex.Matches(content[clipEnd..], @"[-\d.]+ ([-\d.]+) m\s+[-\d.]+ [-\d.]+ l\s+S")
                .Select(m => Number(m.Groups[1].Value))
                .ToList();

            return (clipTop, clipBottom, strokes);
        }

        private static double Number(string value) =>
            double.Parse(value, CultureInfo.InvariantCulture);
    }
}

using PeachDrawing.PDF.Engine;
using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for a real bug found while verifying issue #1117
    /// (<c>background-clip: text</c>) by rendering and rasterizing the feature's own repro, not by any
    /// unit test: <see cref="PeachDrawing.PDF.Engine.Rendering.PdfGraphicsState.RealizeBrush"/>'s
    /// gradient/pattern-brush branch never realized its own fill alpha (<c>/ca</c>) - unlike a solid
    /// color fill, which always does via <c>RealizeFillColor</c>. A gradient/pattern fill with no
    /// semi-transparent stops of its own therefore silently inherited whatever <c>/ca</c> the *previous*
    /// solid-color fill left active in the PDF graphics state - so a <c>color: transparent</c> text draw
    /// (routine right before a <c>background-clip: text</c> element's own gradient fill, and common
    /// wherever two such elements sit next to each other) left the next element's gradient invisible.
    /// This mirrors <see cref="SvgGradientStrokeAlphaTests"/>'s own issue #134 (the identical leak on
    /// the stroke side, already fixed there) - the fill side never got the same guard until now.
    /// The same reset also runs for a gradient WITH semi-transparent stops (its soft mask carries the per-stop alpha, which the leftover /ca used to multiply
    /// a second time, or, after /ca 0 text, wipe out), covered by the tests that look at every gs before a pattern fill.
    /// </summary>
    public class GradientFillAlphaTests
    {
        private static async Task<string> GetPdfText(string bodyHtml, string styleHtml = "")
        {
            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }}{styleHtml}</style></head><body>{bodyHtml}</body></html>";
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        // The bug's own minimal repro shape: a `color: transparent` text draw (h1's own glyphs)
        // immediately followed by another element's gradient fill (h2's background-clip: text) - the
        // ordering that poisoned the realized fill alpha to 0.
        private const string Style = """
            h1, h2 { margin: 0; background: linear-gradient(to right, #e11, #11e); background-clip: text; color: transparent; }
            h1 { font-size: 40pt; } h2 { font-size: 20pt; }
            """;
        private const string Body = "<h1>Revenue</h1><h2>Q4 Highlights</h2>";

        /// <summary>
        /// Resolves a content-stream resource reference like "/GS0" to the body of the indirect PDF
        /// object it names in the page's own resource dictionary ("/GS0 7 0 R" -&gt; object 7's body) -
        /// the resource *name* used in a content stream is never the same thing as the PDF object
        /// *number* that ultimately backs it.
        /// </summary>
        private static string ResolveResourceObjectBody(string pdfText, string resourceName)
        {
            var reference = Regex.Match(pdfText, $@"/{Regex.Escape(resourceName)}\s+(\d+)\s+0\s+R");
            Assert.True(reference.Success, $"expected to find a resource-dictionary entry naming /{resourceName}");

            var objectNumber = reference.Groups[1].Value;
            var body = Regex.Match(pdfText, $@"(?:^|\n){Regex.Escape(objectNumber)}\s+0\s+obj(.*?)endobj", RegexOptions.Singleline);
            Assert.True(body.Success, $"expected to find object {objectNumber} 0 obj this /{resourceName} resource references");
            return body.Groups[1].Value;
        }

        [Fact]
        public async Task GradientFillAfterTransparentTextFill_DoesNotZeroTheFillAlpha()
        {
            var pdfText = await GetPdfText(Body, Style);

            // Every ExtGState referenced immediately before a pattern-fill's own "/Pattern cs ... scn"
            // must not carry "/ca 0" - a zeroed /ca right there is exactly what made the second
            // element's gradient fill invisible.
            var fills = Regex.Matches(pdfText, @"/(GS\d+) gs\r?\n/Pattern cs\r?\n/Pa\d+ scn");
            Assert.NotEmpty(fills);

            foreach (Match fill in fills)
            {
                var gsBody = ResolveResourceObjectBody(pdfText, fill.Groups[1].Value);

                var ca = Regex.Match(gsBody, @"/ca\s+([0-9.]+)");
                if (ca.Success)
                    Assert.NotEqual(0.0, double.Parse(ca.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        [Fact]
        public async Task GradientFillAfterTransparentTextFill_StillEmitsBothShadingPatternFills()
        {
            var pdfText = await GetPdfText(Body, Style);

            // h1's own transparent text draw (its ca-0 "poisoning" gs) must precede h2's own pattern
            // fill for the trigger condition to actually be exercised, and both headings' gradient
            // fills must still be present.
            var transparentTextIndex = pdfText.IndexOf("1 1 1 rg", StringComparison.Ordinal);
            var patternFillIndices = Regex.Matches(pdfText, @"/Pattern cs\r?\n/Pa\d+ scn");

            Assert.True(transparentTextIndex >= 0, "expected h1's own text-show to realize a (transparent) fill color");
            Assert.Equal(2, patternFillIndices.Count);
            Assert.True(transparentTextIndex < patternFillIndices[1].Index,
                "h1's transparent text draw must precede h2's own pattern fill to reproduce the bug");
            Assert.Contains("/ShadingType", pdfText);
        }

        // An SVG shape with a fill-opacity leaves /ca 0.5 behind; the next shape's gradient has a translucent stop.
        private const string FillOpacityThenTranslucentGradient = """
            <svg width="300" height="120" viewBox="0 0 300 120">
              <defs><linearGradient id="g" x1="0" x2="0" y1="0" y2="1"><stop offset="0" stop-color="#f00"/><stop offset="1" stop-color="#f00" stop-opacity=".5"/></linearGradient></defs>
              <rect x="120" y="20" width="10" height="10" fill="#f00" fill-opacity=".5"/>
              <rect width="100" height="100" fill="url(#g)"/>
            </svg>
            """;

        /// <summary>
        /// The ExtGState bodies a fill-side shading pattern is preceded by, one list per pattern fill: the <c>gs</c> operators written right before its
        /// <c>/Pattern cs ... scn</c>, in the order written (a fill alpha reset and a soft mask are independent graphics-state keys, so either order is correct).
        /// </summary>
        private static System.Collections.Generic.List<System.Collections.Generic.List<string>> ExtGStatesBeforePatternFills(string pdfText) =>
            Regex.Matches(pdfText, @"((?:/GS\d+ gs\r?\n)+)/Pattern cs\r?\n/Pa\d+ scn")
                .Select(fill => Regex.Matches(fill.Groups[1].Value, @"/(GS\d+) gs").Select(gs => ResolveResourceObjectBody(pdfText, gs.Groups[1].Value)).ToList())
                .ToList();

        [Fact]
        public async Task TranslucentGradientFillAfterAFillOpacityShape_ResetsTheAmbientFillAlpha()
        {
            // The gradient's per-stop alpha rides its soft mask; the ambient /ca left by the earlier shape (0.5) used to multiply it a second time,
            // so the fill came out about half as strong as its own stops say (Chrome draws it as the stops say).
            var pdfText = await GetPdfText(FillOpacityThenTranslucentGradient);

            var bodies = Assert.Single(ExtGStatesBeforePatternFills(pdfText));
            Assert.Contains(bodies, body => body.Contains("/SMask"));
            Assert.Contains(bodies, body => Regex.Match(body, @"/ca\s+([0-9.]+)") is { Success: true } ca
                && double.Parse(ca.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) == 1.0);
        }

        [Fact]
        public async Task TranslucentGradientFillAfterTransparentTextFill_ResetsTheTextsZeroAlpha()
        {
            // background-clip: text beside color: transparent text: the transparent text leaves /ca 0, which used to carry over onto the next heading's translucent
            // gradient and make it vanish (an opaque one was reset already). The last pattern fill is the translucent one.
            const string style = """
                h1 { margin: 0; font-size: 40pt; background: linear-gradient(to right, #e11, #11e); background-clip: text; color: transparent; }
                h2 { margin: 0; font-size: 20pt; background: linear-gradient(to right, rgba(245,158,11,0.2), #ef4444); background-clip: text; color: transparent; }
                """;

            var pdfText = await GetPdfText(Body, style);

            var bodies = ExtGStatesBeforePatternFills(pdfText).Last();
            Assert.Contains(bodies, body => body.Contains("/SMask"));
            Assert.Contains(bodies, body => Regex.Match(body, @"/ca\s+([0-9.]+)") is { Success: true } ca
                && double.Parse(ca.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) == 1.0);
        }

        [Fact]
        public async Task SemiTransparentGradientFillAfterTransparentTextFill_KeepsItsOwnSoftMaskAlpha()
        {
            // A gradient with a translucent stop still carries real transparency via its own soft-mask
            // ExtGState, which must survive - the fix only stops the *ambient* leftover /ca from a
            // previous fill from leaking in; it must not suppress genuine gradient transparency the
            // pattern's own AlphaExtGState sets. (h1's own transparent *text* legitimately still has its
            // own "/ca 0" elsewhere in the document - that is not what this checks.)
            const string style = """
                h1 { margin: 0; font-size: 40pt; background: linear-gradient(to right, #e11, #11e); background-clip: text; color: transparent; }
                h2 { margin: 0; font-size: 20pt; background: linear-gradient(to right, rgba(245,158,11,0.2), #ef4444); background-clip: text; color: transparent; }
                """;

            var pdfText = await GetPdfText(Body, style);
            Assert.Contains("/SMask", pdfText);

            var fills = Regex.Matches(pdfText, @"/(GS\d+) gs\r?\n/Pattern cs\r?\n/Pa\d+ scn");
            Assert.NotEmpty(fills);

            foreach (Match fill in fills)
            {
                var gsBody = ResolveResourceObjectBody(pdfText, fill.Groups[1].Value);

                var ca = Regex.Match(gsBody, @"/ca\s+([0-9.]+)");
                if (ca.Success)
                    Assert.NotEqual(0.0, double.Parse(ca.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }
}

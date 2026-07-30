using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Real UAX#9 bidi resolution for SVG <c>&lt;text&gt;</c> (SVG 2 §11.3.1: the same
    /// <c>direction</c>/<c>unicode-bidi</c> properties and algorithm CSS text uses). Renders through
    /// <see cref="SvgRenderer.RenderInto"/> into a <see cref="TestRecordingGraphics"/> and asserts on the
    /// actual painted <c>DrawString</c> text - not just that a bidi property parses.
    /// </summary>
    public class SvgTextBidiTests
    {
        private static readonly PdfSharpAdapter Adapter = new() { PixelsPerPoint = 1.0 };

        private static TestRecordingGraphics Render(string body)
        {
            var markup = $$"""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">
                  {{body}}
                </svg>
                """;
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new RRect(0, 0, 200, 100));
            return g;
        }

        [Fact]
        public void Text_DirectionRtl_RealHebrewContent_IsReorderedAndMirrored()
        {
            const string hebrew = "שלום עולם";
            var g = Render($"""<text x="10" y="50" font-size="20" direction="rtl">{hebrew}</text>""");

            var draw = Assert.Single(g.DrawStringCalls);

            // L2 reverses the whole (uniform-level, since it's all strong-R plus a neutral space) run;
            // L4 would additionally mirror any bracket-like characters, of which this text has none.
            var expected = new string(hebrew.Reverse().ToArray());
            Assert.Equal(expected, draw.Text);
        }

        [Fact]
        public void Text_DirectionRtl_PlainLatinContent_StaysLogical()
        {
            // UAX#9 I2 bumps strong-L characters to the next even level inside an RTL (odd) paragraph, so
            // plain Latin text is not reordered even under direction="rtl" - unlike real RTL-script text.
            var g = Render("""<text x="10" y="50" font-size="20" direction="rtl">AB CD</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("AB CD", draw.Text);
        }

        [Fact]
        public void Text_DirectionRtl_MirrorsParentheses()
        {
            const string hebrew = "שלום (עולם)";
            var g = Render($"""<text x="10" y="50" font-size="20" direction="rtl">{hebrew}</text>""");

            var draw = Assert.Single(g.DrawStringCalls);

            // Reversing the whole run then mirroring each character's shape puts an ordinary '(' back at
            // the run's start and ')' back at its end, wrapping the reversed enclosed word - and since
            // reversing the whole line also reverses the two words' relative order, the parenthesized
            // group (originally second/last) ends up first (see the equivalent HTML characterization in
            // ShapingCharacterizationTests).
            var reversedOlam = new string("עולם".Reverse().ToArray());
            Assert.Contains($"({reversedOlam})", draw.Text);
        }

        [Fact]
        public void TSpan_UnicodeBidiIsolate_DoesNotLeakIntoSurroundingLtrText()
        {
            // An isolated RTL tspan's own reordering must not affect how the surrounding LTR text's own
            // words are ordered - mirrors CssLayoutEngineBidiTests' <bdi> isolation test for HTML.
            var g = Render(
                """<text x="10" y="50" font-size="20">before <tspan unicode-bidi="isolate" direction="rtl">שלום עולם</tspan> after</text>""");

            var texts = g.DrawStringCalls.Select(c => c.Text).ToList();

            // "before"/"after" are painted in their own authored (unreversed) form...
            Assert.Contains(texts, t => t.Contains("before"));
            Assert.Contains(texts, t => t.Contains("after"));

            // ...while the isolated tspan's own two words reorder relative to each other: the second
            // logical word (עולם) ends up visually before the first (שלום).
            var hebrewCall = Assert.Single(texts, t => t.Contains('ם') || t.Contains('ש'));
            var reversedOlam = new string("עולם".Reverse().ToArray());
            var reversedShalom = new string("שלום".Reverse().ToArray());
            Assert.True(hebrewCall.IndexOf(reversedOlam, System.StringComparison.Ordinal) < hebrewCall.IndexOf(reversedShalom, System.StringComparison.Ordinal),
                $"expected the isolated tspan's own words to reorder; text='{hebrewCall}'");
        }

        [Fact]
        public void Text_BidiOverrideReversesGlyphsOfDifferingAdvance_NoGapOrOverlap()
        {
            // Reflecting a run about its own content span (each glyph's own Advance and its own offset
            // from the run's start), not reusing the logical-order Px of whichever glyph used to occupy
            // a list position, is what keeps this correct once glyph advances differ within a run - here
            // by giving the two tspans very different font sizes. A regression shows up as a gap or
            // overlap between the two runs, not merely as the wrong left-to-right order.
            var g = Render(
                """<text x="10" y="60" direction="rtl" unicode-bidi="bidi-override">""" +
                """<tspan font-size="40">AA</tspan><tspan font-size="10">BB</tspan></text>""");

            Assert.Equal(2, g.DrawStringCalls.Count);
            var bb = Assert.Single(g.DrawStringCalls, c => c.Text == "BB");
            var aa = Assert.Single(g.DrawStringCalls, c => c.Text == "AA");

            // "BB" (logically last, small font) is visually first; "AA" (logically first, large font)
            // immediately follows it with no gap and no overlap.
            var bbAdvance = g.MeasureString("BB", bb.Font).Width;
            Assert.Equal(bb.Point.X + bbAdvance, aa.Point.X, 1);
        }
    }
}

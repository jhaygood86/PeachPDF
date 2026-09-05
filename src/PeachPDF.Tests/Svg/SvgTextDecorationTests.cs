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
    /// Coverage for SVG <c>&lt;text&gt;</c>'s <c>text-decoration-line/-style/-color</c> support (issue
    /// #533) - previously entirely unpainted (zero references to underline/overline/line-through logic
    /// anywhere in <c>src/PeachPDF/Svg</c>). Renders through <see cref="SvgRenderer.RenderInto"/> into a
    /// <see cref="TestRecordingGraphics"/> and asserts the resulting <c>DrawLine</c> calls - a
    /// structural/adjacency check (line count, color, position), not a content-stream-substring check,
    /// per this repo's own stated distrust of the latter for paint-affecting changes.
    /// </summary>
    public class SvgTextDecorationTests
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

        private static TestRecordingGraphics.DrawLineCall[] Lines(TestRecordingGraphics g)
            => g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToArray();

        [Fact]
        public void None_PaintsNoLine()
        {
            var g = Render("""<text x="10" y="50" font-size="20">Hi</text>""");

            Assert.Empty(Lines(g));
        }

        [Fact]
        public void Underline_PaintsOneLine_BelowBaselineWithinCell()
        {
            var g = Render("""<text x="10" y="50" font-size="20" text-decoration-line="underline" fill="rgb(0,0,0)">Hi</text>""");

            var line = Assert.Single(Lines(g));
            // Same top-of-cell/baseline convention PaintTextGlyphs uses: an underline sits below the
            // baseline (y=50) but still within the glyph's own em box (above y=50+font descent-ish
            // margin) - a loose sanity bound rather than an exact pixel match.
            Assert.True(line.Y1 > 30 && line.Y1 <= 55);
            Assert.Equal(line.Y1, line.Y2);
            Assert.True(line.X2 > line.X1);
        }

        [Fact]
        public void Overline_PaintsAboveUnderline()
        {
            var g = Render("""<text x="10" y="50" font-size="20" text-decoration-line="overline underline" fill="rgb(0,0,0)">Hi</text>""");

            var lines = Lines(g);
            Assert.Equal(2, lines.Length);
            // overline listed first in text-decoration-line, drawn first - and sits above (smaller Y,
            // top-left-origin convention) the underline.
            Assert.True(lines[0].Y1 < lines[1].Y1);
        }

        [Fact]
        public void LineThrough_SitsBetweenOverlineAndUnderline()
        {
            var g = Render("""<text x="10" y="50" font-size="20" text-decoration-line="overline line-through underline" fill="rgb(0,0,0)">Hi</text>""");

            var lines = Lines(g);
            Assert.Equal(3, lines.Length);
            Assert.True(lines[0].Y1 < lines[1].Y1); // overline above line-through
            Assert.True(lines[1].Y1 < lines[2].Y1); // line-through above underline
        }

        [Fact]
        public void ExplicitColor_UsedInsteadOfFill()
        {
            var g = Render("""<text x="10" y="50" font-size="20" fill="rgb(0,128,0)" text-decoration-line="underline" text-decoration-color="rgb(255,0,0)">Hi</text>""");

            var line = Assert.Single(Lines(g));
            Assert.Equal(RColor.FromArgb(255, 255, 0, 0), line.Color);
        }

        [Fact]
        public void NoExplicitColor_FallsBackToFill()
        {
            var g = Render("""<text x="10" y="50" font-size="20" fill="rgb(0,128,0)" text-decoration-line="underline">Hi</text>""");

            var line = Assert.Single(Lines(g));
            Assert.Equal(RColor.FromArgb(255, 0, 128, 0), line.Color);
        }

        [Fact]
        public void DottedStyle_MapsToDotDashStyle()
        {
            var g = Render("""<text x="10" y="50" font-size="20" text-decoration-line="underline" text-decoration-style="dotted">Hi</text>""");

            var line = Assert.Single(Lines(g));
            Assert.Equal(RDashStyle.Dot, line.DashStyle);
        }

        [Fact]
        public void TspanWithoutOwnDecoration_StillPaintsAncestorsLine_FlowingAcrossIt()
        {
            // text-decoration is NOT inherited (CSS Text Decoration 3) - but the ancestor's own line
            // still "flows across" a descendant tspan that declares no decoration of its own, matching
            // real browser behavior (and FragmentPainter.Decorations.cs's own HTML model).
            var g = Render("""<text x="10" y="50" font-size="20" text-decoration-line="underline" fill="rgb(0,0,0)">A<tspan font-size="10">B</tspan></text>""");

            var line = Assert.Single(Lines(g));
            // One continuous line spans both "A" and "B" - not two separate segments - since both
            // glyphs share the same decorator and the same baseline (Py).
            Assert.True(line.X2 > line.X1);
        }

        [Fact]
        public void TspanWithOwnDecoration_AddsASecondIndependentLine()
        {
            var g = Render("""<text x="10" y="50" font-size="20" fill="rgb(0,0,0)">A<tspan text-decoration-line="underline">B</tspan></text>""");

            // Only "B" is underlined - "A" contributes no decorator, so exactly one line, covering only
            // the tspan's own glyph span.
            var line = Assert.Single(Lines(g));
            Assert.True(line.X1 > 10); // starts after "A", not at the text's own x=10
        }

        [Fact]
        public void VerticalWritingMode_SkipsDecoration_DocumentedV1Gap()
        {
            var g = Render("""<text x="10" y="50" font-size="20" writing-mode="vertical-rl" text-decoration-line="underline">Hi</text>""");

            Assert.Empty(Lines(g));
        }
    }
}

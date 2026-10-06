using PeachDrawing.Core;
using PeachPDF.Adapters;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// The CSS <c>display</c> property on SVG elements: <c>none</c> takes an element and its subtree out of rendering, and
    /// <c>contents</c> (CSS Display 3 Appendix B) strips a container or text content child from the formatting tree while its
    /// content stays, so the element's own box effects (opacity, transform, clip, mask, filter) are gone but what it passes down
    /// by inheritance is not. Every other SVG element computes to <c>none</c> for <c>contents</c>.
    /// </summary>
    public class SvgDisplayTests
    {
        private static readonly PdfSharpAdapter Adapter = new();

        private static readonly PaintColor Red = PaintColor.FromArgb(255, 0, 0);
        private static readonly PaintColor Green = PaintColor.FromArgb(0, 128, 0);
        private static readonly PaintColor Blue = PaintColor.FromArgb(0, 0, 255);

        private static SvgDocument Build(string body) =>
            SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(
                $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\">{body}</svg>").Root!), Adapter);

        private static bool Paints(string body, PaintColor color)
        {
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, Build(body), new Rect(0, 0, 100, 100));
            return g.Log.Any(e => e switch
            {
                TestRecordingGraphics.DrawPathCall { Stroked: false } p => p.PaintColor == color,
                TestRecordingGraphics.DrawRectCall r => r.PaintColor == color,
                TestRecordingGraphics.DrawPolygonCall p => p.PaintColor == color,
                _ => false,
            });
        }

        private static int Transforms(TestRecordingGraphics g) => g.Log.Count(e => e is TestRecordingGraphics.PushTransformCall);

        [Theory]
        [InlineData("""<rect width="10" height="10" fill="red" display="none"/>""")]
        [InlineData("""<rect width="10" height="10" fill="red" style="display: none"/>""")]
        [InlineData("""<g display="none"><rect width="10" height="10" fill="red"/></g>""")]
        [InlineData("""<g style="display:none"><g><circle r="5" cx="5" cy="5" fill="red"/></g></g>""")]
        [InlineData("""<rect width="10" height="10" fill="red" display="contents"/>""")]
        public void DisplayNone_OrContentsOnAShape_RendersNothing(string body)
        {
            Assert.False(Paints(body, Red));
        }

        [Fact]
        public void DisplayNone_LeavesItsSiblingsRendering()
        {
            var body = """
                <rect width="10" height="10" fill="red" display="none"/>
                <rect x="20" width="10" height="10" fill="blue"/>
                """;

            Assert.False(Paints(body, Red));
            Assert.True(Paints(body, Blue));
        }

        [Fact]
        public void DisplayInline_StillRenders()
        {
            Assert.True(Paints("""<rect width="10" height="10" fill="red" display="inline"/>""", Red));
        }

        [Fact]
        public void DisplayNone_OnAnElementUsedByAUse_RendersNoInstance()
        {
            var body = """
                <defs><rect id="r" width="10" height="10" fill="red" display="none"/></defs>
                <use href="#r"/>
                """;

            Assert.False(Paints(body, Red));
        }

        [Fact]
        public void DisplayNone_OnTheUseItself_RendersNoInstance()
        {
            var body = """
                <defs><rect id="r" width="10" height="10" fill="red"/></defs>
                <use href="#r" display="none"/>
                """;

            Assert.False(Paints(body, Red));
        }

        [Fact]
        public void DisplayContents_OnAGroup_KeepsTheChildrenAndDropsTheGroupsOwnEffects()
        {
            var document = Build("""
                <g display="contents" opacity="0.5" transform="translate(40 40)" clip-path="url(#c)" mask="url(#m)" filter="url(#f)" fill="green">
                  <rect width="10" height="10"/>
                </g>
                """);

            var group = Assert.IsAssignableFrom<SvgGroupElement>(Assert.Single(document.Children));
            Assert.Equal(1, group.Opacity);
            Assert.Null(group.Transform);
            Assert.Null(group.ClipPathRef);
            Assert.Null(group.MaskRef);
            Assert.Null(group.FilterRef);

            // fill is inherited, so it still reaches the child through the stripped group.
            var rect = Assert.Single(group.Children);
            Assert.Equal(SvgPaint.Solid(Green), rect.Fill);
            Assert.Null(rect.Transform);
            Assert.True(Paints("""<g display="contents" fill="green"><rect width="10" height="10"/></g>""", Green));
        }

        [Fact]
        public void DisplayContents_OnAGroup_PushesNoTransformForIt()
        {
            var stripped = new TestRecordingGraphics();
            SvgRenderer.RenderInto(stripped, Build(
                """<g display="contents" transform="translate(50 50)"><rect width="10" height="10" fill="blue"/></g>"""), new Rect(0, 0, 100, 100));

            var plain = new TestRecordingGraphics();
            SvgRenderer.RenderInto(plain, Build(
                """<g transform="translate(50 50)"><rect width="10" height="10" fill="blue"/></g>"""), new Rect(0, 0, 100, 100));

            // The viewBox itself pushes one; the group's own translate is the extra one.
            Assert.True(Transforms(plain) > Transforms(stripped), "the stripped group still pushed its own transform");
        }

        [Fact]
        public void DisplayContents_OnAUse_HoistsTheInstanceWithoutItsOffset()
        {
            var body = """
                <defs><rect id="r" width="10" height="10" fill="red"/></defs>
                <use href="#r" x="50" y="50" display="contents"/>
                """;

            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, Build(body), new Rect(0, 0, 100, 100));

            Assert.Contains(g.Log, e => e is TestRecordingGraphics.DrawPathCall { Stroked: false } p && p.PaintColor == Red);

            var offset = new TestRecordingGraphics();
            SvgRenderer.RenderInto(offset, Build("""
                <defs><rect id="r" width="10" height="10" fill="red"/></defs>
                <use href="#r" x="50" y="50"/>
                """), new Rect(0, 0, 100, 100));
            Assert.True(Transforms(offset) > Transforms(g), "the stripped use still carried its x/y offset");
        }

        [Fact]
        public void Switch_WhoseFirstChildIsDisplayNone_RendersNoneOfItsCandidates()
        {
            var body = """
                <switch>
                  <rect width="10" height="10" fill="red" display="none"/>
                  <rect width="10" height="10" fill="blue"/>
                </switch>
                """;

            Assert.False(Paints(body, Red));
            Assert.False(Paints(body, Blue));
        }

        [Fact]
        public void Switch_WithAVisibleFirstChild_StillRendersIt()
        {
            Assert.True(Paints("""<switch><rect width="10" height="10" fill="blue"/><rect fill="red" width="1" height="1"/></switch>""", Blue));
        }

        [Fact]
        public void DisplayNone_OnATspan_DropsItsText()
        {
            var document = Build("""<text x="10" y="50">a<tspan display="none">hidden</tspan>b</text>""");

            var text = Assert.IsType<SvgTextElement>(Assert.Single(document.Children));
            Assert.DoesNotContain(text.Content, c => c is SvgTextSpan);
            Assert.Equal("ab", string.Concat(text.Content.OfType<SvgTextFragment>().Select(f => f.Text)));
        }

        [Fact]
        public void DisplayContents_OnATspan_ClearsItsPositionLists_AndKeepsItsText()
        {
            var document = Build("""<text x="10" y="50">a<tspan x="70" y="20" dx="5" rotate="30" display="contents" fill="green">mid</tspan></text>""");

            var text = Assert.IsType<SvgTextElement>(Assert.Single(document.Children));
            var span = Assert.IsType<SvgTextSpan>(Assert.Single(text.Content, c => c is SvgTextSpan));
            Assert.False(span.Run.HasOwnX);
            Assert.False(span.Run.HasOwnY);
            Assert.Equal(0, span.Run.Dx);
            Assert.True(span.Run.RotateList is null or { Length: 0 });
            Assert.Equal("mid", Assert.IsType<SvgTextFragment>(Assert.Single(span.Run.Content)).Text);
            Assert.Equal(SvgPaint.Solid(Green), span.Run.Fill);
        }

        [Fact]
        public void DisplayContents_OnATextPath_RendersItsTextOnTheStraightBaseline()
        {
            var document = Build("""
                <defs><path id="p" d="M10 80 C 30 10, 70 10, 90 80"/></defs>
                <text><textPath href="#p" display="contents">curved</textPath></text>
                """);

            var text = Assert.IsType<SvgTextElement>(document.Children.Last());
            var span = Assert.IsType<SvgTextSpan>(Assert.Single(text.Content));
            Assert.Null(span.Run.PathData);
        }

        [Fact]
        public void DisplayNone_OnAnElementInsideAPatternOrMarker_IsNotDrawnThere()
        {
            var body = """
                <defs><pattern id="p" width="10" height="10" patternUnits="userSpaceOnUse">
                  <rect width="10" height="10" fill="red" display="none"/>
                </pattern></defs>
                <rect width="50" height="50" fill="url(#p)"/>
                """;

            Assert.False(Paints(body, Red));
        }

        [Fact]
        public async System.Threading.Tasks.Task InlineSvg_TakesDisplayFromTheDocumentCascade()
        {
            // The inline path reads the HTML document's own rules, not just the SVG's attributes.
            var (_, container) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><head><style>.h { display: none } .c { display: contents }</style></head><body style='margin:0'>" +
                "<svg width='100' height='100' viewBox='0 0 100 100'>" +
                "<rect width='10' height='10' fill='red' class='h'/>" +
                "<g class='c' fill='green'><rect x='20' width='10' height='10'/></g>" +
                "</svg></body></html>");

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            bool Filled(PaintColor color) => g.Log.Any(e => e is TestRecordingGraphics.DrawPathCall { Stroked: false } p && p.PaintColor == color);

            Assert.False(Filled(Red), "a rect hidden by a document rule was painted");
            Assert.True(Filled(Green), "the content of a display: contents group was not painted with its inherited fill");
        }
    }
}

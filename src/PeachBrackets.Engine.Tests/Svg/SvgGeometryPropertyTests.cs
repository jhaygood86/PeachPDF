using PeachPDF.Rendering;
using PeachDrawing.PDF;
using PeachBrackets.Svg;
using System.Linq;
using System.Xml.Linq;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// <c>r</c>, <c>cx</c>, <c>cy</c>, <c>rx</c>, <c>ry</c>, <c>x</c>, <c>y</c>, <c>width</c>, <c>height</c> and <c>d</c> are CSS properties in SVG 2: a rule or
    /// <c>style=""</c> sets them (inline beats rule beats attribute) and a keyframe can animate them. Values are Chrome's.
    /// </summary>
    public class SvgGeometryPropertyTests
    {
        private static readonly HtmlRenderContext RenderContext = new();

        private static SvgElement Build(string rule, string element)
        {
            var markup = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"100\"><style>{rule}</style>{element}</svg>";
            var root = XDocument.Parse(markup).Root!;
            var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root), new PeachPDF.Rendering.HtmlRenderContext());
            return SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, cssData, "print"), RenderContext).Children.Single();
        }

        [Fact]
        public void ACircle_TakesRAndCentreFromARule()
        {
            var circle = Assert.IsType<SvgCircleElement>(Build("circle { r: 9px; cx: 12px; cy: 7px }", "<circle r=\"1\" cx=\"2\" cy=\"3\"/>"));

            Assert.Equal(9, circle.R, 3);
            Assert.Equal(12, circle.Cx, 3);
            Assert.Equal(7, circle.Cy, 3);
        }

        [Fact]
        public void ACircle_TakesPercentagesAgainstTheViewport()
        {
            var circle = Assert.IsType<SvgCircleElement>(Build("circle { cx: 50%; cy: 50% }", "<circle r=\"1\"/>"));

            Assert.Equal(100, circle.Cx, 3);
            Assert.Equal(50, circle.Cy, 3);
        }

        [Fact]
        public void ABareNumber_IsTakenAsUserUnits()
        {
            var circle = Assert.IsType<SvgCircleElement>(Build("circle { r: 10; cx: 20 }", "<circle r=\"1\" cx=\"2\" cy=\"3\"/>"));
            Assert.Equal(10, circle.R, 3);
            Assert.Equal(20, circle.Cx, 3);
        }

        [Fact]
        public void AnInlineStyle_BeatsARule_WhichBeatsTheAttribute()
        {
            var inline = Assert.IsType<SvgCircleElement>(Build("circle { r: 9px }", "<circle r=\"1\" style=\"r: 4px\"/>"));
            var rule = Assert.IsType<SvgCircleElement>(Build("circle { r: 9px }", "<circle r=\"1\"/>"));
            var attribute = Assert.IsType<SvgCircleElement>(Build("", "<circle r=\"1\"/>"));

            Assert.Equal(4, inline.R, 3);
            Assert.Equal(9, rule.R, 3);
            Assert.Equal(1, attribute.R, 3);
        }

        [Fact]
        public void AnEllipse_TakesRxAndRyFromARule()
        {
            var ellipse = Assert.IsType<SvgEllipseElement>(Build("ellipse { rx: 20px; ry: 10px }", "<ellipse rx=\"1\" ry=\"1\"/>"));

            Assert.Equal(20, ellipse.Rx, 3);
            Assert.Equal(10, ellipse.Ry, 3);
        }

        [Fact]
        public void ARect_TakesPositionAndSizeFromARule()
        {
            var rect = Assert.IsType<SvgRectElement>(Build("rect { x: 5px; y: 6px; width: 30px; height: 20px; rx: 3px }", "<rect width=\"1\" height=\"1\"/>"));

            Assert.Equal(5, rect.X, 3);
            Assert.Equal(6, rect.Y, 3);
            Assert.Equal(30, rect.Width, 3);
            Assert.Equal(20, rect.Height, 3);
            Assert.Equal(3, rect.Rx, 3);
        }

        [Fact]
        public void APath_TakesDFromAPathFunction()
        {
            var path = Assert.IsType<SvgPathElement>(Build("path { d: path(\"M0 0 L10 10 L0 10 Z\") }", "<path d=\"M0 0 L1 1\"/>"));

            Assert.Equal(4, path.Segments.Count);
        }

        [Fact]
        public void APath_WithDNone_DrawsNothing()
        {
            var path = Assert.IsType<SvgPathElement>(Build("path { d: none }", "<path d=\"M0 0 L1 1\"/>"));

            Assert.Empty(path.Segments);
        }

        [Fact]
        public void APath_WithoutARule_StillReadsTheAttribute()
        {
            var path = Assert.IsType<SvgPathElement>(Build("", "<path d=\"M0 0 L1 1\"/>"));

            Assert.Equal(2, path.Segments.Count);
        }
    }
}

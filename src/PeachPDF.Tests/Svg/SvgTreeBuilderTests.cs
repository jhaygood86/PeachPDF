using PeachPDF.Adapters;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Xml.Linq;

namespace PeachPDF.Tests.Svg
{
    public class SvgTreeBuilderTests
    {
        private static readonly PdfSharpAdapter Adapter = new();

        [Fact]
        public void Build_FromCssBoxTree_ProducesExpectedStructure()
        {
            var root = HtmlParser.ParseDocument(SvgTestFixture.Markup);
            var svgBox = DomUtils.GetBoxByTagName(root, "svg");

            Assert.NotNull(svgBox);

            var document = SvgTreeBuilder.Build(new CssBoxSvgSourceNode(svgBox!), Adapter);

            AssertExpectedStructure(document);
        }

        [Fact]
        public void Build_FromXmlDocument_ProducesEquivalentStructure()
        {
            var xdoc = XDocument.Parse(SvgTestFixture.Markup);
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(xdoc.Root!), Adapter);

            AssertExpectedStructure(document);
        }

        private static void AssertExpectedStructure(SvgDocument document)
        {
            Assert.NotNull(document.ViewBox);
            Assert.Equal(200, document.ViewBox!.Value.Width, 3);
            Assert.Equal(200, document.ViewBox!.Value.Height, 3);
            Assert.Equal(200, document.Width);
            Assert.Equal(200, document.Height);

            Assert.Equal(2, document.Gradients.Count);
            Assert.True(document.Gradients.ContainsKey("bgGradient"));
            Assert.True(document.Gradients.ContainsKey("shineGradient"));

            var bgGradient = Assert.IsType<SvgLinearGradient>(document.Gradients["bgGradient"]);
            Assert.Equal(2, bgGradient.Stops.Count);
            Assert.True(bgGradient.GradientUnitsUserSpaceOnUse);

            var shineGradient = Assert.IsType<SvgRadialGradient>(document.Gradients["shineGradient"]);
            Assert.NotNull(shineGradient.GradientTransform);

            Assert.Single(document.ClipPaths);
            var clipPath = document.ClipPaths["circleClip"];
            Assert.Single(clipPath.Shapes);
            var clippedUse = Assert.IsType<SvgUseElement>(clipPath.Shapes[0]);
            Assert.IsType<SvgCircleElement>(clippedUse.Target);

            // <defs> is skipped; only the two <g> elements and the ribbon <polygon> remain.
            Assert.Equal(3, document.Children.Count);

            var clippedGroup = Assert.IsType<SvgGroupElement>(document.Children[0]);
            Assert.Equal("circleClip", clippedGroup.ClipPathRef);
            Assert.Equal(2, clippedGroup.Children.Count);
            Assert.IsType<SvgPolygonElement>(clippedGroup.Children[0]);
            var opacityGroup = Assert.IsType<SvgGroupElement>(clippedGroup.Children[1]);
            Assert.Equal(0.5, opacityGroup.Opacity, 3);
            Assert.IsType<SvgCircleElement>(Assert.Single(opacityGroup.Children));

            var checkmarkGroup = Assert.IsType<SvgGroupElement>(document.Children[1]);
            Assert.NotNull(checkmarkGroup.Transform);
            var path = Assert.IsType<SvgPathElement>(Assert.Single(checkmarkGroup.Children));
            Assert.True(path.Segments.Count > 0);
            Assert.Contains(path.Segments, s => s.Kind == PathSegmentKind.ArcTo);
            Assert.Contains(path.Segments, s => s.Kind == PathSegmentKind.CubicBezierTo);

            var ribbon = Assert.IsType<SvgPolygonElement>(document.Children[2]);
            Assert.Equal(6, ribbon.Points.Length);
        }
    }
}

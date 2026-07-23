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
    /// <c>clipPathUnits="objectBoundingBox"</c> (issue #168, SVG 1.1 §14.3.5): the clipPath's 0..1 child
    /// geometry maps onto the referencing element's bounding box. Renders through
    /// <see cref="SvgRenderer.RenderInto"/> into a recording graphics that captures the pushed clip
    /// path's points, then asserts the mapped clip bounds. The referencing element is a rect at
    /// (20,20) sized 40×30, so its bounding box is (20,20,40,30).
    /// </summary>
    public class SvgClipPathUnitsTests
    {
        private static readonly PdfSharpAdapter Adapter = new();

        private static RPoint[] RenderAndCaptureClip(string clipUnitsAttr, string clipChildren)
        {
            var markup = $$"""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
                  <defs><clipPath id="c" {{clipUnitsAttr}}>{{clipChildren}}</clipPath></defs>
                  <rect x="20" y="20" width="40" height="30" clip-path="url(#c)"/>
                </svg>
                """;
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new RRect(0, 0, 100, 100));
            var clip = Assert.Single(g.ClipPaths);
            return clip.Points.ToArray();
        }

        private static (double MinX, double MinY, double MaxX, double MaxY) Bounds(RPoint[] points)
        {
            Assert.NotEmpty(points);
            return (points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
        }

        [Fact]
        public void ObjectBoundingBox_UnitRect_CoversTheReferencingElementsBox()
        {
            // The issue's scenario: a 0..1 clip rect under objectBoundingBox maps to exactly the
            // referencing element's box (20,20)-(60,50), i.e. the element is effectively unclipped.
            var points = RenderAndCaptureClip(
                """clipPathUnits="objectBoundingBox" """,
                """<rect x="0" y="0" width="1" height="1"/>""");

            var (minX, minY, maxX, maxY) = Bounds(points);
            Assert.Equal(20, minX, 3);
            Assert.Equal(20, minY, 3);
            Assert.Equal(60, maxX, 3);
            Assert.Equal(50, maxY, 3);
        }

        [Fact]
        public void ObjectBoundingBox_CenterHalf_MapsToTheMiddleOfTheBox()
        {
            // A 0.25..0.75 rect covers the center half of the bbox: x in [20+0.25*40, 20+0.75*40]=[30,50],
            // y in [20+0.25*30, 20+0.75*30]=[27.5,42.5].
            var points = RenderAndCaptureClip(
                """clipPathUnits="objectBoundingBox" """,
                """<rect x="0.25" y="0.25" width="0.5" height="0.5"/>""");

            var (minX, minY, maxX, maxY) = Bounds(points);
            Assert.Equal(30, minX, 3);
            Assert.Equal(27.5, minY, 3);
            Assert.Equal(50, maxX, 3);
            Assert.Equal(42.5, maxY, 3);
        }

        [Fact]
        public void UserSpaceOnUse_IsUnaffected_UnitRectStaysOneByOne()
        {
            // Regression/contrast: the default userSpaceOnUse treats 0 0 1 1 as literal user coords — a
            // 1×1 square at the origin, NOT scaled to the element's box.
            var points = RenderAndCaptureClip(
                """clipPathUnits="userSpaceOnUse" """,
                """<rect x="0" y="0" width="1" height="1"/>""");

            var (minX, minY, maxX, maxY) = Bounds(points);
            Assert.Equal(0, minX, 3);
            Assert.Equal(0, minY, 3);
            Assert.Equal(1, maxX, 3);
            Assert.Equal(1, maxY, 3);
        }

        [Fact]
        public void DefaultUnits_AreUserSpaceOnUse()
        {
            // No clipPathUnits attribute → userSpaceOnUse (the spec default), same as the explicit case.
            var points = RenderAndCaptureClip(
                "",
                """<rect x="0" y="0" width="1" height="1"/>""");

            var (minX, minY, maxX, maxY) = Bounds(points);
            Assert.Equal(0, minX, 3);
            Assert.Equal(0, minY, 3);
            Assert.Equal(1, maxX, 3);
            Assert.Equal(1, maxY, 3);
        }

        [Fact]
        public void ObjectBoundingBox_ComposesWithAShapeTransform()
        {
            // A shape transform inside the clipPath applies in unit space first, then the bbox mapping.
            // translate(0.5,0) shifts the unit rect to x in [0.5,1], then maps to x in [40,60], y in [20,35].
            var points = RenderAndCaptureClip(
                """clipPathUnits="objectBoundingBox" """,
                """<rect x="0" y="0" width="0.5" height="0.5" transform="translate(0.5,0)"/>""");

            var (minX, minY, maxX, maxY) = Bounds(points);
            Assert.Equal(40, minX, 3);
            Assert.Equal(20, minY, 3);
            Assert.Equal(60, maxX, 3);
            Assert.Equal(35, maxY, 3);
        }
    }
}

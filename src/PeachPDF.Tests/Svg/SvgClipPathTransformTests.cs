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
    /// Verifies a clip shape's own <c>transform</c> (directly, via <c>&lt;use&gt;</c>, or via a
    /// wrapping <c>&lt;g&gt;</c>) is baked into the combined clip geometry - issue #169. Renders the
    /// document through <see cref="SvgRenderer.RenderInto"/> into a recording graphics whose
    /// <see cref="TestGraphicsPath"/> captures every clip-path point, then asserts on the resulting
    /// coordinates. Uses arc-free rect/polygon clip shapes so the recorded points are exact. The
    /// viewport is chosen equal to the viewBox (identity ambient transform), and the clip-shape
    /// transform is applied to the path points regardless of the ambient CTM, so the captured points
    /// are the raw clip geometry with the transform baked in.
    /// </summary>
    public class SvgClipPathTransformTests
    {
        private static readonly PdfSharpAdapter Adapter = new();

        /// <summary>Builds the document, renders it, and returns the points of the single clip path
        /// pushed for the one clipped element in the fixture.</summary>
        private static RPoint[] RenderAndCaptureClip(string markup)
        {
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

        private static string ClipFixture(string clipChildren, string defs = "") => $$"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
              <defs>{{defs}}<clipPath id="c">{{clipChildren}}</clipPath></defs>
              <rect x="0" y="0" width="100" height="100" clip-path="url(#c)"/>
            </svg>
            """;

        [Fact]
        public void NoTransform_ClipGeometryUnchanged()
        {
            var points = RenderAndCaptureClip(ClipFixture("""<rect x="0" y="0" width="4" height="4"/>"""));

            var (minX, minY, maxX, maxY) = Bounds(points);
            Assert.Equal(0, minX, 3);
            Assert.Equal(0, minY, 3);
            Assert.Equal(4, maxX, 3);
            Assert.Equal(4, maxY, 3);
        }

        [Fact]
        public void Translate_OnClipShape_ShiftsGeometry()
        {
            var points = RenderAndCaptureClip(ClipFixture("""<rect x="0" y="0" width="4" height="4" transform="translate(20,30)"/>"""));

            var (minX, minY, maxX, maxY) = Bounds(points);
            Assert.Equal(20, minX, 3);
            Assert.Equal(30, minY, 3);
            Assert.Equal(24, maxX, 3);
            Assert.Equal(34, maxY, 3);
        }

        [Fact]
        public void Scale_OnClipShape_ScalesGeometryAboutOrigin()
        {
            var points = RenderAndCaptureClip(ClipFixture("""<rect x="0" y="0" width="4" height="4" transform="scale(2)"/>"""));

            var (minX, minY, maxX, maxY) = Bounds(points);
            Assert.Equal(0, minX, 3);
            Assert.Equal(0, minY, 3);
            Assert.Equal(8, maxX, 3);
            Assert.Equal(8, maxY, 3);
        }

        [Fact]
        public void Rotate90_OnClipShape_RotatesGeometryExactly()
        {
            // rotate(90) maps (x,y)->(-y,x). A rect at [10..14]x[0..4] becomes [-4..0]x[10..14].
            var points = RenderAndCaptureClip(ClipFixture("""<rect x="10" y="0" width="4" height="4" transform="rotate(90)"/>"""));

            var (minX, minY, maxX, maxY) = Bounds(points);
            Assert.Equal(-4, minX, 3);
            Assert.Equal(10, minY, 3);
            Assert.Equal(0, maxX, 3);
            Assert.Equal(14, maxY, 3);
        }

        [Fact]
        public void UseTransform_ReusesShapeAtTransformedPosition()
        {
            var points = RenderAndCaptureClip(ClipFixture(
                """<use href="#base" transform="translate(20,30)"/>""",
                defs: """<rect id="base" x="0" y="0" width="4" height="4"/>"""));

            var (minX, minY, maxX, maxY) = Bounds(points);
            Assert.Equal(20, minX, 3);
            Assert.Equal(30, minY, 3);
            Assert.Equal(24, maxX, 3);
            Assert.Equal(34, maxY, 3);
        }

        [Fact]
        public void UseXY_ReusesShapeAtOffset()
        {
            var points = RenderAndCaptureClip(ClipFixture(
                """<use href="#base" x="20" y="30"/>""",
                defs: """<rect id="base" x="0" y="0" width="4" height="4"/>"""));

            var (minX, minY, maxX, maxY) = Bounds(points);
            Assert.Equal(20, minX, 3);
            Assert.Equal(30, minY, 3);
            Assert.Equal(24, maxX, 3);
            Assert.Equal(34, maxY, 3);
        }

        [Fact]
        public void TwoUses_AtDifferentOffsets_UnionBothRegions()
        {
            var points = RenderAndCaptureClip(ClipFixture(
                """<use href="#base" transform="translate(20,0)"/><use href="#base" transform="translate(0,50)"/>""",
                defs: """<rect id="base" x="0" y="0" width="4" height="4"/>"""));

            // Union spans both instances: x in [0,24], y in [0,54].
            var (minX, minY, maxX, maxY) = Bounds(points);
            Assert.Equal(0, minX, 3);
            Assert.Equal(0, minY, 3);
            Assert.Equal(24, maxX, 3);
            Assert.Equal(54, maxY, 3);

            // Both instances actually contributed (one shifted only in x, one only in y).
            Assert.Contains(points, p => p.X >= 20 && p.Y <= 4);
            Assert.Contains(points, p => p.Y >= 50 && p.X <= 4);
        }

        // Every clip leaf-shape type must have its transform applied, not just <rect>. Each shape is
        // authored with a local minimum corner at (0,0), so translate(20,30) puts its minimum at
        // exactly (20,30). (Circle/ellipse points are recorded as their four cardinal arc endpoints,
        // which are the bounding-box extremes, so the min still lands on (20,30).)
        [Theory]
        [InlineData("""<path d="M0,0 L10,0 L6,12 Z" transform="translate(20,30)"/>""")]
        [InlineData("""<circle cx="5" cy="5" r="5" transform="translate(20,30)"/>""")]
        [InlineData("""<ellipse cx="5" cy="8" rx="5" ry="8" transform="translate(20,30)"/>""")]
        [InlineData("""<polygon points="0,0 10,0 10,12" transform="translate(20,30)"/>""")]
        [InlineData("""<polyline points="0,0 10,0 0,12" transform="translate(20,30)"/>""")]
        [InlineData("""<line x1="0" y1="0" x2="10" y2="8" transform="translate(20,30)"/>""")]
        public void Transform_AppliesToEveryClipShapeType(string clipChild)
        {
            var points = RenderAndCaptureClip(ClipFixture(clipChild));

            var (minX, minY, _, _) = Bounds(points);
            Assert.Equal(20, minX, 3);
            Assert.Equal(30, minY, 3);
        }

        [Fact]
        public void GroupTransform_ShiftsChildGeometry()
        {
            var points = RenderAndCaptureClip(ClipFixture(
                """<g transform="translate(20,30)"><rect x="0" y="0" width="4" height="4"/></g>"""));

            var (minX, minY, maxX, maxY) = Bounds(points);
            Assert.Equal(20, minX, 3);
            Assert.Equal(30, minY, 3);
            Assert.Equal(24, maxX, 3);
            Assert.Equal(34, maxY, 3);
        }

        [Fact]
        public void NestedTransforms_ComposeGroupThenUseThenShape()
        {
            // g translate(10,0) > use translate(0,20) of a rect that itself has transform="translate(1,2)".
            // Expected shift: (10+0+1, 0+20+2) = (11, 22); rect [0..4] -> [11..15] x [22..26].
            var points = RenderAndCaptureClip(ClipFixture(
                """<g transform="translate(10,0)"><use href="#base" transform="translate(0,20)"/></g>""",
                defs: """<rect id="base" x="0" y="0" width="4" height="4" transform="translate(1,2)"/>"""));

            var (minX, minY, maxX, maxY) = Bounds(points);
            Assert.Equal(11, minX, 3);
            Assert.Equal(22, minY, 3);
            Assert.Equal(15, maxX, 3);
            Assert.Equal(26, maxY, 3);
        }
    }
}

using PeachDrawing.Core.Geometry;

namespace PeachDrawing.Core.Tests.Geometry
{
    public class PolygonClipperTests
    {
        private static readonly Rect Window = new(0, 0, 10, 10);

        [Fact]
        public void ClipToRect_APolygonInsideTheWindow_IsUnchanged()
        {
            PaintPoint[] square = [new(2, 2), new(8, 2), new(8, 8), new(2, 8)];

            Assert.Equal(square, PolygonClipper.ClipToRect(square, Window));
        }

        [Fact]
        public void ClipToRect_APolygonOverlappingOneSide_IsCutAtTheBoundary()
        {
            PaintPoint[] square = [new(5, 2), new(15, 2), new(15, 8), new(5, 8)];

            var clipped = PolygonClipper.ClipToRect(square, Window);

            Assert.Equal(4, clipped.Count);
            Assert.All(clipped, p => Assert.InRange(p.X, 5, 10));
            Assert.Contains(new PaintPoint(10, 2), clipped);
            Assert.Contains(new PaintPoint(10, 8), clipped);
        }

        [Fact]
        public void ClipToRect_APolygonOutsideTheWindow_LeavesNothingWithArea()
        {
            PaintPoint[] square = [new(20, 20), new(30, 20), new(30, 30), new(20, 30)];

            Assert.True(PolygonClipper.ClipToRect(square, Window).Count < 3);
        }

        [Fact]
        public void ClipToRect_ATriangleCrossingACorner_ClipsOnEveryEdge()
        {
            PaintPoint[] triangle = [new(-5, 5), new(5, -5), new(15, 15)];

            var clipped = PolygonClipper.ClipToRect(triangle, Window);

            Assert.True(clipped.Count >= 3);
            Assert.All(clipped, p =>
            {
                Assert.InRange(p.X, 0 - 1e-9, 10 + 1e-9);
                Assert.InRange(p.Y, 0 - 1e-9, 10 + 1e-9);
            });
        }

        [Fact]
        public void ClipToRect_AnEmptyPolygon_ReturnsEmpty() => Assert.Empty(PolygonClipper.ClipToRect([], Window));

        [Fact]
        public void ClipToRect_ANullPolygon_Throws() => Assert.Throws<ArgumentNullException>(() => PolygonClipper.ClipToRect(null!, Window));
    }
}

using PeachPDF.Raster;

namespace PeachPDF.Tests.Raster
{
    public class StrokerTests
    {
        private struct ArraySink(byte[] buffer, int width) : ICoverageSink
        {
            public void Span(int y, int x0, ReadOnlySpan<byte> coverage) => coverage.CopyTo(buffer.AsSpan(y * width + x0));
        }

        private static byte[] StrokeToCoverage(FlatPath path, StrokeStyle style, int width, int height)
        {
            var polygons = new PolygonSet();
            Stroker.Stroke(path, style, Affine.Identity, polygons);
            polygons.NormalizeWinding();

            var buffer = new byte[width * height];
            var sink = new ArraySink(buffer, width);
            ScanlineRasterizer.Fill(polygons, false, new IntRect(0, 0, width, height), ref sink);
            return buffer;
        }

        private static FlatPath Line(double x1, double y1, double x2, double y2)
        {
            var path = new FlatPath();
            path.AddContour([(x1, y1), (x2, y2)], closed: false);
            return path;
        }

        private static StrokeStyle Style(double width, StrokeCap cap = StrokeCap.Butt, StrokeJoin join = StrokeJoin.Miter,
            double[]? dashes = null) => new(width, cap, join, 10, dashes, 0);

        [Fact]
        public void ButtCapLine_CoversExactlyItsLengthAndWidth()
        {
            var cov = StrokeToCoverage(Line(2, 2.5, 8, 2.5), Style(1), 10, 5);

            for (var x = 0; x < 10; x++)
            {
                Assert.Equal(x is >= 2 and < 8 ? 255 : 0, cov[2 * 10 + x]);
                Assert.Equal(0, cov[1 * 10 + x]);
                Assert.Equal(0, cov[3 * 10 + x]);
            }
        }

        [Fact]
        public void SquareCap_ExtendsByHalfTheWidthAtBothEnds()
        {
            var cov = StrokeToCoverage(Line(3, 2.5, 7, 2.5), Style(2, StrokeCap.Square), 10, 5);

            // width 2 -> half width 1: covers x in [2, 8) and y in [1.5, 3.5).
            Assert.Equal(255, cov[2 * 10 + 2]);
            Assert.Equal(255, cov[2 * 10 + 7]);
            Assert.Equal(0, cov[2 * 10 + 1]);
            Assert.Equal(0, cov[2 * 10 + 8]);
        }

        [Fact]
        public void RoundCap_ReachesTheCapPointButRoundsTheCorner()
        {
            var cov = StrokeToCoverage(Line(5, 5, 15, 5), Style(8, StrokeCap.Round), 20, 10);

            // The cap centre column just left of the start is covered; the cap's outer corner is not.
            Assert.Equal(255, cov[5 * 20 + 2]);
            Assert.True(cov[1 * 20 + 1] < 40);
        }

        [Fact]
        public void DashedLine_LeavesTheGaps()
        {
            var cov = StrokeToCoverage(Line(0, 2.5, 12, 2.5), Style(1, dashes: [3, 3]), 12, 5);

            for (var x = 0; x < 12; x++)
                Assert.Equal(x / 3 % 2 == 0 ? 255 : 0, cov[2 * 12 + x]);
        }

        [Fact]
        public void ZeroLengthDash_WithRoundCap_DrawsADot()
        {
            var cov = StrokeToCoverage(Line(1, 5, 19, 5), Style(4, StrokeCap.Round, dashes: [0, 8]), 20, 10);

            Assert.True(cov[5 * 20 + 1] > 200);
            Assert.True(cov[5 * 20 + 9] > 200);
            Assert.Equal(0, cov[5 * 20 + 5]);
        }

        [Fact]
        public void MiterJoin_FillsTheOuterCorner_BevelDoesNot()
        {
            var corner = new FlatPath();
            corner.AddContour([(2, 8), (2, 2), (8, 2)], closed: false);

            var mitre = StrokeToCoverage(corner, Style(2, join: StrokeJoin.Miter), 10, 10);
            var bevel = StrokeToCoverage(corner, Style(2, join: StrokeJoin.Bevel), 10, 10);

            // The outer corner pixel (1, 1) belongs to the mitre tip only.
            Assert.Equal(255, mitre[1 * 10 + 1]);
            Assert.True(bevel[1 * 10 + 1] < 130);
        }

        [Fact]
        public void MiterLimit_TurnsAVerySharpCornerIntoABevel()
        {
            var sharp = new FlatPath();
            sharp.AddContour([(1, 5), (17, 4), (1, 6)], closed: false);

            var polygons = new PolygonSet();
            Stroker.Stroke(sharp, new StrokeStyle(1, StrokeCap.Butt, StrokeJoin.Miter, 2, null, 0), Affine.Identity, polygons);

            // With a limit of 2 no piece may reach far beyond the vertex at x = 17.
            var bounds = polygons.GetBounds();
            Assert.NotNull(bounds);
            Assert.True(bounds.Value.MaxX < 18.5);
        }

        [Fact]
        public void ClosedContour_HasNoCapsAndJoinsAtTheSeam()
        {
            var square = new FlatPath();
            square.AddContour([(2, 2), (8, 2), (8, 8), (2, 8)], closed: true);

            var cov = StrokeToCoverage(square, Style(2), 10, 10);

            Assert.Equal(255, cov[1 * 10 + 1]);
            Assert.Equal(255, cov[8 * 10 + 8]);
            Assert.Equal(0, cov[5 * 10 + 5]);
        }

        [Fact]
        public void NonUniformTransform_ProducesAnEllipticalPen()
        {
            var polygons = new PolygonSet();
            Stroker.Stroke(Line(0, 0, 0, 10), Style(2), Affine.Scale(4, 1), polygons);

            var bounds = polygons.GetBounds();
            Assert.NotNull(bounds);
            Assert.Equal(-4, bounds.Value.MinX, 6);
            Assert.Equal(4, bounds.Value.MaxX, 6);
        }

        [Fact]
        public void NonPositiveWidth_DrawsNothing()
        {
            var polygons = new PolygonSet();
            Stroker.Stroke(Line(0, 0, 5, 5), Style(0), Affine.Identity, polygons);

            Assert.Equal(0, polygons.ContourCount);
        }
    }
}

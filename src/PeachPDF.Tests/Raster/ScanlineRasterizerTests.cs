using PeachPDF.Raster;

namespace PeachPDF.Tests.Raster
{
    public class ScanlineRasterizerTests
    {
        private struct ArraySink(byte[] buffer, int width) : ICoverageSink
        {
            public void Span(int y, int x0, ReadOnlySpan<byte> coverage) => coverage.CopyTo(buffer.AsSpan(y * width + x0));
        }

        private static byte[] Fill(PolygonSet polygons, bool evenOdd, int width, int height)
        {
            var buffer = new byte[width * height];
            var sink = new ArraySink(buffer, width);
            ScanlineRasterizer.Fill(polygons, evenOdd, new IntRect(0, 0, width, height), ref sink);
            return buffer;
        }

        private static PolygonSet Rect(double l, double t, double r, double b)
        {
            var set = new PolygonSet();
            set.AddRectangle(l, t, r, b);
            return set;
        }

        [Fact]
        public void PixelAlignedRectangle_IsFullyCoveredInsideAndEmptyOutside()
        {
            var cov = Fill(Rect(1, 1, 4, 3), false, 6, 5);

            for (var y = 0; y < 5; y++)
            {
                for (var x = 0; x < 6; x++)
                {
                    var inside = x is >= 1 and < 4 && y is >= 1 and < 3;
                    Assert.Equal(inside ? 255 : 0, cov[y * 6 + x]);
                }
            }
        }

        [Fact]
        public void HorizontalHalfPixelEdge_GivesHalfCoverage()
        {
            var cov = Fill(Rect(1.5, 0, 3, 1), false, 4, 1);

            Assert.Equal(0, cov[0]);
            Assert.InRange(cov[1], 127, 128);
            Assert.Equal(255, cov[2]);
            Assert.Equal(0, cov[3]);
        }

        [Fact]
        public void VerticalHalfPixelEdge_GivesHalfCoverage()
        {
            var cov = Fill(Rect(0, 0.5, 2, 2), false, 2, 2);

            Assert.InRange(cov[0], 127, 128);
            Assert.Equal(255, cov[2]);
        }

        [Fact]
        public void QuarterPixelRectangle_GivesQuarterCoverage()
        {
            var cov = Fill(Rect(0.5, 0.5, 1, 1), false, 1, 1);

            Assert.InRange(cov[0], 63, 65);
        }

        [Fact]
        public void TriangleCoverage_SumsToItsArea()
        {
            var triangle = new PolygonSet();
            triangle.BeginContour();
            triangle.Add(0.3, 0.2);
            triangle.Add(9.4, 0.7);
            triangle.Add(2.1, 7.9);

            var cov = Fill(triangle, false, 10, 8);
            double total = 0;
            foreach (var c in cov)
                total += c / 255.0;

            // area = |cross| / 2
            var area = Math.Abs((9.4 - 0.3) * (7.9 - 0.2) - (2.1 - 0.3) * (0.7 - 0.2)) / 2;
            Assert.Equal(area, total, 0.5);
        }

        [Fact]
        public void NonzeroFillsOverlappingSameDirectionContours_EvenOddLeavesAHole()
        {
            var set = new PolygonSet();
            set.AddRectangle(0, 0, 6, 6);
            set.AddRectangle(2, 2, 4, 4);

            var nonzero = Fill(set, false, 6, 6);
            var evenOdd = Fill(set, true, 6, 6);

            Assert.Equal(255, nonzero[3 * 6 + 3]);
            Assert.Equal(0, evenOdd[3 * 6 + 3]);
            Assert.Equal(255, evenOdd[0]);
        }

        [Fact]
        public void OppositeWindingInnerContour_IsAHoleUnderNonzeroToo()
        {
            var set = new PolygonSet();
            set.AddRectangle(0, 0, 6, 6);
            set.BeginContour();
            set.Add(2, 2);
            set.Add(2, 4);
            set.Add(4, 4);
            set.Add(4, 2);

            var cov = Fill(set, false, 6, 6);

            Assert.Equal(0, cov[3 * 6 + 3]);
            Assert.Equal(255, cov[0]);
        }

        [Fact]
        public void GeometryBeyondTheClip_IsClippedWithoutError()
        {
            var cov = Fill(Rect(-10, -10, 100, 100), false, 4, 4);

            Assert.All(cov, c => Assert.Equal(255, c));
        }

        [Fact]
        public void PartialClipEdge_IsCoveredCorrectly()
        {
            var buffer = new byte[16];
            var sink = new ArraySink(buffer, 4);
            ScanlineRasterizer.Fill(Rect(0, 0, 4, 4), false, new IntRect(1, 1, 3, 3), ref sink);

            Assert.Equal(255, buffer[1 * 4 + 1]);
            Assert.Equal(0, buffer[0]);
            Assert.Equal(0, buffer[1 * 4 + 3]);
        }

        [Fact]
        public void EmptyOrDegeneratePolygon_DrawsNothing()
        {
            var line = new PolygonSet();
            line.BeginContour();
            line.Add(0, 0);
            line.Add(4, 4);

            Assert.All(Fill(line, false, 4, 4), c => Assert.Equal(0, c));
            Assert.All(Fill(new PolygonSet(), false, 4, 4), c => Assert.Equal(0, c));
        }
    }
}

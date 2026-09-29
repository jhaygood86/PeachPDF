using PeachDrawing.Core.Geometry;

namespace PeachDrawing.Core.Tests.Geometry
{
    public class PathOperationsTests
    {
        private sealed class TestGraphicsPath : GraphicsPath
        {
            public override FillMode FillMode { get; set; }
            public override GraphicsPath ClipToRect(Rect rect) => this;
            public override void Dispose() { }
        }

        private static TestGraphicsPath Rect(double x, double y, double w, double h)
        {
            var p = new TestGraphicsPath();
            p.AddPolygon([new PaintPoint(x, y), new PaintPoint(x + w, y), new PaintPoint(x + w, y + h), new PaintPoint(x, y + h)]);
            return p;
        }

        private static TestGraphicsPath Circle(double x, double y, double r)
        {
            var p = new TestGraphicsPath();
            p.AddCircle(x, y, r);
            return p;
        }

        private static TestGraphicsPath Combine(GraphicsPath a, GraphicsPath b, PathOperation op)
        {
            var result = new TestGraphicsPath();
            PathOperations.Combine(a, b, op, result);
            return result;
        }

        private static double Area(GraphicsPath p) => new PathMeasure(p).Area;

        /// <summary>The area covered by a path under its own fill rule, by sampling a fine grid.</summary>
        private static double CoveredArea(GraphicsPath path, Rect bounds, int steps = 200)
        {
            var contours = path.Flatten(0.01);
            var covered = 0;
            for (var iy = 0; iy < steps; iy++)
            {
                for (var ix = 0; ix < steps; ix++)
                {
                    var x = bounds.Left + (ix + 0.5) * bounds.Width / steps;
                    var y = bounds.Top + (iy + 0.5) * bounds.Height / steps;
                    var winding = 0;
                    foreach (var contour in contours)
                    {
                        var pts = contour.Points;
                        for (var i = 0; i < pts.Count; i++)
                        {
                            var a = pts[i];
                            var b = pts[(i + 1) % pts.Count];
                            if ((a.Y <= y) == (b.Y <= y))
                                continue;

                            var cx = a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                            if (cx > x)
                                winding += b.Y > a.Y ? 1 : -1;
                        }
                    }

                    if (path.FillMode == FillMode.EvenOdd ? (winding & 1) != 0 : winding != 0)
                        covered++;
                }
            }

            return covered * bounds.Width * bounds.Height / (steps * steps);
        }

        [Fact]
        public void Union_OfOverlappingRectangles_IsTheirOutline()
        {
            using var a = Rect(0, 0, 10, 10);
            using var b = Rect(5, 5, 10, 10);

            using var result = Combine(a, b, PathOperation.Union);

            Assert.Equal(100 + 100 - 25, Area(result), 6);
            Assert.Equal(new Rect(0, 0, 15, 15), new PathMeasure(result).Bounds);
            Assert.Single(result.GetCurveContours());
        }

        [Fact]
        public void Intersect_OfOverlappingRectangles_IsTheOverlap()
        {
            using var a = Rect(0, 0, 10, 10);
            using var b = Rect(5, 5, 10, 10);

            using var result = Combine(a, b, PathOperation.Intersect);

            Assert.Equal(25, Area(result), 6);
            Assert.Equal(new Rect(5, 5, 5, 5), new PathMeasure(result).Bounds);
        }

        [Fact]
        public void Difference_RemovesTheOverlapFromTheFirst()
        {
            using var a = Rect(0, 0, 10, 10);
            using var b = Rect(5, 5, 10, 10);

            using var result = Combine(a, b, PathOperation.Difference);

            Assert.Equal(75, Area(result), 6);
        }

        [Fact]
        public void Xor_IsEverythingButTheOverlap()
        {
            using var a = Rect(0, 0, 10, 10);
            using var b = Rect(5, 5, 10, 10);

            using var result = Combine(a, b, PathOperation.Xor);

            Assert.Equal(150, CoveredArea(result, new Rect(-1, -1, 17, 17), 340), 1.5);
        }

        [Fact]
        public void Union_OfDisjointShapes_KeepsBoth()
        {
            using var a = Rect(0, 0, 10, 10);
            using var b = Rect(20, 0, 10, 10);

            using var result = Combine(a, b, PathOperation.Union);

            Assert.Equal(200, Area(result), 6);
            Assert.Equal(2, result.GetCurveContours().Count);
        }

        [Fact]
        public void Intersect_OfDisjointShapes_IsEmpty()
        {
            using var a = Rect(0, 0, 10, 10);
            using var b = Rect(20, 0, 10, 10);

            using var result = Combine(a, b, PathOperation.Intersect);

            Assert.Empty(result.GetCurveContours());
        }

        [Fact]
        public void Union_WhenOneContainsTheOther_IsTheOuter()
        {
            using var outer = Rect(0, 0, 20, 20);
            using var inner = Rect(5, 5, 5, 5);

            using var result = Combine(outer, inner, PathOperation.Union);

            Assert.Equal(400, Area(result), 6);
            Assert.Single(result.GetCurveContours());
        }

        [Fact]
        public void Difference_WhenTheSecondIsInsideTheFirst_MakesAHole()
        {
            using var outer = Rect(0, 0, 20, 20);
            using var inner = Rect(5, 5, 5, 5);

            using var result = Combine(outer, inner, PathOperation.Difference);

            Assert.Equal(375, Area(result), 6);
            Assert.Equal(2, result.GetCurveContours().Count);
            Assert.Equal(FillMode.Nonzero, result.FillMode);
        }

        [Fact]
        public void Union_OfAShapeWithItself_IsTheShape()
        {
            using var a = Rect(0, 0, 10, 10);
            using var b = Rect(0, 0, 10, 10);

            using var result = Combine(a, b, PathOperation.Union);

            Assert.Equal(100, Area(result), 6);
            Assert.Single(result.GetCurveContours());
        }

        [Fact]
        public void Difference_OfAShapeFromItself_IsEmpty()
        {
            using var a = Rect(0, 0, 10, 10);
            using var b = Rect(0, 0, 10, 10);

            using var result = Combine(a, b, PathOperation.Difference);

            Assert.Empty(result.GetCurveContours());
        }

        [Fact]
        public void Union_OfRectanglesSharingAnEdge_MergesThem()
        {
            using var a = Rect(0, 0, 10, 10);
            using var b = Rect(10, 0, 10, 10);

            using var result = Combine(a, b, PathOperation.Union);

            Assert.Equal(200, Area(result), 6);
            Assert.Equal(new Rect(0, 0, 20, 10), new PathMeasure(result).Bounds);
        }

        [Fact]
        public void Union_WithAnEmptyShape_IsTheOtherShape()
        {
            using var a = Rect(0, 0, 10, 10);
            using var empty = new TestGraphicsPath();

            using var result = Combine(a, empty, PathOperation.Union);

            Assert.Equal(100, Area(result), 6);
        }

        [Fact]
        public void Intersect_WithAnEmptyShape_IsEmpty()
        {
            using var a = Rect(0, 0, 10, 10);
            using var empty = new TestGraphicsPath();

            using var result = Combine(a, empty, PathOperation.Intersect);

            Assert.Empty(result.GetCurveContours());
        }

        [Fact]
        public void Union_OfTwoEmptyShapes_IsEmpty()
        {
            using var a = new TestGraphicsPath();
            using var b = new TestGraphicsPath();

            using var result = Combine(a, b, PathOperation.Union);

            Assert.Empty(result.GetCurveContours());
        }

        [Fact]
        public void Intersect_OfTwoOverlappingCircles_IsALensWithCurvedEdges()
        {
            using var a = Circle(0, 0, 10);
            using var b = Circle(10, 0, 10);

            using var result = Combine(a, b, PathOperation.Intersect);

            // Lens area of two unit-radius-r circles whose centres are r apart: r^2 * (2*pi/3 - sqrt(3)/2).
            var expected = 100 * (2 * Math.PI / 3 - Math.Sqrt(3) / 2);
            Assert.Equal(expected, Area(result), 0.5);

            // The lens is bounded by circular arcs: the result must still contain curves, not only lines.
            var contour = Assert.Single(result.GetCurveContours());
            Assert.Contains(contour.Commands, c => c.Kind == PathCommandKind.Cubic);
            Assert.DoesNotContain(contour.Commands, c => c.Kind == PathCommandKind.Line);
        }

        [Fact]
        public void Union_OfTwoOverlappingCircles_HasTheAnalyticArea()
        {
            using var a = Circle(0, 0, 10);
            using var b = Circle(10, 0, 10);

            using var result = Combine(a, b, PathOperation.Union);

            var lens = 100 * (2 * Math.PI / 3 - Math.Sqrt(3) / 2);
            Assert.Equal(2 * Math.PI * 100 - lens, Area(result), 1.0);
        }

        [Fact]
        public void Difference_OfACircleAndARectangle_CutsAChord()
        {
            using var circle = Circle(0, 0, 10);
            using var rectangle = Rect(0, -20, 20, 40);

            using var result = Combine(circle, rectangle, PathOperation.Difference);

            Assert.Equal(Math.PI * 100 / 2, Area(result), 0.5);
            Assert.Equal(0, new PathMeasure(result).Bounds.Right, 3);
        }

        [Fact]
        public void Combine_MatchesASampledReference_ForCurvedShapesOfAllOperations()
        {
            using var a = Circle(0, 0, 10);
            using var b = new TestGraphicsPath();
            b.AddRoundedRectangle(new Rect(-4, -14, 22, 20), 6);
            var bounds = new Rect(-16, -16, 36, 32);

            foreach (var op in Enum.GetValues<PathOperation>())
            {
                using var result = Combine(a, b, op);
                var expected = op switch
                {
                    PathOperation.Union => CoveredArea(a, bounds) + CoveredArea(b, bounds) - IntersectCovered(a, b, bounds),
                    PathOperation.Intersect => IntersectCovered(a, b, bounds),
                    PathOperation.Difference => CoveredArea(a, bounds) - IntersectCovered(a, b, bounds),
                    _ => CoveredArea(a, bounds) + CoveredArea(b, bounds) - 2 * IntersectCovered(a, b, bounds),
                };

                Assert.True(Math.Abs(CoveredArea(result, bounds) - expected) < 3, $"{op}: expected about {expected}, got {CoveredArea(result, bounds)}");
            }
        }

        private static double IntersectCovered(GraphicsPath a, GraphicsPath b, Rect bounds)
        {
            using var lens = Combine(a, b, PathOperation.Intersect);
            return CoveredArea(lens, bounds);
        }

        [Fact]
        public void Union_OfASelfIntersectingBowtie_ResolvesItsCrossing()
        {
            using var bowtie = new TestGraphicsPath();
            bowtie.AddPolygon([new PaintPoint(0, 0), new PaintPoint(10, 10), new PaintPoint(10, 0), new PaintPoint(0, 10)]);
            using var empty = new TestGraphicsPath();

            using var result = Combine(bowtie, empty, PathOperation.Union);

            // Two triangles of area 25 meeting at a point.
            Assert.Equal(50, CoveredArea(result, new Rect(-1, -1, 12, 12), 240), 1.0);
        }

        [Fact]
        public void EvenOddInput_IsReadWithItsOwnRule()
        {
            using var donut = new TestGraphicsPath { FillMode = FillMode.EvenOdd };
            donut.AddPolygon([new PaintPoint(0, 0), new PaintPoint(20, 0), new PaintPoint(20, 20), new PaintPoint(0, 20)]);
            donut.AddPolygon([new PaintPoint(5, 5), new PaintPoint(15, 5), new PaintPoint(15, 15), new PaintPoint(5, 15)]);
            using var probe = Rect(0, 0, 20, 20);

            using var result = Combine(donut, probe, PathOperation.Intersect);

            Assert.Equal(300, CoveredArea(result, new Rect(-1, -1, 22, 22), 330), 3.0);
        }

        private static TestGraphicsPath RandomShape(Random random)
        {
            var path = new TestGraphicsPath();
            switch (random.Next(4))
            {
                case 0:
                    path.AddCircle(random.NextDouble() * 20, random.NextDouble() * 20, 3 + random.NextDouble() * 8);
                    break;
                case 1:
                    path.AddEllipse(random.NextDouble() * 20, random.NextDouble() * 20, 3 + random.NextDouble() * 8, 2 + random.NextDouble() * 6);
                    break;
                case 2:
                    path.AddRoundedRectangle(new Rect(random.NextDouble() * 15, random.NextDouble() * 15, 4 + random.NextDouble() * 10, 4 + random.NextDouble() * 10), 1 + random.NextDouble() * 2);
                    break;
                default:
                    path.AddStar(random.NextDouble() * 20, random.NextDouble() * 20, 4 + random.NextDouble() * 6, 2 + random.NextDouble() * 2, 5, random.NextDouble());
                    break;
            }

            return path;
        }

        [Fact]
        public void Combine_MatchesASampledReference_ForRandomPairsOfShapes()
        {
            var random = new Random(20260928);
            var bounds = new Rect(-12, -12, 44, 44);

            for (var round = 0; round < 40; round++)
            {
                using var a = RandomShape(random);
                using var b = RandomShape(random);
                var both = IntersectCovered(a, b, bounds);
                var onlyA = CoveredArea(a, bounds);
                var onlyB = CoveredArea(b, bounds);

                foreach (var op in Enum.GetValues<PathOperation>())
                {
                    using var result = Combine(a, b, op);
                    var expected = op switch
                    {
                        PathOperation.Union => onlyA + onlyB - both,
                        PathOperation.Intersect => both,
                        PathOperation.Difference => onlyA - both,
                        _ => onlyA + onlyB - 2 * both,
                    };

                    var actual = CoveredArea(result, bounds);
                    Assert.True(Math.Abs(actual - expected) < 4, $"round {round}, {op}: expected about {expected}, got {actual}");
                }
            }
        }

        [Fact]
        public void Combine_OfAStrokeOutlineAndAShape_Works()
        {
            // A thin closed outline (a ring) unioned with a disc inside it must leave the ring and the disc as separate areas.
            using var ring = new TestGraphicsPath();
            ring.AddCircle(0, 0, 10);
            using var hole = new TestGraphicsPath();
            hole.AddCircle(0, 0, 8);
            using var band = Combine(ring, hole, PathOperation.Difference);
            using var disc = Circle(0, 0, 5);

            using var result = Combine(band, disc, PathOperation.Union);

            Assert.Equal(Math.PI * (100 - 64 + 25), Area(result), 2.0);
            Assert.Equal(2, new PathMeasure(band).Area > 0 ? band.GetCurveContours().Count : 0);
        }

        [Fact]
        public void Combine_RejectsNonFiniteCoordinates_InsteadOfSearchingForever()
        {
            using var good = Circle(0, 0, 10);
            using var nan = new TestGraphicsPath();
            nan.Start(0, 0);
            nan.AddBezierTo(double.NaN, 5, 10, 5, 10, 0);
            using var infinite = new TestGraphicsPath();
            infinite.Start(0, 0);
            infinite.LineTo(double.PositiveInfinity, 3);
            infinite.LineTo(4, 4);

            Assert.Throws<ArgumentException>(() => Combine(good, nan, PathOperation.Union));
            Assert.Throws<ArgumentException>(() => Combine(infinite, good, PathOperation.Intersect));
        }

        [Fact]
        public void Union_OfACircleAndItsRotatedCopy_ThatIsTheSameShapeSplitDifferently_StaysSmallAndFast()
        {
            // The same circle, but its four quarter arcs start 45 degrees round: each arc lies along two of the other's for its whole length.
            using var a = Circle(0, 0, 10);
            using var b = Circle(0, 0, 10);
            b.Transform(System.Numerics.Matrix3x2.CreateRotation(MathF.PI / 4));

            var clock = System.Diagnostics.Stopwatch.StartNew();
            using var union = Combine(a, b, PathOperation.Union);
            using var difference = Combine(a, b, PathOperation.Difference);
            clock.Stop();

            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
            Assert.Equal(Math.PI * 100, Area(union), 1.5);
            Assert.True(Area(difference) < 1.5);
            Assert.True(union.GetCurveContours().Sum(c => c.Commands.Count) < 400, "the outline should not be shattered into thousands of pieces");
        }

        [Fact]
        public void Union_OfAShapeThatIsAClosedSingleCurveLoop_ClosesItself()
        {
            // One cubic that starts and ends at the same point is a complete outline on its own.
            using var leaf = new TestGraphicsPath();
            leaf.Start(0, 0);
            leaf.AddBezierTo(40, -30, 40, 30, 0, 0);
            leaf.CloseFigure();
            using var far = Circle(100, 0, 5);

            using var result = Combine(leaf, far, PathOperation.Union);

            Assert.Equal(2, result.GetCurveContours().Count);
            Assert.All(result.GetCurveContours(), c => Assert.True(c.Closed));
        }

        [Fact]
        public void Combine_ValidatesItsArguments()
        {
            using var a = Rect(0, 0, 1, 1);
            using var d = new TestGraphicsPath();

            Assert.Throws<ArgumentNullException>(() => PathOperations.Combine(null!, a, PathOperation.Union, d));
            Assert.Throws<ArgumentNullException>(() => PathOperations.Combine(a, null!, PathOperation.Union, d));
            Assert.Throws<ArgumentNullException>(() => PathOperations.Combine(a, a, PathOperation.Union, null!));
            Assert.Throws<ArgumentOutOfRangeException>(() => PathOperations.Combine(a, a, (PathOperation)99, d));
        }
    }
}

using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Tests.Html.Core.Utils
{
    public class RectilinearRegionTests
    {
        [Fact]
        public void Union_OfASingleRectangle_IsThatRectangleClockwise()
        {
            var contours = RectilinearRegion.Union([new RRect(10, 20, 30, 40)]);

            var contour = Assert.Single(contours);
            Assert.True(contour.IsOuter);
            Assert.True(RectilinearRegion.IsRectangle(contour));
            AssertCorners(contour, [(10, 20), (40, 20), (40, 60), (10, 60)]);
        }

        [Fact]
        public void Union_OfDisjointRectangles_KeepsThemAsSeparateContours()
        {
            var contours = RectilinearRegion.Union(
            [
                new RRect(0, 0, 100, 10),
                new RRect(0, 30, 100, 10),
                new RRect(0, 60, 100, 10)
            ]);

            Assert.Equal(3, contours.Count);
            Assert.All(contours, contour => Assert.True(contour.IsOuter));
            Assert.All(contours, contour => Assert.True(RectilinearRegion.IsRectangle(contour)));
        }

        [Fact]
        public void Union_OfVerticallyOverlappingLineRects_IsOneConnectedContour()
        {
            // The case from a wrapped inline with a border: each line's rect is tall enough that
            // consecutive lines touch, so the whole run bounds a single shape.
            var contours = RectilinearRegion.Union(
            [
                new RRect(20, 0, 80, 20),
                new RRect(0, 20, 100, 20),
                new RRect(0, 40, 50, 20)
            ]);

            var contour = Assert.Single(contours);
            Assert.True(contour.IsOuter);
            AssertCorners(contour,
            [
                (20, 0), (100, 0), (100, 40), (50, 40), (50, 60), (0, 60), (0, 20), (20, 20)
            ]);
        }

        [Fact]
        public void Union_OfRectanglesSharingAnEdge_MergesWithoutLeavingTheSeamAsACorner()
        {
            var contours = RectilinearRegion.Union(
            [
                new RRect(0, 0, 50, 20),
                new RRect(50, 0, 50, 20)
            ]);

            var contour = Assert.Single(contours);
            Assert.True(RectilinearRegion.IsRectangle(contour));
            AssertCorners(contour, [(0, 0), (100, 0), (100, 20), (0, 20)]);
        }

        [Fact]
        public void Union_OfRectanglesEnclosingAGap_ProducesAHoleWoundOppositeTheOuterContour()
        {
            var contours = RectilinearRegion.Union(
            [
                new RRect(0, 0, 30, 90),
                new RRect(60, 0, 30, 90),
                new RRect(0, 0, 90, 30),
                new RRect(0, 60, 90, 30)
            ]);

            Assert.Equal(2, contours.Count);

            var outer = Assert.Single(contours, contour => contour.IsOuter);
            AssertCorners(outer, [(0, 0), (90, 0), (90, 90), (0, 90)]);

            var hole = Assert.Single(contours, contour => !contour.IsOuter);
            AssertCorners(hole, [(30, 30), (30, 60), (60, 60), (60, 30)]);
        }

        [Fact]
        public void Union_OfFullyContainedRectangles_IgnoresTheOnesThatAddNothing()
        {
            var contours = RectilinearRegion.Union(
            [
                new RRect(0, 0, 100, 100),
                new RRect(20, 20, 10, 10)
            ]);

            var contour = Assert.Single(contours);
            Assert.True(RectilinearRegion.IsRectangle(contour));
            AssertCorners(contour, [(0, 0), (100, 0), (100, 100), (0, 100)]);
        }

        [Fact]
        public void Union_OfRectanglesTouchingOnlyAtACorner_KeepsThemAsSeparateContours()
        {
            var contours = RectilinearRegion.Union(
            [
                new RRect(0, 0, 20, 20),
                new RRect(20, 20, 20, 20)
            ]);

            Assert.Equal(2, contours.Count);
            Assert.All(contours, contour => Assert.True(RectilinearRegion.IsRectangle(contour)));
            Assert.All(contours, contour => Assert.True(contour.IsOuter));
        }

        [Fact]
        public void Union_IgnoresEmptyRectangles()
        {
            var contours = RectilinearRegion.Union(
            [
                new RRect(0, 0, 40, 40),
                new RRect(80, 0, 0, 40),
                new RRect(0, 80, 40, 0)
            ]);

            var contour = Assert.Single(contours);
            AssertCorners(contour, [(0, 0), (40, 0), (40, 40), (0, 40)]);
        }

        [Fact]
        public void Union_OfNoRectangles_IsEmpty()
        {
            Assert.Empty(RectilinearRegion.Union([]));
            Assert.Empty(RectilinearRegion.Union([new RRect(5, 5, 0, 0)]));
        }

        [Fact]
        public void Union_OfEdgesThatDifferByLessThanTheEpsilon_TreatsThemAsOneGridLine()
        {
            // Two rectangles whose shared edge positions disagree in the fifteenth decimal - the kind of
            // difference accumulated layout arithmetic produces, not something an author expressed. They
            // must merge into a single seamless contour rather than leaving a hairline sliver behind,
            // and each rectangle must still resolve to the merged grid line it was folded into.
            var contours = RectilinearRegion.Union(
            [
                new RRect(0, 0, 50, 20),
                new RRect(50.0000000000001, 0, 50, 20.0000000000001)
            ]);

            var contour = Assert.Single(contours);
            AssertCorners(contour, [(0, 0), (100, 0), (100, 20), (0, 20)]);
        }

        [Fact]
        public void Shrink_MovesEveryEdgeOfARectangleInward()
        {
            var contour = Assert.Single(RectilinearRegion.Union([new RRect(0, 0, 100, 60)]));

            var shrunk = RectilinearRegion.Shrink(contour, 10);

            AssertCorners(shrunk, [(10, 10), (90, 10), (90, 50), (10, 50)]);
            Assert.True(shrunk.IsOuter);
        }

        [Fact]
        public void Shrink_WidensANotchSoTheRingKeepsAConstantWidthAroundIt()
        {
            var contour = Assert.Single(RectilinearRegion.Union(
            [
                new RRect(0, 0, 100, 20),
                new RRect(0, 20, 60, 20)
            ]));

            var shrunk = RectilinearRegion.Shrink(contour, 5);

            // Every corner is where its two adjacent inset edges now meet. At the reflex corner
            // (60, 20) that is (55, 15) - the notch widens by the inset on both of its sides, which is
            // what keeps the ring the same thickness turning the corner as it is along an edge.
            AssertCorners(shrunk,
            [
                (5, 5), (95, 5), (95, 15), (55, 15), (55, 35), (5, 35)
            ]);
        }

        [Fact]
        public void Shrink_MovesAHolesEdgesTowardTheSurroundingRegion()
        {
            var contours = RectilinearRegion.Union(
            [
                new RRect(0, 0, 30, 90),
                new RRect(60, 0, 30, 90),
                new RRect(0, 0, 90, 30),
                new RRect(0, 60, 90, 30)
            ]);

            var hole = Assert.Single(contours, contour => !contour.IsOuter);

            var shrunk = RectilinearRegion.Shrink(hole, 5);

            // A hole grows as the region around it is eaten into, so its edges move outward.
            AssertCorners(shrunk, [(25, 25), (25, 65), (65, 65), (65, 25)]);
        }

        [Fact]
        public void Shrink_ByMoreThanHalfTheWidthOfAStrip_ReversesTheEdgesBoundingIt()
        {
            var contour = Assert.Single(RectilinearRegion.Union(
            [
                new RRect(0, 0, 40, 100),
                new RRect(60, 0, 40, 100),
                new RRect(0, 0, 100, 10)
            ]));

            var shrunk = RectilinearRegion.Shrink(contour, 20);

            // The 10-tall strip bridging the two arms cannot survive a 20 inset from above and below:
            // its top edge lands at y = 20 but its bottom edge, the top of the notch, lands at y = -10,
            // so the two have swapped over. The contour is kept as-is rather than repaired - filled as
            // a ring's inner contour under the nonzero winding rule, the reversed sub-loop stops
            // cancelling the outer contour and the collapsed strip fills solid, which is how it should
            // look once the ring is thicker than the strip it runs through.
            var top = shrunk.Points.Min(point => point.Y);
            var stripBottom = shrunk.Points.Where(point => point.X > 50 && point.X < 90).Min(point => point.Y);
            Assert.Equal(-10, stripBottom, 3);
            Assert.Equal(-10, top, 3);
        }

        [Fact]
        public void IsRectangle_IsFalseForAContourWithAStep()
        {
            var contour = Assert.Single(RectilinearRegion.Union(
            [
                new RRect(0, 0, 100, 20),
                new RRect(0, 20, 60, 20)
            ]));

            Assert.False(RectilinearRegion.IsRectangle(contour));
        }

        private static void AssertCorners(
            RectilinearRegion.Contour contour, (double X, double Y)[] expected)
        {
            Assert.Equal(expected.Length, contour.Points.Count);

            var start = IndexOf(contour.Points, expected[0]);
            Assert.True(start >= 0, $"({expected[0].X}, {expected[0].Y}) is not a corner of the contour.");

            for (var i = 0; i < expected.Length; i++)
            {
                var point = contour.Points[(start + i) % expected.Length];
                Assert.Equal(expected[i].X, point.X, 3);
                Assert.Equal(expected[i].Y, point.Y, 3);
            }
        }

        private static int IndexOf(IReadOnlyList<RPoint> points, (double X, double Y) target)
        {
            for (var i = 0; i < points.Count; i++)
            {
                if (Math.Abs(points[i].X - target.X) < 0.001 && Math.Abs(points[i].Y - target.Y) < 0.001)
                    return i;
            }

            return -1;
        }
    }
}

namespace PeachDrawing.Abstractions.Tests.Geometry
{
    public class RectTests
    {
        [Fact]
        public void Constructor_SetsLeftTopRightBottomFromXYWidthHeight()
        {
            var rect = new Rect(10, 20, 30, 40);

            Assert.Equal(10, rect.X);
            Assert.Equal(20, rect.Y);
            Assert.Equal(30, rect.Width);
            Assert.Equal(40, rect.Height);
            Assert.Equal(10, rect.Left);
            Assert.Equal(20, rect.Top);
            Assert.Equal(40, rect.Right);
            Assert.Equal(60, rect.Bottom);
        }

        [Fact]
        public void FromLTRB_ComputesWidthAndHeightFromTheFourEdges()
        {
            var rect = Rect.FromLTRB(5, 10, 25, 50);

            Assert.Equal(5, rect.X);
            Assert.Equal(10, rect.Y);
            Assert.Equal(20, rect.Width);
            Assert.Equal(40, rect.Height);
        }

        [Fact]
        public void Intersect_OfOverlappingRects_IsTheOverlapRegion()
        {
            var result = Rect.Intersect(new Rect(0, 0, 10, 10), new Rect(5, 5, 10, 10));

            Assert.Equal(new Rect(5, 5, 5, 5), result);
        }

        [Fact]
        public void Intersect_OfNonOverlappingRects_IsEmpty()
        {
            var result = Rect.Intersect(new Rect(0, 0, 10, 10), new Rect(20, 20, 10, 10));

            Assert.True(result.IsEmpty);
        }

        [Fact]
        public void Union_OfTwoRects_IsTheirSmallestEnclosingRect()
        {
            var result = Rect.Union(new Rect(0, 0, 10, 10), new Rect(20, 20, 10, 10));

            Assert.Equal(new Rect(0, 0, 30, 30), result);
        }

        [Fact]
        public void Contains_APointInsideTheRect_IsTrue()
        {
            var rect = new Rect(0, 0, 10, 10);

            Assert.True(rect.Contains(5, 5));
            Assert.False(rect.Contains(10, 10));
            Assert.False(rect.Contains(-1, 5));
        }

        [Fact]
        public void IsEmpty_IsTrueWhenWidthOrHeightIsZeroOrLess()
        {
            Assert.True(new Rect(0, 0, 0, 10).IsEmpty);
            Assert.True(new Rect(0, 0, 10, 0).IsEmpty);
            Assert.False(new Rect(0, 0, 10, 10).IsEmpty);
        }

        [Fact]
        public void Inflate_Instance_GrowsInPlaceAroundTheCenter()
        {
            var rect = new Rect(10, 10, 20, 20);
            rect.Inflate(5, 2);

            Assert.Equal(new Rect(5, 8, 30, 24), rect);
        }

        [Fact]
        public void Inflate_Static_ReturnsAnInflatedCopyWithoutModifyingTheOriginal()
        {
            var original = new Rect(10, 10, 20, 20);

            var inflated = Rect.Inflate(original, 5, 2);

            Assert.Equal(new Rect(5, 8, 30, 24), inflated);
            Assert.Equal(new Rect(10, 10, 20, 20), original);
        }
    }
}

namespace PeachDrawing.Abstractions.Tests.Geometry
{
    public class SizeTests
    {
        [Fact]
        public void Constructor_SetsWidthAndHeight()
        {
            var size = new Size(10, 20);

            Assert.Equal(10, size.Width);
            Assert.Equal(20, size.Height);
        }

        [Fact]
        public void Constructor_FromAnotherSize_CopiesItsValues()
        {
            var copy = new Size(new Size(3, 4));

            Assert.Equal(3, copy.Width);
            Assert.Equal(4, copy.Height);
        }

        [Fact]
        public void Constructor_FromAPaintPoint_UsesItsCoordinates()
        {
            var size = new Size(new PaintPoint(5, 6));

            Assert.Equal(5, size.Width);
            Assert.Equal(6, size.Height);
        }

        [Fact]
        public void IsEmpty_IsTrueOnlyWhenBothDimensionsAreZero()
        {
            Assert.True(Size.Empty.IsEmpty);
            Assert.False(new Size(1, 0).IsEmpty);
            Assert.False(new Size(0, 1).IsEmpty);
        }

        [Fact]
        public void ExplicitConversion_ToPaintPoint_MapsWidthAndHeightToXAndY()
        {
            var point = (PaintPoint)new Size(7, 8);

            Assert.Equal(7, point.X);
            Assert.Equal(8, point.Y);
        }

        [Fact]
        public void AdditionOperator_AddsWidthsAndHeights()
        {
            var result = new Size(1, 2) + new Size(10, 20);

            Assert.Equal(new Size(11, 22), result);
        }

        [Fact]
        public void SubtractionOperator_SubtractsWidthsAndHeights()
        {
            var result = new Size(10, 20) - new Size(1, 2);

            Assert.Equal(new Size(9, 18), result);
        }

        [Fact]
        public void Add_MatchesTheAdditionOperator()
        {
            Assert.Equal(new Size(1, 2) + new Size(10, 20), Size.Add(new Size(1, 2), new Size(10, 20)));
        }

        [Fact]
        public void Subtract_MatchesTheSubtractionOperator()
        {
            Assert.Equal(new Size(10, 20) - new Size(1, 2), Size.Subtract(new Size(10, 20), new Size(1, 2)));
        }
    }
}

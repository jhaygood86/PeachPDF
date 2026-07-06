using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class PdfRectangleTests
    {
        [Fact]
        public void DefaultConstructor_IsEmpty()
        {
            var rect = new PdfRectangle();

            Assert.True(rect.IsEmpty);
        }

        [Fact]
        public void Constructor_FromTwoPoints_SetsCorners()
        {
            var rect = new PdfRectangle(new XPoint(1, 2), new XPoint(3, 4));

            Assert.Equal(1, rect.X1);
            Assert.Equal(2, rect.Y1);
            Assert.Equal(3, rect.X2);
            Assert.Equal(4, rect.Y2);
        }

        [Fact]
        public void Constructor_FromPointAndSize_ComputesSecondCorner()
        {
            var rect = new PdfRectangle(new XPoint(1, 1), new XSize(3, 4));

            Assert.Equal(1, rect.X1);
            Assert.Equal(1, rect.Y1);
            Assert.Equal(4, rect.X2);
            Assert.Equal(5, rect.Y2);
        }

        [Fact]
        public void Constructor_FromXRect_ComputesSecondCorner()
        {
            var rect = new PdfRectangle(new XRect(1, 1, 3, 4));

            Assert.Equal(1, rect.X1);
            Assert.Equal(1, rect.Y1);
            Assert.Equal(4, rect.X2);
            Assert.Equal(5, rect.Y2);
        }

        [Fact]
        public void Width_And_Height_AreDifferencesOfCorners()
        {
            var rect = new PdfRectangle(1, 2, 4, 6);

            Assert.Equal(3, rect.Width);
            Assert.Equal(4, rect.Height);
        }

        [Fact]
        public void Location_And_Size_Properties()
        {
            var rect = new PdfRectangle(1, 2, 4, 6);

            Assert.Equal(new XPoint(1, 2), rect.Location);
            Assert.Equal(new XSize(3, 4), rect.Size);
        }

        [Fact]
        public void IsEmpty_FalseWhenAnyCoordinateNonZero()
        {
            var rect = new PdfRectangle(0, 0, 1, 0);

            Assert.False(rect.IsEmpty);
        }

        [Fact]
        public void Equality_ComparesCoordinates()
        {
            var a = new PdfRectangle(1, 2, 3, 4);
            var b = new PdfRectangle(1, 2, 3, 4);
            var c = new PdfRectangle(1, 2, 3, 5);

            Assert.True(a == b);
            Assert.False(a == c);
            Assert.True(a != c);
            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.False(a.Equals((object)"not a rectangle"));
        }

        [Fact]
        public void EqualityOperator_HandlesNulls()
        {
            PdfRectangle? left = null;
            PdfRectangle? right = null;

            Assert.True(left == right);

            right = new PdfRectangle(1, 1, 2, 2);
            Assert.False(left == right);
            Assert.False(right == left);
        }

        [Fact]
        public void GetHashCode_SameForEqualRectangles()
        {
            var a = new PdfRectangle(1, 2, 3, 4);
            var b = new PdfRectangle(1, 2, 3, 4);

            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Contains_Point_WithinInclusiveBounds()
        {
            var rect = new PdfRectangle(0, 0, 10, 10);

            Assert.True(rect.Contains(new XPoint(0, 0)));
            Assert.True(rect.Contains(10, 10));
            Assert.False(rect.Contains(new XPoint(11, 5)));
        }

        [Fact]
        public void Contains_XRect_WhenFullyEnclosed()
        {
            var rect = new PdfRectangle(0, 0, 10, 10);

            Assert.True(rect.Contains(new XRect(1, 1, 2, 2)));
            Assert.False(rect.Contains(new XRect(5, 5, 20, 20)));
        }

        [Fact]
        public void Contains_PdfRectangle_WhenFullyEnclosed()
        {
            var outer = new PdfRectangle(0, 0, 10, 10);
            var inner = new PdfRectangle(1, 1, 3, 3);

            Assert.True(outer.Contains(inner));
            Assert.False(inner.Contains(outer));
        }

        [Fact]
        public void ToXRect_ConvertsToXRectWithWidthAndHeight()
        {
            var rect = new PdfRectangle(1, 2, 4, 6);

            var xrect = rect.ToXRect();

            Assert.Equal(1, xrect.X);
            Assert.Equal(2, xrect.Y);
            Assert.Equal(3, xrect.Width);
            Assert.Equal(4, xrect.Height);
        }

        [Fact]
        public void ToString_ProducesBracketedCoordinates()
        {
            var rect = new PdfRectangle(1, 2, 3, 4);

            var text = rect.ToString();

            Assert.StartsWith("[", text);
            Assert.EndsWith("]", text);
            Assert.Contains("1", text);
            Assert.Contains("4", text);
        }
    }
}

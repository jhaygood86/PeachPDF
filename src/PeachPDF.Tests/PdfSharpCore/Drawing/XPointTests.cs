using PeachPDF.PdfSharpCore.Drawing;
using System.Globalization;

namespace PeachPDF.Tests.PdfSharpCoreTests.Drawing
{
    public class XPointTests
    {
        [Fact]
        public void Constructor_SetsXAndY()
        {
            var p = new XPoint(3, 4);

            Assert.Equal(3, p.X);
            Assert.Equal(4, p.Y);
        }

        [Fact]
        public void Equality_ComparesByValue()
        {
            var a = new XPoint(1, 2);
            var b = new XPoint(1, 2);
            var c = new XPoint(1, 3);

            Assert.True(a == b);
            Assert.False(a == c);
            Assert.True(a != c);
            Assert.False(a != b);
            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.False(a.Equals((object)"not a point"));
            Assert.True(XPoint.Equals(a, b));
        }

        [Fact]
        public void GetHashCode_SameForEqualPoints()
        {
            var a = new XPoint(1.5, 2.5);
            var b = new XPoint(1.5, 2.5);

            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Offset_AddsToXAndY()
        {
            var p = new XPoint(1, 1);
            p.Offset(2, 3);

            Assert.Equal(3, p.X);
            Assert.Equal(4, p.Y);
        }

        [Fact]
        public void AddVector_ReturnsTranslatedPoint()
        {
            var p = new XPoint(1, 1);
            var v = new XVector(2, 3);

            var result = p + v;

            Assert.Equal(new XPoint(3, 4), result);
            Assert.Equal(result, XPoint.Add(p, v));
        }

        [Fact]
        public void AddSize_ReturnsTranslatedPoint()
        {
            var p = new XPoint(1, 1);
            var s = new XSize(2, 3);

            var result = p + s;

            Assert.Equal(new XPoint(3, 4), result);
        }

        [Fact]
        public void SubtractVector_ReturnsTranslatedPoint()
        {
            var p = new XPoint(5, 5);
            var v = new XVector(2, 3);

            var result = p - v;

            Assert.Equal(new XPoint(3, 2), result);
            Assert.Equal(result, XPoint.Subtract(p, v));
        }

        [Fact]
        public void SubtractPoint_ReturnsVector()
        {
            var a = new XPoint(5, 5);
            var b = new XPoint(2, 3);

            var result = a - b;

            Assert.Equal(new XVector(3, 2), result);
            Assert.Equal(result, XPoint.Subtract(a, b));
        }

        [Fact]
        public void MultiplyByMatrix_TransformsPoint()
        {
            var p = new XPoint(1, 2);
            var matrix = XMatrix.Identity;

            var result = p * matrix;

            Assert.Equal(p, result);
            Assert.Equal(result, XPoint.Multiply(p, matrix));
        }

        [Fact]
        public void MultiplyByScalar_ScalesCoordinates()
        {
            var p = new XPoint(2, 3);

            Assert.Equal(new XPoint(4, 6), p * 2);
            Assert.Equal(new XPoint(4, 6), 2 * p);
        }

        [Fact]
        public void ExplicitConversion_ToXSize_UsesAbsoluteValues()
        {
            var p = new XPoint(-2, 3);

            var size = (XSize)p;

            Assert.Equal(2, size.Width);
            Assert.Equal(3, size.Height);
        }

        [Fact]
        public void ExplicitConversion_ToXVector_PreservesCoordinates()
        {
            var p = new XPoint(-2, 3);

            var vector = (XVector)p;

            Assert.Equal(-2, vector.X);
            Assert.Equal(3, vector.Y);
        }

        [Fact]
        public void ToString_RoundTripsThroughParse()
        {
            var p = new XPoint(1.5, 2.5);

            var text = p.ToString(CultureInfo.InvariantCulture);
            var parsed = XPoint.Parse(text);

            Assert.Equal(p, parsed);
        }

        [Fact]
        public void ParsePoints_SplitsOnSpaces()
        {
            var points = XPoint.ParsePoints("1,2 3,4 5,6");

            Assert.Equal(3, points.Length);
            Assert.Equal(new XPoint(1, 2), points[0]);
            Assert.Equal(new XPoint(3, 4), points[1]);
            Assert.Equal(new XPoint(5, 6), points[2]);
        }
    }
}

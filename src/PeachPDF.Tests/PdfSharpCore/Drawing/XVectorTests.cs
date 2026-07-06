using PeachPDF.PdfSharpCore.Drawing;
using System.Globalization;

namespace PeachPDF.Tests.PdfSharpCoreTests.Drawing
{
    public class XVectorTests
    {
        [Fact]
        public void Constructor_SetsXAndY()
        {
            var v = new XVector(3, 4);

            Assert.Equal(3, v.X);
            Assert.Equal(4, v.Y);
        }

        [Fact]
        public void Equality_ComparesByValue()
        {
            var a = new XVector(1, 2);
            var b = new XVector(1, 2);
            var c = new XVector(1, 3);

            Assert.True(a == b);
            Assert.False(a == c);
            Assert.True(a != c);
            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.False(a.Equals((object)"not a vector"));
            Assert.True(XVector.Equals(a, b));
        }

        [Fact]
        public void GetHashCode_SameForEqualVectors()
        {
            var a = new XVector(1.5, 2.5);
            var b = new XVector(1.5, 2.5);

            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Length_ComputesEuclideanLength()
        {
            var v = new XVector(3, 4);

            Assert.Equal(5, v.Length);
            Assert.Equal(25, v.LengthSquared);
        }

        [Fact]
        public void Normalize_ProducesUnitLengthVector()
        {
            var v = new XVector(3, 4);
            v.Normalize();

            Assert.Equal(1, v.Length, precision: 10);
        }

        [Fact]
        public void CrossProduct_ComputesDeterminant()
        {
            var a = new XVector(1, 0);
            var b = new XVector(0, 1);

            Assert.Equal(1, XVector.CrossProduct(a, b));
            Assert.Equal(XVector.CrossProduct(a, b), XVector.Determinant(a, b));
        }

        [Fact]
        public void AngleBetween_PerpendicularVectors_Is90Degrees()
        {
            var a = new XVector(1, 0);
            var b = new XVector(0, 1);

            Assert.Equal(90, XVector.AngleBetween(a, b), precision: 8);
        }

        [Fact]
        public void UnaryNegate_NegatesBothComponents()
        {
            var v = new XVector(2, -3);

            var negated = -v;

            Assert.Equal(new XVector(-2, 3), negated);
        }

        [Fact]
        public void Negate_MutatesInPlace()
        {
            var v = new XVector(2, -3);
            v.Negate();

            Assert.Equal(new XVector(-2, 3), v);
        }

        [Fact]
        public void Add_SumsComponents()
        {
            var a = new XVector(1, 2);
            var b = new XVector(3, 4);

            Assert.Equal(new XVector(4, 6), a + b);
            Assert.Equal(new XVector(4, 6), XVector.Add(a, b));
        }

        [Fact]
        public void Subtract_DiffsComponents()
        {
            var a = new XVector(5, 6);
            var b = new XVector(3, 4);

            Assert.Equal(new XVector(2, 2), a - b);
            Assert.Equal(new XVector(2, 2), XVector.Subtract(a, b));
        }

        [Fact]
        public void AddToPoint_ReturnsPoint()
        {
            var v = new XVector(1, 2);
            var p = new XPoint(3, 4);

            Assert.Equal(new XPoint(4, 6), v + p);
            Assert.Equal(new XPoint(4, 6), XVector.Add(v, p));
        }

        [Fact]
        public void MultiplyByScalar_ScalesComponents()
        {
            var v = new XVector(2, 3);

            Assert.Equal(new XVector(4, 6), v * 2);
            Assert.Equal(new XVector(4, 6), 2 * v);
            Assert.Equal(new XVector(4, 6), XVector.Multiply(v, 2));
            Assert.Equal(new XVector(4, 6), XVector.Multiply(2, v));
        }

        [Fact]
        public void DivideByScalar_ScalesComponentsDown()
        {
            var v = new XVector(4, 6);

            Assert.Equal(new XVector(2, 3), v / 2);
            Assert.Equal(new XVector(2, 3), XVector.Divide(v, 2));
        }

        [Fact]
        public void MultiplyByMatrix_TransformsVector()
        {
            var v = new XVector(2, 3);
            var matrix = XMatrix.Identity;

            Assert.Equal(v, v * matrix);
            Assert.Equal(v, XVector.Multiply(v, matrix));
        }

        [Fact]
        public void MultiplyVectorByVector_ReturnsDotProduct()
        {
            var a = new XVector(1, 2);
            var b = new XVector(3, 4);

            Assert.Equal(11, a * b);
            Assert.Equal(11, XVector.Multiply(a, b));
        }

        [Fact]
        public void ExplicitConversion_ToXSize_UsesAbsoluteValues()
        {
            var v = new XVector(-2, 3);

            var size = (XSize)v;

            Assert.Equal(2, size.Width);
            Assert.Equal(3, size.Height);
        }

        [Fact]
        public void ExplicitConversion_ToXPoint_PreservesCoordinates()
        {
            var v = new XVector(-2, 3);

            var point = (XPoint)v;

            Assert.Equal(-2, point.X);
            Assert.Equal(3, point.Y);
        }

        [Fact]
        public void ToString_RoundTripsThroughParse()
        {
            var v = new XVector(1.5, 2.5);

            var text = v.ToString(CultureInfo.InvariantCulture);
            var parsed = XVector.Parse(text);

            Assert.Equal(v, parsed);
        }
    }
}

using PeachPDF.PdfSharpCore.Drawing;
using System.Globalization;

namespace PeachPDF.Tests.PdfSharpCoreTests.Drawing
{
    public class XSizeTests
    {
        [Fact]
        public void Constructor_SetsWidthAndHeight()
        {
            var s = new XSize(3, 4);

            Assert.Equal(3, s.Width);
            Assert.Equal(4, s.Height);
        }

        [Fact]
        public void Constructor_NegativeWidthOrHeight_Throws()
        {
            Assert.Throws<ArgumentException>(() => new XSize(-1, 1));
            Assert.Throws<ArgumentException>(() => new XSize(1, -1));
        }

        [Fact]
        public void Empty_IsEmptyAndHasNegativeInfiniteDimensions()
        {
            var empty = XSize.Empty;

            Assert.True(empty.IsEmpty);
            Assert.Equal(double.NegativeInfinity, empty.Width);
            Assert.Equal(double.NegativeInfinity, empty.Height);
        }

        [Fact]
        public void Width_SetOnEmpty_Throws()
        {
            var empty = XSize.Empty;

            Assert.Throws<InvalidOperationException>(() => empty.Width = 1);
            Assert.Throws<InvalidOperationException>(() => empty.Height = 1);
        }

        [Fact]
        public void Width_SetNegative_Throws()
        {
            var s = new XSize(1, 1);

            Assert.Throws<ArgumentException>(() => s.Width = -1);
            Assert.Throws<ArgumentException>(() => s.Height = -1);
        }

        [Fact]
        public void Equality_ComparesByValue()
        {
            var a = new XSize(1, 2);
            var b = new XSize(1, 2);
            var c = new XSize(1, 3);

            Assert.True(a == b);
            Assert.False(a == c);
            Assert.True(a != c);
            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.False(a.Equals((object)"not a size"));
            Assert.True(XSize.Equals(a, b));
            Assert.True(XSize.Equals(XSize.Empty, XSize.Empty));
        }

        [Fact]
        public void GetHashCode_EmptyIsZero()
        {
            Assert.Equal(0, XSize.Empty.GetHashCode());
        }

        [Fact]
        public void GetHashCode_SameForEqualSizes()
        {
            var a = new XSize(1.5, 2.5);
            var b = new XSize(1.5, 2.5);

            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void ToXPoint_UsesWidthAndHeightAsCoordinates()
        {
            var s = new XSize(3, 4);

            Assert.Equal(new XPoint(3, 4), s.ToXPoint());
        }

        [Fact]
        public void ToXVector_UsesWidthAndHeightAsComponents()
        {
            var s = new XSize(3, 4);

            Assert.Equal(new XVector(3, 4), s.ToXVector());
        }

        [Fact]
        public void ExplicitConversions_ToVectorAndPoint()
        {
            var s = new XSize(3, 4);

            Assert.Equal(new XVector(3, 4), (XVector)s);
            Assert.Equal(new XPoint(3, 4), (XPoint)s);
        }

        [Fact]
        public void ToString_Empty_ReturnsEmptyLiteral()
        {
            Assert.Equal("Empty", XSize.Empty.ToString());
        }

        [Fact]
        public void ToString_RoundTripsThroughParse()
        {
            var s = new XSize(1.5, 2.5);

            var text = s.ToString(CultureInfo.InvariantCulture);
            var parsed = XSize.Parse(text);

            Assert.Equal(s, parsed);
        }

        [Fact]
        public void Parse_EmptyLiteral_ReturnsEmpty()
        {
            var parsed = XSize.Parse("Empty");

            Assert.True(parsed.IsEmpty);
        }
    }
}

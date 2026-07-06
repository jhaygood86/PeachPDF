using PeachPDF.PdfSharpCore.Drawing;
using System.Globalization;

namespace PeachPDF.Tests.PdfSharpCoreTests.Drawing
{
    public class XRectTests
    {
        [Fact]
        public void Constructor_SetsFields()
        {
            var r = new XRect(1, 2, 3, 4);

            Assert.Equal(1, r.X);
            Assert.Equal(2, r.Y);
            Assert.Equal(3, r.Width);
            Assert.Equal(4, r.Height);
        }

        [Fact]
        public void Constructor_NegativeWidthOrHeight_Throws()
        {
            Assert.Throws<ArgumentException>(() => new XRect(0, 0, -1, 1));
            Assert.Throws<ArgumentException>(() => new XRect(0, 0, 1, -1));
        }

        [Fact]
        public void Constructor_FromTwoPoints_NormalizesOrder()
        {
            var r = new XRect(new XPoint(5, 5), new XPoint(1, 2));

            Assert.Equal(1, r.X);
            Assert.Equal(2, r.Y);
            Assert.Equal(4, r.Width);
            Assert.Equal(3, r.Height);
        }

        [Fact]
        public void Constructor_FromPointAndVector()
        {
            var r = new XRect(new XPoint(1, 1), new XVector(3, 4));

            Assert.Equal(1, r.X);
            Assert.Equal(1, r.Y);
            Assert.Equal(3, r.Width);
            Assert.Equal(4, r.Height);
        }

        [Fact]
        public void Constructor_FromPointAndSize()
        {
            var r = new XRect(new XPoint(1, 2), new XSize(3, 4));

            Assert.Equal(1, r.X);
            Assert.Equal(2, r.Y);
            Assert.Equal(3, r.Width);
            Assert.Equal(4, r.Height);
        }

        [Fact]
        public void Constructor_FromEmptySize_ProducesEmptyRect()
        {
            var r = new XRect(new XPoint(1, 2), XSize.Empty);

            Assert.True(r.IsEmpty);
        }

        [Fact]
        public void Constructor_FromSizeOnly()
        {
            var r = new XRect(new XSize(3, 4));

            Assert.Equal(0, r.X);
            Assert.Equal(0, r.Y);
            Assert.Equal(3, r.Width);
            Assert.Equal(4, r.Height);
        }

        [Fact]
        public void FromLTRB_ComputesWidthAndHeight()
        {
            var r = XRect.FromLTRB(1, 2, 5, 8);

            Assert.Equal(1, r.Left);
            Assert.Equal(2, r.Top);
            Assert.Equal(5, r.Right);
            Assert.Equal(8, r.Bottom);
            Assert.Equal(4, r.Width);
            Assert.Equal(6, r.Height);
        }

        [Fact]
        public void Empty_IsEmpty()
        {
            Assert.True(XRect.Empty.IsEmpty);
            Assert.Equal(double.NegativeInfinity, XRect.Empty.Right);
            Assert.Equal(double.NegativeInfinity, XRect.Empty.Bottom);
        }

        [Fact]
        public void Equality_ComparesByValue()
        {
            var a = new XRect(1, 2, 3, 4);
            var b = new XRect(1, 2, 3, 4);
            var c = new XRect(1, 2, 3, 5);

            Assert.True(a == b);
            Assert.False(a == c);
            Assert.True(a != c);
            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.False(a.Equals((object)"not a rect"));
            Assert.True(XRect.Equals(a, b));
            Assert.True(XRect.Equals(XRect.Empty, XRect.Empty));
        }

        [Fact]
        public void GetHashCode_EmptyIsZero()
        {
            Assert.Equal(0, XRect.Empty.GetHashCode());
        }

        [Fact]
        public void Corners_AreComputedFromLocationAndSize()
        {
            var r = new XRect(1, 2, 3, 4);

            Assert.Equal(new XPoint(1, 2), r.TopLeft);
            Assert.Equal(new XPoint(4, 2), r.TopRight);
            Assert.Equal(new XPoint(1, 6), r.BottomLeft);
            Assert.Equal(new XPoint(4, 6), r.BottomRight);
            Assert.Equal(new XPoint(2.5, 4), r.Center);
        }

        [Fact]
        public void Location_And_Size_Properties()
        {
            var r = new XRect(1, 2, 3, 4);

            Assert.Equal(new XPoint(1, 2), r.Location);
            Assert.Equal(new XSize(3, 4), r.Size);

            r.Location = new XPoint(5, 6);
            Assert.Equal(5, r.X);
            Assert.Equal(6, r.Y);

            r.Size = new XSize(7, 8);
            Assert.Equal(7, r.Width);
            Assert.Equal(8, r.Height);
        }

        [Fact]
        public void Size_SetEmpty_MakesRectEmpty()
        {
            var r = new XRect(1, 2, 3, 4);
            r.Size = XSize.Empty;

            Assert.True(r.IsEmpty);
        }

        [Fact]
        public void Contains_Point_WithinBounds()
        {
            var r = new XRect(0, 0, 10, 10);

            Assert.True(r.Contains(new XPoint(5, 5)));
            Assert.True(r.Contains(5, 5));
            Assert.False(r.Contains(new XPoint(20, 20)));
            Assert.False(XRect.Empty.Contains(new XPoint(1, 1)));
        }

        [Fact]
        public void Contains_Rect_WhenFullyEnclosed()
        {
            var outer = new XRect(0, 0, 10, 10);
            var inner = new XRect(1, 1, 2, 2);
            var overlapping = new XRect(5, 5, 20, 20);

            Assert.True(outer.Contains(inner));
            Assert.False(outer.Contains(overlapping));
        }

        [Fact]
        public void IntersectsWith_OverlappingRects()
        {
            var a = new XRect(0, 0, 10, 10);
            var b = new XRect(5, 5, 10, 10);
            var c = new XRect(20, 20, 5, 5);

            Assert.True(a.IntersectsWith(b));
            Assert.False(a.IntersectsWith(c));
        }

        [Fact]
        public void Intersect_ComputesOverlappingRegion()
        {
            var a = new XRect(0, 0, 10, 10);
            var b = new XRect(5, 5, 10, 10);

            var result = XRect.Intersect(a, b);

            Assert.Equal(new XRect(5, 5, 5, 5), result);
        }

        [Fact]
        public void Intersect_NonOverlapping_ResultsInEmpty()
        {
            var a = new XRect(0, 0, 1, 1);
            var b = new XRect(10, 10, 1, 1);

            a.Intersect(b);

            Assert.True(a.IsEmpty);
        }

        [Fact]
        public void Union_WithRect_ExpandsToFit()
        {
            var a = new XRect(0, 0, 1, 1);
            var b = new XRect(5, 5, 1, 1);

            var result = XRect.Union(a, b);

            Assert.Equal(new XRect(0, 0, 6, 6), result);
        }

        [Fact]
        public void Union_EmptyWithRect_ReturnsOtherRect()
        {
            var result = XRect.Union(XRect.Empty, new XRect(1, 1, 2, 2));

            Assert.Equal(new XRect(1, 1, 2, 2), result);
        }

        [Fact]
        public void Union_WithPoint_ExpandsToIncludePoint()
        {
            var r = new XRect(0, 0, 1, 1);

            var result = XRect.Union(r, new XPoint(5, 5));

            Assert.Equal(new XRect(0, 0, 5, 5), result);
        }

        [Fact]
        public void Offset_MovesRectByVector()
        {
            var r = new XRect(1, 1, 2, 2);
            r.Offset(new XVector(3, 4));

            Assert.Equal(4, r.X);
            Assert.Equal(5, r.Y);
        }

        [Fact]
        public void Offset_MovesRectByAmounts()
        {
            var r = XRect.Offset(new XRect(1, 1, 2, 2), 3, 4);

            Assert.Equal(4, r.X);
            Assert.Equal(5, r.Y);
        }

        [Fact]
        public void Offset_OnEmptyRect_Throws()
        {
            var r = XRect.Empty;
            Assert.Throws<InvalidOperationException>(() => r.Offset(1, 1));
        }

        [Fact]
        public void AddAndSubtractPoint_TranslatesRect()
        {
            var r = new XRect(1, 1, 2, 2);
            var p = new XPoint(3, 4);

            var added = r + p;
            Assert.Equal(4, added.X);
            Assert.Equal(5, added.Y);

            var subtracted = r - p;
            Assert.Equal(-2, subtracted.X);
            Assert.Equal(-3, subtracted.Y);
        }

        [Fact]
        public void Inflate_ExpandsBySize()
        {
            var r = new XRect(2, 2, 4, 4);
            r.Inflate(new XSize(1, 1));

            Assert.Equal(1, r.X);
            Assert.Equal(1, r.Y);
            Assert.Equal(6, r.Width);
            Assert.Equal(6, r.Height);
        }

        [Fact]
        public void Inflate_ByWidthAndHeight_StaticOverload()
        {
            var result = XRect.Inflate(new XRect(2, 2, 4, 4), 1, 1);

            Assert.Equal(new XRect(1, 1, 6, 6), result);
        }

        [Fact]
        public void Scale_MultipliesDimensionsAndPosition()
        {
            var r = new XRect(1, 1, 2, 2);
            r.Scale(2, 3);

            Assert.Equal(2, r.X);
            Assert.Equal(3, r.Y);
            Assert.Equal(4, r.Width);
            Assert.Equal(6, r.Height);
        }

        [Fact]
        public void Scale_NegativeFactors_FlipsAndNormalizes()
        {
            var r = new XRect(1, 1, 2, 2);
            r.Scale(-1, -1);

            // x *= -1 => -1, width *= -1 => -2, then negative-width normalization: x += width, width *= -1.
            Assert.Equal(-3, r.X);
            Assert.Equal(-3, r.Y);
            Assert.Equal(2, r.Width);
            Assert.Equal(2, r.Height);
        }

        [Fact]
        public void Transform_WithIdentityMatrix_LeavesRectUnchanged()
        {
            var r = new XRect(1, 2, 3, 4);
            var result = XRect.Transform(r, XMatrix.Identity);

            Assert.Equal(r, result);
        }

        [Fact]
        public void ToString_Empty_ReturnsEmptyLiteral()
        {
            Assert.Equal("Empty", XRect.Empty.ToString());
        }

        [Fact]
        public void ToString_RoundTripsThroughParse()
        {
            var r = new XRect(1.5, 2.5, 3.5, 4.5);

            var text = r.ToString(CultureInfo.InvariantCulture);
            var parsed = XRect.Parse(text);

            Assert.Equal(r, parsed);
        }

        [Fact]
        public void Parse_EmptyLiteral_ReturnsEmpty()
        {
            var parsed = XRect.Parse("Empty");

            Assert.True(parsed.IsEmpty);
        }
    }
}

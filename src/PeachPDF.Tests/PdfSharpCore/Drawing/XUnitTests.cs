using PeachPDF.PdfSharpCore.Drawing;
using System.Globalization;

namespace PeachPDF.Tests.PdfSharpCoreTests.Drawing
{
    public class XUnitTests
    {
        const double Tolerance = 1e-6;

        [Fact]
        public void Constructor_Point_SetsValueAndType()
        {
            var u = new XUnit(72);

            Assert.Equal(72, u.Value);
            Assert.Equal(XGraphicsUnit.Point, u.Type);
        }

        [Fact]
        public void Constructor_ValueAndType()
        {
            var u = new XUnit(2, XGraphicsUnit.Centimeter);

            Assert.Equal(2, u.Value);
            Assert.Equal(XGraphicsUnit.Centimeter, u.Type);
        }

        [Fact]
        public void Constructor_InvalidType_Throws()
        {
            Assert.Throws<ArgumentException>(() => new XUnit(1, (XGraphicsUnit)999));
        }

        [Fact]
        public void FromInch_ConvertsToPoint()
        {
            var u = XUnit.FromInch(1);

            Assert.Equal(72, u.Point, Tolerance);
        }

        [Fact]
        public void FromMillimeter_ConvertsToPoint()
        {
            var u = XUnit.FromMillimeter(25.4);

            Assert.Equal(72, u.Point, Tolerance);
        }

        [Fact]
        public void FromCentimeter_ConvertsToPoint()
        {
            var u = XUnit.FromCentimeter(2.54);

            Assert.Equal(72, u.Point, Tolerance);
        }

        [Fact]
        public void FromPresentation_ConvertsToPoint()
        {
            var u = XUnit.FromPresentation(96);

            Assert.Equal(72, u.Point, Tolerance);
        }

        [Fact]
        public void FromPoint_ConvertsToOtherUnits()
        {
            var u = XUnit.FromPoint(72);

            Assert.Equal(1, u.Inch, Tolerance);
            Assert.Equal(25.4, u.Millimeter, Tolerance);
            Assert.Equal(2.54, u.Centimeter, Tolerance);
            Assert.Equal(96, u.Presentation, Tolerance);
        }

        [Fact]
        public void Point_Setter_ChangesTypeToPoint()
        {
            var u = XUnit.FromInch(1);
            u.Point = 36;

            Assert.Equal(XGraphicsUnit.Point, u.Type);
            Assert.Equal(36, u.Value);
        }

        [Fact]
        public void Inch_Setter_ChangesTypeToInch()
        {
            var u = XUnit.FromPoint(72);
            u.Inch = 2;

            Assert.Equal(XGraphicsUnit.Inch, u.Type);
            Assert.Equal(2, u.Value);
        }

        [Fact]
        public void Millimeter_Setter_ChangesTypeToMillimeter()
        {
            var u = XUnit.FromPoint(72);
            u.Millimeter = 10;

            Assert.Equal(XGraphicsUnit.Millimeter, u.Type);
            Assert.Equal(10, u.Value);
        }

        [Fact]
        public void Centimeter_Setter_ChangesTypeToCentimeter()
        {
            var u = XUnit.FromPoint(72);
            u.Centimeter = 3;

            Assert.Equal(XGraphicsUnit.Centimeter, u.Type);
            Assert.Equal(3, u.Value);
        }

        [Fact]
        public void Presentation_Setter_ChangesTypeToPoint()
        {
            var u = XUnit.FromPoint(72);
            u.Presentation = 96;

            Assert.Equal(XGraphicsUnit.Point, u.Type);
            Assert.Equal(96, u.Value);
        }

        [Fact]
        public void ImplicitConversion_FromInt_UsesPoint()
        {
            XUnit u = 42;

            Assert.Equal(42, u.Point);
            Assert.Equal(XGraphicsUnit.Point, u.Type);
        }

        [Fact]
        public void ImplicitConversion_FromDouble_UsesPoint()
        {
            XUnit u = 42.5;

            Assert.Equal(42.5, u.Point);
        }

        [Fact]
        public void ImplicitConversion_ToDouble_ReturnsPoint()
        {
            XUnit u = XUnit.FromInch(1);

            double points = u;

            Assert.Equal(72, points, Tolerance);
        }

        [Theory]
        [InlineData("2cm", (int)XGraphicsUnit.Centimeter)]
        [InlineData("2in", (int)XGraphicsUnit.Inch)]
        [InlineData("2mm", (int)XGraphicsUnit.Millimeter)]
        [InlineData("2pt", (int)XGraphicsUnit.Point)]
        [InlineData("2", (int)XGraphicsUnit.Point)]
        [InlineData("2pu", (int)XGraphicsUnit.Presentation)]
        public void ImplicitConversion_FromString_ParsesUnitSuffix(string text, int expectedType)
        {
            XUnit u = text;

            Assert.Equal((XGraphicsUnit)expectedType, u.Type);
            Assert.Equal(2, u.Value);
        }

        [Fact]
        public void ImplicitConversion_FromString_HandlesGermanDecimalComma()
        {
            XUnit u = "2,5cm";

            Assert.Equal(2.5, u.Value);
        }

        [Fact]
        public void ImplicitConversion_FromString_UnknownSuffix_Throws()
        {
            Assert.Throws<ArgumentException>(() => { XUnit u = "2xyz"; });
        }

        [Fact]
        public void Equality_ComparesValueAndType()
        {
            var a = XUnit.FromPoint(10);
            var b = XUnit.FromPoint(10);
            var c = XUnit.FromPoint(20);
            var d = XUnit.FromInch(10);

            Assert.True(a == b);
            Assert.False(a == c);
            Assert.True(a != c);
            Assert.False(a == d);
            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.False(a.Equals((object)"not a unit"));
        }

        [Fact]
        public void GetHashCode_SameForEqualUnits()
        {
            var a = XUnit.FromPoint(10);
            var b = XUnit.FromPoint(10);

            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void ToString_AppendsUnitSuffix()
        {
            var u = XUnit.FromCentimeter(2);

            Assert.Equal("2cm", u.ToString());
        }

        [Fact]
        public void ToString_WithFormatProvider()
        {
            var u = XUnit.FromInch(3);

            Assert.Equal("3in", u.ToString(CultureInfo.InvariantCulture));
        }

        [Fact]
        public void Parse_RoundTripsThroughToString()
        {
            var original = XUnit.FromMillimeter(15);

            var parsed = XUnit.Parse(original.ToString());

            Assert.Equal(original, parsed);
        }

        [Fact]
        public void ConvertType_ChangesUnitPreservingPointValue()
        {
            var u = XUnit.FromInch(1);
            u.ConvertType(XGraphicsUnit.Point);

            Assert.Equal(XGraphicsUnit.Point, u.Type);
            Assert.Equal(72, u.Value, Tolerance);
        }

        [Fact]
        public void ConvertType_SameType_IsNoOp()
        {
            var u = XUnit.FromCentimeter(2);
            u.ConvertType(XGraphicsUnit.Centimeter);

            Assert.Equal(2, u.Value);
        }

        [Theory]
        [InlineData((int)XGraphicsUnit.Millimeter)]
        [InlineData((int)XGraphicsUnit.Centimeter)]
        [InlineData((int)XGraphicsUnit.Presentation)]
        public void ConvertType_ToEachUnit_PreservesPointValue(int targetValue)
        {
            var target = (XGraphicsUnit)targetValue;
            var u = XUnit.FromPoint(72);
            u.ConvertType(target);

            Assert.Equal(72, u.Point, Tolerance);
        }

        [Fact]
        public void Zero_HasZeroValueInPoint()
        {
            Assert.Equal(0, XUnit.Zero.Value);
            Assert.Equal(XGraphicsUnit.Point, XUnit.Zero.Type);
        }
    }
}

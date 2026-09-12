using PeachPDF.MathML;
using Xunit;

namespace PeachPDF.Tests.MathML
{
    public class MathAttributeParserTests
    {
        [Theory]
        [InlineData("2pt", 2, "Pt")]
        [InlineData("1.5em", 1.5, "Em")]
        [InlineData("50%", 50, "Percent")]
        [InlineData("3px", 3, "Px")]
        [InlineData("2in", 2, "In")]
        [InlineData("1cm", 1, "Cm")]
        [InlineData("10mm", 10, "Mm")]
        [InlineData("2pc", 2, "Pc")]
        [InlineData("0.5ex", 0.5, "Ex")]
        public void TryParseLength_ParsesNumberAndUnit(string value, double expectedValue, string expectedUnitName)
        {
            var length = MathAttributeParser.TryParseLength(value);
            Assert.NotNull(length);
            Assert.Equal(expectedValue, length!.Value.Value, 3);
            Assert.Equal(expectedUnitName, length.Value.Unit.ToString());
        }

        [Fact]
        public void TryParseLength_BareZero_IsZeroPoints()
        {
            var length = MathAttributeParser.TryParseLength("0");
            Assert.Equal(MathLength.Zero, length);
        }

        [Theory]
        [InlineData("thinmathspace", 3)]
        [InlineData("mediummathspace", 4)]
        [InlineData("thickmathspace", 5)]
        [InlineData("veryverythinmathspace", 1)]
        [InlineData("veryverythickmathspace", 7)]
        [InlineData("negativethinmathspace", -3)]
        [InlineData("negativeveryverythickmathspace", -7)]
        public void TryParseLength_NamedSpaces_ResolveToMuMultiples(string keyword, double expectedMu)
        {
            var length = MathAttributeParser.TryParseLength(keyword);
            Assert.NotNull(length);
            Assert.Equal(expectedMu, length!.Value.Value);
            Assert.Equal(MathLengthUnit.Mu, length.Value.Unit);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-length")]
        [InlineData("5")]
        public void TryParseLength_InvalidOrUnitlessNonZero_ReturnsNull(string? value)
        {
            Assert.Null(MathAttributeParser.TryParseLength(value));
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("false", false)]
        [InlineData("invalid", null)]
        [InlineData(null, null)]
        public void TryParseBool_ParsesMathMlBooleans(string? value, bool? expected)
        {
            Assert.Equal(expected, MathAttributeParser.TryParseBool(value));
        }

        [Theory]
        [InlineData("prefix", "prefix")]
        [InlineData("infix", "infix")]
        [InlineData("postfix", "postfix")]
        [InlineData("nonsense", null)]
        public void TryParseForm_ParsesTheThreeValidForms(string value, string? expected)
        {
            Assert.Equal(expected, MathAttributeParser.TryParseForm(value));
        }

        [Theory]
        [InlineData("+2", 3, 5)]
        [InlineData("-1", 3, 2)]
        [InlineData("2", 3, 2)]
        [InlineData(null, 3, 3)]
        [InlineData("garbage", 3, 3)]
        public void ParseScriptLevel_HandlesRelativeAbsoluteAndFallback(string? value, int inherited, int expected)
        {
            Assert.Equal(expected, MathAttributeParser.ParseScriptLevel(value, inherited));
        }

        [Theory]
        [InlineData("true", true, true)]
        [InlineData("false", true, false)]
        [InlineData(null, true, true)]
        [InlineData("bogus", false, false)]
        public void ParseDisplayStyle_HandlesFallback(string? value, bool inherited, bool expected)
        {
            Assert.Equal(expected, MathAttributeParser.ParseDisplayStyle(value, inherited));
        }

        [Theory]
        [InlineData("small", 10.0, 7.1)]
        [InlineData("normal", 10.0, 10.0)]
        [InlineData("big", 10.0, 14.1)]
        [InlineData(null, 10.0, 10.0)]
        public void ParseMathSize_KeywordsAreRatiosOfInherited(string? value, double inheritedPt, double expectedPt)
        {
            Assert.Equal(expectedPt, MathAttributeParser.ParseMathSize(value, inheritedPt), 2);
        }

        [Fact]
        public void ParseMathSize_EmIsRelativeToInherited()
        {
            Assert.Equal(20.0, MathAttributeParser.ParseMathSize("2em", 10.0), 3);
        }

        [Fact]
        public void ParseMathSize_PointsAreAbsolute()
        {
            Assert.Equal(18.0, MathAttributeParser.ParseMathSize("18pt", 10.0), 3);
        }

        [Theory]
        [InlineData("20px", 15.0)] // 20 * 0.75
        [InlineData("150%", 15.0)] // 150% of 10
        [InlineData("0.2in", 14.4)] // 0.2 * 72
        [InlineData("0.5cm", 14.173228346456694)] // 0.5 * 72 / 2.54
        [InlineData("5mm", 14.173228346456694)] // 5 * 72 / 25.4
        [InlineData("1pc", 12.0)]
        [InlineData("1ex", 10.0)] // ex is meaningless for a font-size - falls back to inherited
        [InlineData("garbage", 10.0)] // unparseable - falls back to inherited
        public void ParseMathSize_ConvertsEveryOtherUnit(string value, double expectedPt)
        {
            Assert.Equal(expectedPt, MathAttributeParser.ParseMathSize(value, 10.0), 6);
        }

        [Fact]
        public void TryParseColor_None_ReturnsNull()
        {
            var adapter = new PeachPDF.Adapters.PdfSharpAdapter();
            Assert.Null(MathAttributeParser.TryParseColor(null, adapter));
            Assert.Null(MathAttributeParser.TryParseColor("", adapter));
            Assert.Null(MathAttributeParser.TryParseColor("none", adapter));
            Assert.Null(MathAttributeParser.TryParseColor("transparent", adapter));
        }

        [Fact]
        public void TryParseColor_ValidCssColor_ResolvesRealColor()
        {
            var adapter = new PeachPDF.Adapters.PdfSharpAdapter();
            var color = MathAttributeParser.TryParseColor("red", adapter);
            Assert.NotNull(color);
            Assert.Equal(255, color!.Value.R);
            Assert.Equal(0, color.Value.G);
            Assert.Equal(0, color.Value.B);
        }
    }
}

using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Svg;

namespace PeachPDF.Tests.Svg
{
    public class SvgValueParsersTests
    {
        private static readonly PdfSharpAdapter Adapter = new();

        [Fact]
        public void ParseViewBox_FourNumbers_ReturnsRect()
        {
            var viewBox = SvgValueParsers.ParseViewBox("0 0 299.667 207.333");

            Assert.NotNull(viewBox);
            var r = viewBox!.Value;
            Assert.Equal(0, r.X);
            Assert.Equal(0, r.Y);
            Assert.Equal(299.667, r.Width, 3);
            Assert.Equal(207.333, r.Height, 3);
        }

        [Fact]
        public void ParseViewBox_MissingValue_ReturnsNull()
        {
            Assert.Null(SvgValueParsers.ParseViewBox(null));
            Assert.Null(SvgValueParsers.ParseViewBox("0 0 100"));
        }

        [Theory]
        [InlineData("299.667px", 299.667)]
        [InlineData("299.667", 299.667)]
        public void ParseLength_PlainOrPixelSuffix_ReturnsValue(string value, double expected)
        {
            Assert.Equal(expected, SvgValueParsers.ParseLength(value)!.Value, 3);
        }

        [Fact]
        public void ParseLength_Percentage_WithNoReferenceLength_ReturnsNull()
        {
            Assert.Null(SvgValueParsers.ParseLength("50%"));
        }

        [Theory]
        [InlineData("50%", 200.0, 100.0)]
        [InlineData("100%", 200.0, 200.0)]
        [InlineData("0%", 200.0, 0.0)]
        public void ParseLength_Percentage_WithReferenceLength_Resolves(string value, double referenceLength, double expected)
        {
            Assert.Equal(expected, SvgValueParsers.ParseLength(value, referenceLength)!.Value, 3);
        }

        [Theory]
        [InlineData("1in", 96.0)]
        [InlineData("1pt", 96.0 / 72.0)]
        [InlineData("1pc", 16.0)]
        [InlineData("1cm", 96.0 / 2.54)]
        [InlineData("1mm", 96.0 / 25.4)]
        [InlineData("1em", 16.0)]
        [InlineData("1rem", 16.0)]
        [InlineData("2rem", 32.0)]
        public void ParseLength_UnitSuffix_ConvertsToPixels(string value, double expected)
        {
            Assert.Equal(expected, SvgValueParsers.ParseLength(value)!.Value, 3);
        }

        [Fact]
        public void ParseLength_UnrecognizedSuffix_ReturnsNull()
        {
            Assert.Null(SvgValueParsers.ParseLength("10vh"));
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("nonzero", false)]
        [InlineData("evenodd", true)]
        [InlineData("EVENODD", true)]
        [InlineData("bogus", false)]
        public void ParseFillRule_ReturnsExpectedMode(string? value, bool expectEvenOdd)
        {
            var expected = expectEvenOdd ? RFillMode.EvenOdd : RFillMode.Nonzero;
            Assert.Equal(expected, SvgValueParsers.ParseFillRule(value));
        }

        [Fact]
        public void ParseOpacity_MissingValue_DefaultsToFullyOpaque()
        {
            Assert.Equal(1.0, SvgValueParsers.ParseOpacity(null));
        }

        [Theory]
        [InlineData("0.5", 0.5)]
        [InlineData("50%", 0.5)]
        [InlineData("2", 1.0)]
        [InlineData("-1", 0.0)]
        public void ParseOpacity_ClampsToUnitRange(string value, double expected)
        {
            Assert.Equal(expected, SvgValueParsers.ParseOpacity(value), 6);
        }

        [Fact]
        public void ParsePaint_None_ReturnsNonePaint()
        {
            var paint = SvgValueParsers.ParsePaint("none", Adapter, RColor.Black);
            Assert.Equal(SvgPaintKind.None, paint.Kind);
        }

        [Fact]
        public void ParsePaint_UrlReference_ReturnsGradientRefWithId()
        {
            var paint = SvgValueParsers.ParsePaint("url(#SVGID_1_)", Adapter, RColor.Black);
            Assert.Equal(SvgPaintKind.GradientRef, paint.Kind);
            Assert.Equal("SVGID_1_", paint.ReferenceId);
        }

        [Fact]
        public void ParsePaint_HexColor_ReturnsSolidColor()
        {
            var paint = SvgValueParsers.ParsePaint("#431300", Adapter, RColor.Black);
            Assert.Equal(SvgPaintKind.Solid, paint.Kind);
            Assert.Equal(RColor.FromArgb(0x43, 0x13, 0x00), paint.Color);
        }

        [Fact]
        public void ParseStopColor_PlainAttribute_ReturnsColor()
        {
            var color = SvgValueParsers.ParseStopColor("#A07335", null, null, Adapter);
            Assert.Equal(RColor.FromArgb(0xA0, 0x73, 0x35), color);
        }

        [Fact]
        public void ParseStopColor_StyleAttributeOverridesPlainAttribute()
        {
            var color = SvgValueParsers.ParseStopColor("#000000", null, "stop-color:#FFFFFF", Adapter);
            Assert.Equal(RColor.White, color);
        }

        [Fact]
        public void ParseStopColor_WithOpacity_BakesAlphaIntoColor()
        {
            var color = SvgValueParsers.ParseStopColor("#FFFFFF", "0.5", null, Adapter);
            Assert.Equal(128, color.A);
        }

        [Theory]
        [InlineData(null, false, false)]
        [InlineData("butt", false, false)]
        [InlineData("round", true, false)]
        [InlineData("square", false, true)]
        public void ParseLineCap_ReturnsExpectedValue(string? value, bool expectRound, bool expectSquare)
        {
            var expected = expectRound ? RLineCap.Round : expectSquare ? RLineCap.Square : RLineCap.Butt;
            Assert.Equal(expected, SvgValueParsers.ParseLineCap(value));
        }

        [Theory]
        [InlineData(null, false, false)]
        [InlineData("miter", false, false)]
        [InlineData("round", true, false)]
        [InlineData("bevel", false, true)]
        public void ParseLineJoin_ReturnsExpectedValue(string? value, bool expectRound, bool expectBevel)
        {
            var expected = expectRound ? RLineJoin.Round : expectBevel ? RLineJoin.Bevel : RLineJoin.Miter;
            Assert.Equal(expected, SvgValueParsers.ParseLineJoin(value));
        }

        [Fact]
        public void ParseDashArray_None_ReturnsEmptyArray()
        {
            Assert.Empty(SvgValueParsers.ParseDashArray("none", null)!);
        }

        [Fact]
        public void ParseDashArray_MissingValue_ReturnsNull()
        {
            Assert.Null(SvgValueParsers.ParseDashArray(null, null));
        }

        [Fact]
        public void ParseDashArray_EvenCount_ReturnsAsIs()
        {
            var result = SvgValueParsers.ParseDashArray("5,3,2,1", null);
            Assert.Equal([5, 3, 2, 1], result);
        }

        [Fact]
        public void ParseDashArray_OddCount_IsDuplicatedToEvenCount()
        {
            var result = SvgValueParsers.ParseDashArray("5 3 2", null);
            Assert.Equal([5, 3, 2, 5, 3, 2], result);
        }

        [Fact]
        public void ParseDashArray_AllZeros_ReturnsEmptyArray()
        {
            Assert.Empty(SvgValueParsers.ParseDashArray("0,0", null)!);
        }

        [Fact]
        public void ParseDashArray_NegativeValue_ReturnsNull()
        {
            Assert.Null(SvgValueParsers.ParseDashArray("5,-3", null));
        }

        [Fact]
        public void ParseDashArray_PercentageResolvesAgainstReferenceLength()
        {
            var result = SvgValueParsers.ParseDashArray("50%,25%", 100);
            Assert.Equal([50, 25], result);
        }

        [Fact]
        public void ParsePreserveAspectRatio_MissingValue_ReturnsDefault()
        {
            Assert.Equal(SvgPreserveAspectRatio.Default, SvgValueParsers.ParsePreserveAspectRatio(null));
        }

        [Theory]
        [InlineData("xMinYMin", "XMinYMin")]
        [InlineData("xMidYMid", "XMidYMid")]
        [InlineData("xMaxYMax", "XMaxYMax")]
        [InlineData("none", "None")]
        [InlineData("XMINYMIN", "XMinYMin")]
        public void ParsePreserveAspectRatio_ParsesAlign(string value, string expected)
        {
            Assert.Equal(expected, SvgValueParsers.ParsePreserveAspectRatio(value).Align.ToString());
        }

        [Theory]
        [InlineData("xMidYMid meet", false)]
        [InlineData("xMidYMid slice", true)]
        [InlineData("xMidYMid", false)]
        public void ParsePreserveAspectRatio_ParsesMeetOrSlice(string value, bool expectSlice)
        {
            Assert.Equal(expectSlice, SvgValueParsers.ParsePreserveAspectRatio(value).Slice);
        }

        [Fact]
        public void ParsePreserveAspectRatio_DeferKeyword_IsSkipped()
        {
            var result = SvgValueParsers.ParsePreserveAspectRatio("defer xMinYMax slice");
            Assert.Equal(SvgAlign.XMinYMax, result.Align);
            Assert.True(result.Slice);
        }

        [Fact]
        public void ParsePaint_CurrentColor_ResolvesToContextColor()
        {
            var paint = SvgValueParsers.ParsePaint("currentColor", Adapter, RColor.FromArgb(0x11, 0x22, 0x33));
            Assert.Equal(SvgPaintKind.Solid, paint.Kind);
            Assert.Equal(RColor.FromArgb(0x11, 0x22, 0x33), paint.Color);
        }

        [Theory]
        [InlineData(null, false, false)]
        [InlineData("pad", false, false)]
        [InlineData("reflect", true, false)]
        [InlineData("repeat", false, true)]
        public void ParseSpreadMethod_ReturnsExpectedValue(string? value, bool expectReflect, bool expectRepeat)
        {
            var expected = expectReflect ? SvgSpreadMethod.Reflect : expectRepeat ? SvgSpreadMethod.Repeat : SvgSpreadMethod.Pad;
            Assert.Equal(expected, SvgValueParsers.ParseSpreadMethod(value));
        }

        [Fact]
        public void ParseGradientCoordinate_ObjectBoundingBox_BareNumberIsFraction()
        {
            Assert.Equal(0.25, SvgValueParsers.ParseGradientCoordinate("0.25", isObjectBoundingBox: true, userSpaceReferenceLength: null));
        }

        [Fact]
        public void ParseGradientCoordinate_ObjectBoundingBox_PercentageConvertsToFraction()
        {
            Assert.Equal(0.5, SvgValueParsers.ParseGradientCoordinate("50%", isObjectBoundingBox: true, userSpaceReferenceLength: null));
        }

        [Fact]
        public void ParseGradientCoordinate_UserSpaceOnUse_DelegatesToParseLength()
        {
            Assert.Equal(50, SvgValueParsers.ParseGradientCoordinate("50%", isObjectBoundingBox: false, userSpaceReferenceLength: 100));
        }

        [Fact]
        public void ParseStyleDeclarations_MissingValue_ReturnsEmptyDictionary()
        {
            Assert.Empty(SvgValueParsers.ParseStyleDeclarations(null));
            Assert.Empty(SvgValueParsers.ParseStyleDeclarations(""));
        }

        [Fact]
        public void ParseStyleDeclarations_ParsesMultipleDeclarations()
        {
            var declarations = SvgValueParsers.ParseStyleDeclarations("fill: red; stroke:blue ; opacity : 0.5");

            Assert.Equal("red", declarations["fill"]);
            Assert.Equal("blue", declarations["stroke"]);
            Assert.Equal("0.5", declarations["opacity"]);
        }

        [Fact]
        public void ParseStyleDeclarations_MissingTrailingSemicolon_StillParsesLastDeclaration()
        {
            var declarations = SvgValueParsers.ParseStyleDeclarations("fill: red; stroke: blue");
            Assert.Equal("blue", declarations["stroke"]);
        }

        [Fact]
        public void ParseStyleDeclarations_PropertyNameIsCaseInsensitive()
        {
            var declarations = SvgValueParsers.ParseStyleDeclarations("FILL: red");
            Assert.Equal("red", declarations["fill"]);
        }

        [Fact]
        public void ParseStyleDeclarations_MalformedDeclarationWithoutColon_IsSkipped()
        {
            var declarations = SvgValueParsers.ParseStyleDeclarations("not-a-declaration; fill: red");
            Assert.Single(declarations);
            Assert.Equal("red", declarations["fill"]);
        }
    }
}

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
        public void ParseLength_Percentage_ReturnsNull()
        {
            Assert.Null(SvgValueParsers.ParseLength("50%"));
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
            var paint = SvgValueParsers.ParsePaint("none", Adapter);
            Assert.Equal(SvgPaintKind.None, paint.Kind);
        }

        [Fact]
        public void ParsePaint_UrlReference_ReturnsGradientRefWithId()
        {
            var paint = SvgValueParsers.ParsePaint("url(#SVGID_1_)", Adapter);
            Assert.Equal(SvgPaintKind.GradientRef, paint.Kind);
            Assert.Equal("SVGID_1_", paint.GradientId);
        }

        [Fact]
        public void ParsePaint_HexColor_ReturnsSolidColor()
        {
            var paint = SvgValueParsers.ParsePaint("#431300", Adapter);
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
    }
}

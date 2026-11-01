using PeachBrackets.Css;
using PeachBrackets.Engine.Tests.CSS;
using PeachBrackets.Html.Parse;
using PeachBrackets.Svg;

namespace PeachBrackets.Engine.Tests.Html.Core.Parse
{
    public class AlphaValueTests : CssConstructionFunctions
    {
        [Theory]
        [InlineData("0.5", 0.5)]
        [InlineData("50%", 0.5)]
        [InlineData("calc(0.25 + 0.25)", 0.5)]
        [InlineData("calc(50%)", 0.5)]
        [InlineData("calc(100% / 4)", 0.25)]
        [InlineData("min(0.9, 0.3)", 0.3)]
        [InlineData("calc(2 * 0.75)", 1.5)]
        public void AnAlphaValue_ParsesToItsNumber(string text, double expected)
        {
            Assert.True(CssValueParser.TryParseAlphaValue(text, out var value));
            Assert.Equal(expected, value, 6);
        }

        [Theory]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("calc(1px)")]
        [InlineData("calc(0.5 + 10%)")]
        [InlineData("rgb(1 2 3)")]
        [InlineData("calc(")]
        public void ANonAlphaValue_IsRejected(string text) =>
            Assert.False(CssValueParser.TryParseAlphaValue(text, out _));

        [Fact]
        public void TheSvgOpacityParser_AcceptsCalcAndClamps()
        {
            Assert.True(SvgValueParsers.TryParseOpacity("calc(50%)", out var half));
            Assert.Equal(0.5, half, 6);
            Assert.True(SvgValueParsers.TryParseOpacity("calc(3 * 1)", out var clamped));
            Assert.Equal(1.0, clamped);
            Assert.False(SvgValueParsers.TryParseOpacity("calc(1px)", out _));
        }

        [Theory]
        [InlineData("opacity: calc(0.5)")]
        [InlineData("opacity: calc(50%)")]
        public void TheOpacityDeclaration_AcceptsCalc(string snippet)
        {
            var property = ParseDeclaration(snippet);

            Assert.IsType<OpacityProperty>(property);
            Assert.True(property.HasValue);
        }
    }
}

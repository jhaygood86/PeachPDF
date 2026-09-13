namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using PeachPDF.Html.Core.Parse;
    using System.Linq;
    using Xunit;

    /// <summary>
    /// Tests for the shared <see cref="FilterGrammar"/> (the <c>filter</c> value grammar
    /// <c>none | &lt;filter-function&gt;+</c>, space-separated). Mirrors
    /// <see cref="BoxShadowGrammarTests"/>'s structure and, for <c>drop-shadow()</c>, its reused
    /// classifier coverage (invalid inset/negative-blur/too-many-lengths cases).
    /// </summary>
    public class FilterGrammarTests
    {
        private static System.Collections.Generic.List<FilterGrammar.FilterFunction> Parse(string value) =>
            FilterGrammar.TryParse(CssValueParser.GetCssTokens(value));

        [Fact]
        public void None_ReturnsEmptyList()
        {
            var functions = Parse("none");
            Assert.NotNull(functions);
            Assert.Empty(functions);
        }

        [Theory]
        [InlineData("")]
        [InlineData("banana")]
        [InlineData("banana()")]           // not one of the nine known function names
        [InlineData("opacity(0.5) banana")] // one valid, one invalid - whole value rejected
        [InlineData("opacity(0.5), brightness(1)")] // comma-separated: filter has no separator at all
        public void Invalid_ReturnsNull(string value)
        {
            Assert.Null(Parse(value));
        }

        [Theory]
        [InlineData("blur")]
        [InlineData("brightness")]
        [InlineData("contrast")]
        [InlineData("grayscale")]
        [InlineData("hue-rotate")]
        [InlineData("invert")]
        [InlineData("opacity")]
        [InlineData("saturate")]
        [InlineData("sepia")]
        public void EveryStandardFunction_WithNoArgument_ParsesWithEmptyArguments(string name)
        {
            var function = Assert.Single(Parse($"{name}()"));
            Assert.Equal(name, function.Name);
            Assert.Empty(function.Arguments);
        }

        [Fact]
        public void FunctionName_IsCaseInsensitive_AndLowercased()
        {
            var function = Assert.Single(Parse("BRIGHTNESS(150%)"));
            Assert.Equal("brightness", function.Name);
        }

        [Theory]
        [InlineData("brightness(150%)", "150%")]
        [InlineData("contrast(0.5)", "0.5")]
        [InlineData("invert(1)", "1")]
        [InlineData("opacity(80%)", "80%")]
        [InlineData("grayscale(50%)", "50%")]
        [InlineData("sepia(1)", "1")]
        [InlineData("saturate(2)", "2")]
        public void SingleArgumentFunctions_CaptureTheArgument(string value, string expectedArgument)
        {
            var function = Assert.Single(Parse(value));
            var argument = Assert.Single(function.Arguments);
            Assert.Equal(expectedArgument, argument);
        }

        [Theory]
        [InlineData("hue-rotate(45deg)")]
        [InlineData("hue-rotate(0.5turn)")]
        [InlineData("hue-rotate(100grad)")]
        [InlineData("hue-rotate(1rad)")]
        [InlineData("hue-rotate(0)")] // the unitless-zero exception (CSS Values 4 §6.1)
        public void HueRotate_AcceptsEveryAngleUnitAndUnitlessZero(string value)
        {
            Assert.Single(Parse(value));
        }

        [Theory]
        [InlineData("hue-rotate(45)")]     // missing unit, non-zero
        [InlineData("hue-rotate(45%)")]    // percentage is not a valid angle
        [InlineData("hue-rotate(45px)")]   // wrong dimension unit
        [InlineData("brightness(-1)")]     // negative amount rejected
        [InlineData("opacity(-10%)")]      // negative percentage rejected
        [InlineData("blur(-5px)")]         // negative length rejected
        [InlineData("blur(50%)")]          // percentage is not a valid length
        [InlineData("brightness(1, 2)")]   // more than one argument
        public void InvalidArguments_ReturnNull(string value)
        {
            Assert.Null(Parse(value));
        }

        [Fact]
        public void Blur_AcceptsANonNegativeLength()
        {
            var function = Assert.Single(Parse("blur(5px)"));
            Assert.Equal("5px", Assert.Single(function.Arguments));
        }

        [Fact]
        public void MultipleFunctions_SpaceSeparated_ParseInOrder()
        {
            var functions = Parse("grayscale(50%) brightness(1.2) opacity(0.8)");
            Assert.Equal(3, functions.Count);
            Assert.Equal("grayscale", functions[0].Name);
            Assert.Equal("brightness", functions[1].Name);
            Assert.Equal("opacity", functions[2].Name);
        }

        // ─── drop-shadow() ───────────────────────────────────────────────────

        [Fact]
        public void DropShadow_TwoLengths_OffsetsOnly_DefaultsBlurAndColor()
        {
            var function = Assert.Single(Parse("drop-shadow(2px 3px)"));
            Assert.Equal("drop-shadow", function.Name);
            Assert.Equal(["2px", "3px", "0", ""], function.Arguments);
        }

        [Fact]
        public void DropShadow_ThreeLengths_HasBlur()
        {
            var function = Assert.Single(Parse("drop-shadow(2px 3px 5px)"));
            Assert.Equal(["2px", "3px", "5px", ""], function.Arguments);
        }

        [Theory]
        [InlineData("drop-shadow(2px 3px 5px red)", "red")]
        [InlineData("drop-shadow(red 2px 3px 5px)", "red")] // color can lead the lengths
        [InlineData("drop-shadow(2px 3px 5px #08f)", "#08f")]
        [InlineData("drop-shadow(2px 3px 5px rgba(0,0,0,.5))", "rgba(0,0,0,.5)")]
        public void DropShadow_ColorIsCaptured(string value, string expectedColor)
        {
            var function = Assert.Single(Parse(value));
            Assert.Equal(expectedColor, function.Arguments[3]);
        }

        [Theory]
        [InlineData("drop-shadow(2px)")]                 // only one length
        [InlineData("drop-shadow(1px 1px 1px 1px)")]     // four lengths - no spread on drop-shadow
        [InlineData("drop-shadow(inset 2px 3px)")]       // inset is not legal on drop-shadow
        [InlineData("drop-shadow(2px 3px -5px)")]        // negative blur radius
        [InlineData("drop-shadow(2px 3px red blue)")]    // two colors
        [InlineData("drop-shadow()")]                    // no lengths at all
        public void DropShadow_Invalid_ReturnsNull(string value)
        {
            Assert.Null(Parse(value));
        }
    }
}

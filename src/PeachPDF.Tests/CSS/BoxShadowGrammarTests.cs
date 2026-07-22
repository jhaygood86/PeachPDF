namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using PeachPDF.Html.Core.Parse;
    using System.Linq;
    using Xunit;

    /// <summary>
    /// Tests for the shared <see cref="BoxShadowGrammar"/> (the <c>box-shadow</c> value grammar
    /// <c>none | [ inset? &amp;&amp; &lt;length&gt;{2,4} &amp;&amp; &lt;color&gt;? ]#</c>) and its
    /// Layer-A accept/reject via the full parser.
    /// </summary>
    public class BoxShadowGrammarTests : CssConstructionFunctions
    {
        private static System.Collections.Generic.List<BoxShadowGrammar.ShadowLayer> Parse(string value) =>
            BoxShadowGrammar.TryParse(CssValueParser.GetCssTokens(value));

        [Fact]
        public void None_ReturnsEmptyList()
        {
            var layers = Parse("none");
            Assert.NotNull(layers);
            Assert.Empty(layers);
        }

        [Fact]
        public void TwoLengths_OffsetsOnly()
        {
            var layers = Parse("2px 3px");
            var layer = Assert.Single(layers);
            Assert.False(layer.Inset);
            Assert.Equal("2px", layer.OffsetX);
            Assert.Equal("3px", layer.OffsetY);
            Assert.Equal("0", layer.Blur);
            Assert.Equal("0", layer.Spread);
            Assert.Null(layer.Color);
        }

        [Fact]
        public void ThreeLengths_HasBlur()
        {
            var layer = Assert.Single(Parse("2px 2px 5px"));
            Assert.Equal("5px", layer.Blur);
            Assert.Equal("0", layer.Spread);
        }

        [Fact]
        public void FourLengths_HasBlurAndSpread()
        {
            var layer = Assert.Single(Parse("1px 1px 2px 3px"));
            Assert.Equal("2px", layer.Blur);
            Assert.Equal("3px", layer.Spread);
        }

        [Fact]
        public void Inset_WithColor()
        {
            var layer = Assert.Single(Parse("inset 0 0 5px red"));
            Assert.True(layer.Inset);
            Assert.Equal("0", layer.OffsetX);
            Assert.Equal("0", layer.OffsetY);
            Assert.Equal("5px", layer.Blur);
            Assert.Equal("red", layer.Color);
        }

        [Theory]
        [InlineData("2px 2px red", "red")]
        [InlineData("2px 2px #fff", "#fff")]        // letter-leading hex (Hash token)
        [InlineData("2px 2px #08f", "#08f")]        // digit-leading short hex (Delim + dimension)
        [InlineData("2px 2px #000", "#000")]        // digit-leading hex, all digits (Delim + number)
        [InlineData("2px 2px #0088ff", "#0088ff")]  // digit-leading long hex
        [InlineData("2px 2px rgba(0,0,0,.5)", "rgba(0,0,0,.5)")]
        [InlineData("2px 2px currentColor", "currentColor")]
        public void Color_IsCaptured(string value, string expectedColor)
        {
            var layer = Assert.Single(Parse(value));
            Assert.Equal(expectedColor, layer.Color);
        }

        [Fact]
        public void ColorBeforeLengths_IsAccepted()
        {
            // The && grammar allows the color in any position, including before the lengths.
            var layer = Assert.Single(Parse("red 2px 2px"));
            Assert.Equal("red", layer.Color);
            Assert.Equal("2px", layer.OffsetX);
        }

        [Fact]
        public void EmValidLengthsAreKeptAsAuthoredStrings()
        {
            var layer = Assert.Single(Parse("0.5em 0.5em 1em"));
            Assert.Equal("0.5em", layer.OffsetX);
            Assert.Equal("1em", layer.Blur);
        }

        [Fact]
        public void NegativeOffsetsAndSpread_AreValid()
        {
            var layer = Assert.Single(Parse("-2px -3px 4px -1px"));
            Assert.Equal("-2px", layer.OffsetX);
            Assert.Equal("-3px", layer.OffsetY);
            Assert.Equal("-1px", layer.Spread);
        }

        [Fact]
        public void MultipleLayers_ParseInOrder()
        {
            var layers = Parse("1px 1px 2px 3px rgba(0,0,0,.5), inset 0 0 0 1px blue");
            Assert.Equal(2, layers.Count);
            Assert.False(layers[0].Inset);
            Assert.Equal("rgba(0,0,0,.5)", layers[0].Color);
            Assert.True(layers[1].Inset);
            Assert.Equal("blue", layers[1].Color);
        }

        [Theory]
        [InlineData("")]
        [InlineData("banana")]              // no lengths
        [InlineData("2px")]                 // only one length
        [InlineData("2px 2px 2px 2px 2px")] // five lengths
        [InlineData("inset inset 2px 2px")] // inset twice
        [InlineData("2px 2px -5px red")]    // negative blur radius
        [InlineData("2px red 2px")]         // non-contiguous lengths
        [InlineData("2px 2px banana")]      // invalid color keyword
        [InlineData("2px 2px #12")]         // "#" + a 2-digit number: not a valid hex length
        [InlineData("2px 2px #")]           // a bare "#" delimiter with nothing after it
        [InlineData("2px 50%")]             // percentage is not a valid length
        [InlineData("2px 2px 50%")]         // percentage where a color/length is expected
        [InlineData("2px 2px red blue")]    // two colors
        public void Invalid_ReturnsNull(string value)
        {
            Assert.Null(Parse(value));
        }

        [Theory]
        [InlineData("box-shadow: none", true)]
        [InlineData("box-shadow: 2px 2px", true)]
        [InlineData("box-shadow: inset 0 0 5px red", true)]
        [InlineData("box-shadow: 1px 1px 2px 3px rgba(0,0,0,.5), 0 0 0 1px blue", true)]
        [InlineData("box-shadow: banana", false)]
        [InlineData("box-shadow: 2px 50%", false)]
        public void LayerA_AcceptsValid_RejectsInvalid(string declaration, bool shouldApply)
        {
            var sheet = ParseStyleSheet($"div {{ {declaration}; }}");
            var style = sheet.Rules.OfType<StyleRule>().Single().Style;
            var applied = !string.IsNullOrEmpty(style.GetPropertyValue("box-shadow"));
            Assert.Equal(shouldApply, applied);
        }
    }
}

namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using Xunit;

    /// <summary>CSS-OM parsing of <c>font-synthesis</c> (shorthand and longhands) and the CSS Fonts 5 <c>font-size-adjust</c> grammar.</summary>
    public class FontSynthesisAndSizeAdjustParsingTests : CssConstructionFunctions
    {
        [Theory]
        [InlineData("font-synthesis: none", "none", "none", "none", "none")]
        [InlineData("font-synthesis: weight style small-caps position", "auto", "auto", "auto", "auto")]
        [InlineData("font-synthesis: weight", "auto", "none", "none", "none")]
        [InlineData("font-synthesis: style position", "none", "auto", "none", "auto")]
        [InlineData("font-synthesis: small-caps weight", "auto", "none", "auto", "none")]
        public void Shorthand_ExpandsListedToAuto_AndOmittedToNone(string declaration, string weight, string style, string smallCaps, string position)
        {
            var style_ = ParseDeclarations(declaration + ";");

            Assert.Equal(weight, style_.GetPropertyValue("font-synthesis-weight"));
            Assert.Equal(style, style_.GetPropertyValue("font-synthesis-style"));
            Assert.Equal(smallCaps, style_.GetPropertyValue("font-synthesis-small-caps"));
            Assert.Equal(position, style_.GetPropertyValue("font-synthesis-position"));
        }

        [Theory]
        [InlineData("font-synthesis: weight weight")]
        [InlineData("font-synthesis: none weight")]
        [InlineData("font-synthesis: bold")]
        [InlineData("font-synthesis: 1")]
        public void Shorthand_RejectsInvalidValues(string declaration)
        {
            var style = ParseDeclarations(declaration + ";");

            Assert.Equal(string.Empty, style.GetPropertyValue("font-synthesis-weight") ?? string.Empty);
        }

        [Theory]
        [InlineData("font-synthesis-weight", "none")]
        [InlineData("font-synthesis-style", "auto")]
        [InlineData("font-synthesis-small-caps", "none")]
        [InlineData("font-synthesis-position", "none")]
        public void Longhands_AcceptAutoAndNone(string name, string value)
        {
            var style = ParseDeclarations($"{name}: {value};");

            Assert.Equal(value, style.GetPropertyValue(name));
        }

        [Fact]
        public void Longhand_RejectsOtherKeywords()
        {
            var style = ParseDeclarations("font-synthesis-weight: bold;");

            Assert.True(string.IsNullOrEmpty(style.GetPropertyValue("font-synthesis-weight")));
        }

        [Theory]
        [InlineData("none", null, double.NaN)]
        [InlineData("0.5", "ExHeight", 0.5)]
        [InlineData("ex-height 0.5", "ExHeight", 0.5)]
        [InlineData("cap-height 0.7", "CapHeight", 0.7)]
        [InlineData("ch-width 0.4", "ChWidth", 0.4)]
        [InlineData("ic-width 1", "IcWidth", 1)]
        [InlineData("ic-height 0.9", "IcHeight", 0.9)]
        [InlineData("from-font", "ExHeight", double.NaN)]
        [InlineData("cap-height from-font", "CapHeight", double.NaN)]
        public void FontSizeAdjust_GrammarResolves(string text, string? metric, double value)
        {
            var parsed = ParseDeclarations($"font-size-adjust: {text};").GetPropertyValue("font-size-adjust");
            var resolved = FontSizeAdjustGrammar.Resolve(parsed);

            if (metric is null)
            {
                Assert.Null(resolved);
                return;
            }

            Assert.NotNull(resolved);
            Assert.Equal(metric, resolved!.Value.Metric.ToString());
            Assert.Equal(value, resolved.Value.Value);
        }

        [Theory]
        [InlineData("0.5 0.5")]
        [InlineData("-1")]
        [InlineData("cap-height")]
        [InlineData("bogus 0.5")]
        [InlineData("none 0.5")]
        public void FontSizeAdjust_RejectsInvalidValues(string text)
        {
            var style = ParseDeclarations($"font-size-adjust: {text};");

            Assert.True(string.IsNullOrEmpty(style.GetPropertyValue("font-size-adjust")));
        }
    }
}

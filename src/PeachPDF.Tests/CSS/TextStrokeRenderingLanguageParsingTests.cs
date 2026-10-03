namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using Xunit;

    /// <summary>CSS-OM parsing of <c>-webkit-text-stroke</c> (shorthand and longhands), <c>text-rendering</c> and <c>font-language-override</c>.</summary>
    public class TextStrokeRenderingLanguageParsingTests : CssConstructionFunctions
    {
        [Theory]
        [InlineData("-webkit-text-stroke: 2px red", "2px", "rgb(255, 0, 0)")]
        [InlineData("-webkit-text-stroke: red 2px", "2px", "rgb(255, 0, 0)")]
        [InlineData("-webkit-text-stroke: thin", "1px", "")]
        [InlineData("-webkit-text-stroke: #00f", "", "rgb(0, 0, 255)")]
        public void Shorthand_SetsWidthAndColourInEitherOrder(string declaration, string width, string color)
        {
            var style = ParseDeclarations(declaration + ";");

            if (width.Length > 0)
                Assert.Equal(width, style.GetPropertyValue("-webkit-text-stroke-width"));
            if (color.Length > 0)
                Assert.Equal(color, style.GetPropertyValue("-webkit-text-stroke-color"));
        }

        [Theory]
        [InlineData("-webkit-text-stroke: 2px 3px")]
        [InlineData("-webkit-text-stroke: red blue")]
        [InlineData("-webkit-text-stroke: bold")]
        public void Shorthand_RejectsInvalidValues(string declaration)
        {
            var style = ParseDeclarations(declaration + ";");

            Assert.True(string.IsNullOrEmpty(style.GetPropertyValue("-webkit-text-stroke-width")));
        }

        [Theory]
        [InlineData("-webkit-text-stroke-width", "3px")]
        [InlineData("-webkit-text-stroke-color", "rgb(0, 128, 0)")]
        [InlineData("text-rendering", "optimizeSpeed")]
        [InlineData("text-rendering", "optimizeLegibility")]
        [InlineData("text-rendering", "geometricPrecision")]
        [InlineData("text-rendering", "auto")]
        public void Longhands_AcceptTheirGrammar(string name, string value)
        {
            var style = ParseDeclarations($"{name}: {value};");

            Assert.Equal(value, style.GetPropertyValue(name), ignoreCase: true);
        }

        [Theory]
        [InlineData("text-rendering", "fast")]
        [InlineData("font-language-override", "SRB")]
        [InlineData("font-language-override", "'TOOLONG'")]
        [InlineData("font-language-override", "''")]
        [InlineData("font-language-override", "'SéR'")]
        public void Longhands_RejectInvalidValues(string name, string value)
        {
            var style = ParseDeclarations($"{name}: {value};");

            Assert.True(string.IsNullOrEmpty(style.GetPropertyValue(name)));
        }

        [Theory]
        [InlineData("normal", "normal")]
        [InlineData("'SRB'", "\"SRB\"")]
        [InlineData("\"ROM\"", "\"ROM\"")]
        [InlineData("'MOLD'", "\"MOLD\"")]
        public void LanguageOverride_AcceptsNormalAndAShortString(string value, string serialized)
        {
            var style = ParseDeclarations($"font-language-override: {value};");

            Assert.Equal(serialized, style.GetPropertyValue("font-language-override"));
        }

        [Theory]
        [InlineData("normal", null)]
        [InlineData("'SRB'", "SRB ")]
        [InlineData("'ROM'", "ROM ")]
        [InlineData("\"MOLD\"", "MOLD")]
        [InlineData("'X'", "X   ")]
        [InlineData("'TOOLONG'", null)]
        [InlineData("SRB", null)]
        [InlineData("", null)]
        public void Resolve_PadsTheStringToAFourCharacterTag(string authored, string? expected)
        {
            Assert.Equal(expected, FontLanguageOverrideGrammar.Resolve(authored));
        }
    }
}

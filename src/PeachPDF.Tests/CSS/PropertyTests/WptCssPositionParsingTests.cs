namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
    using System.Linq;
    using Xunit;

    /// <summary>
    /// Grammar coverage for <c>position</c>, the four physical inset longhands (<c>top</c>/<c>right</c>/
    /// <c>bottom</c>/<c>left</c>), and <c>z-index</c>, adapted from the web-platform-tests
    /// css/css-position/parsing/ fixtures (position-valid.html, position-invalid.html, top/right/bottom/
    /// left-valid.html, top/right/bottom/left-invalid.html, z-index-valid.html, z-index-invalid.html).
    /// WPT's test_valid_value/test_invalid_value assert against a live getComputedStyle(); this engine has
    /// no CSSOM computed-style API to match that against, so the equivalent here is whether the declaration
    /// round-trips through <see cref="StyleDeclaration.GetPropertyValue"/> (accepted) or is dropped
    /// entirely, leaving an empty string (rejected) - see CssConstructionFunctions.ParseStyleSheet.
    /// </summary>
    public class WptCssPositionParsingTests : CssConstructionFunctions
    {
        private static string DeclaredValue(string property, string value)
        {
            var sheet = ParseStyleSheet($".x {{ {property}: {value}; }}");
            var rule = sheet.Rules.OfType<StyleRule>().Single();
            return rule.Style.GetPropertyValue(property);
        }

        // ─── position: 'static | relative | absolute | sticky | fixed' ───────────

        [Theory]
        [InlineData("static")]
        [InlineData("relative")]
        [InlineData("absolute")]
        [InlineData("sticky")]
        [InlineData("fixed")]
        public void Position_AcceptsValidKeyword(string value)
        {
            Assert.Equal(value, DeclaredValue("position", value));
        }

        [Theory]
        [InlineData("auto")]
        [InlineData("static relative")]
        public void Position_RejectsInvalidValue(string value)
        {
            Assert.Equal(string.Empty, DeclaredValue("position", value));
        }

        // ─── top/right/bottom/left: 'auto | <length-percentage>' ─────────────────

        public static readonly TheoryData<string> CoordinateProperties = new()
        {
            "top", "right", "bottom", "left",
        };

        [Theory]
        [MemberData(nameof(CoordinateProperties))]
        public void Coordinate_AcceptsAuto(string property)
        {
            Assert.Equal("auto", DeclaredValue(property, "auto"));
        }

        [Theory]
        [MemberData(nameof(CoordinateProperties))]
        public void Coordinate_AcceptsNegativeLength(string property)
        {
            Assert.Equal("-10px", DeclaredValue(property, "-10px"));
        }

        [Theory]
        [MemberData(nameof(CoordinateProperties))]
        public void Coordinate_AcceptsNegativePercentage(string property)
        {
            Assert.Equal("-20%", DeclaredValue(property, "-20%"));
        }

        [Theory]
        [MemberData(nameof(CoordinateProperties))]
        public void Coordinate_AcceptsCalcExpression(string property)
        {
            Assert.False(string.IsNullOrEmpty(DeclaredValue(property, "calc(2em + 3ex)")));
        }

        [Theory]
        [MemberData(nameof(CoordinateProperties))]
        public void Coordinate_RejectsNonLengthKeyword(string property)
        {
            Assert.Equal(string.Empty, DeclaredValue(property, "min-content"));
        }

        [Theory]
        [MemberData(nameof(CoordinateProperties))]
        public void Coordinate_RejectsUnitlessNumber(string property)
        {
            Assert.Equal(string.Empty, DeclaredValue(property, "60"));
        }

        [Theory]
        [MemberData(nameof(CoordinateProperties))]
        public void Coordinate_RejectsMultipleValues(string property)
        {
            Assert.Equal(string.Empty, DeclaredValue(property, "10px 20%"));
        }

        // ─── z-index: 'auto | <integer>' ──────────────────────────────────────────

        [Theory]
        [InlineData("auto")]
        [InlineData("-789")]
        [InlineData("0")]
        [InlineData("123")]
        public void ZIndex_AcceptsValidValue(string value)
        {
            Assert.Equal(value, DeclaredValue("z-index", value));
        }

        [Theory]
        [InlineData("none")]
        [InlineData("10px")]
        [InlineData("0.5")]
        [InlineData("auto 123")]
        public void ZIndex_RejectsInvalidValue(string value)
        {
            Assert.Equal(string.Empty, DeclaredValue("z-index", value));
        }
    }
}

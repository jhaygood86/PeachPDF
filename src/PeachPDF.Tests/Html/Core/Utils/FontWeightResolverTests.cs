using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Tests.Html.Core.Utils
{
    /// <summary>
    /// Unit tests for <see cref="FontWeightResolver"/> - CSS2.1 §15.6's worked bolder/lighter step table,
    /// keyword/numeric passthrough. See <c>FontWeightResolutionIntegrationTests.cs</c> for the equivalent
    /// coverage through the real cascade (parent → child weight resolution).
    /// </summary>
    public class FontWeightResolverTests
    {
        [Theory]
        [InlineData("400", 400)]
        [InlineData("100", 100)]
        [InlineData("999", 999)]
        public void NumericValue_PassesThroughUnchanged(string value, int expected)
        {
            Assert.Equal(expected, FontWeightResolver.Resolve(value, parentWeight: 400));
        }

        [Theory]
        [InlineData("350.5", 350.5)]
        [InlineData("1.25", 1.25)]
        [InlineData(" 999.75 ", 999.75)]
        public void FractionalValue_PassesThroughUnchanged(string value, double expected)
        {
            Assert.Equal(expected, FontWeightResolver.Resolve(value, parentWeight: 400));
        }

        // Only weights where the CSS 2.1 table this resolver follows and the CSS Fonts 4 table agree are asserted for fractions.
        [Theory]
        [InlineData(300.5, 400)]
        [InlineData(450.5, 700)]
        [InlineData(600.5, 900)]
        [InlineData(800.5, 900)]
        public void Bolder_StepsFromAFractionalParent(double parentWeight, double expected)
        {
            Assert.Equal(expected, FontWeightResolver.Resolve("bolder", parentWeight));
        }

        [Theory]
        [InlineData(300.5, 100)]
        [InlineData(600.5, 400)]
        [InlineData(800.5, 700)]
        public void Lighter_StepsFromAFractionalParent(double parentWeight, double expected)
        {
            Assert.Equal(expected, FontWeightResolver.Resolve("lighter", parentWeight));
        }

        [Theory]
        [InlineData("350.5", true, 350.5)]
        [InlineData(" 12 ", true, 12)]
        [InlineData("-0.25", true, -0.25)]
        [InlineData("1e2", true, 100)]
        [InlineData("bold", false, 0)]
        [InlineData("", false, 0)]
        [InlineData("NaN", false, 0)]
        [InlineData("1e999", false, 0)]
        [InlineData("1,5", false, 0)]
        public void TryParseNumber_ReadsAFiniteNumberWithTheInvariantCulture(string text, bool expectedSuccess, double expected)
        {
            Assert.Equal(expectedSuccess, CssValueParser.TryParseNumber(text, out var result));
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Bold_Resolves700_RegardlessOfParent()
        {
            Assert.Equal(700, FontWeightResolver.Resolve("bold", parentWeight: 100));
        }

        [Fact]
        public void UnrecognizedKeyword_ResolvesToNormal400()
        {
            Assert.Equal(400, FontWeightResolver.Resolve("normal", parentWeight: 900));
        }

        [Theory]
        [InlineData(100, 400)]
        [InlineData(200, 400)]
        [InlineData(300, 400)]
        [InlineData(400, 700)]
        [InlineData(500, 700)]
        [InlineData(600, 900)]
        [InlineData(700, 900)]
        [InlineData(800, 900)]
        [InlineData(900, 900)]
        public void Bolder_MatchesCss21WorkedTable(int parentWeight, int expected)
        {
            Assert.Equal(expected, FontWeightResolver.Resolve("bolder", parentWeight));
        }

        [Theory]
        [InlineData(100, 100)]
        [InlineData(200, 100)]
        [InlineData(300, 100)]
        [InlineData(400, 100)]
        [InlineData(500, 100)]
        [InlineData(600, 400)]
        [InlineData(700, 400)]
        [InlineData(800, 700)]
        [InlineData(900, 700)]
        public void Lighter_MatchesCss21WorkedTable(int parentWeight, int expected)
        {
            Assert.Equal(expected, FontWeightResolver.Resolve("lighter", parentWeight));
        }
    }
}

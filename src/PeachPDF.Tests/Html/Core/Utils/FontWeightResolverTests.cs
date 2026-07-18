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

using PeachBrackets.Html.Dom;
using Xunit;

namespace PeachBrackets.Engine.Tests.Html.Core.Dom
{
    public class CssLengthRemTests
    {
        [Theory]
        [InlineData("1.5rem", 1.5)]
        [InlineData("2rem", 2)]
        public void ARemLength_IsAFlawlessRemUnit_NotAnEmError(string text, double number)
        {
            var length = new CssLength(text);

            Assert.False(length.HasError);
            Assert.Equal(CssUnit.Rem, length.Unit);
            Assert.True(length.IsRelative);
            Assert.Equal(number, length.Number);
        }

        [Fact]
        public void AnEmLength_StillReadsAsEms()
        {
            var length = new CssLength("2em");

            Assert.Equal(CssUnit.Ems, length.Unit);
            Assert.Equal(2, length.Number);
        }
    }
}

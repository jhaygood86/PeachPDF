using PeachBrackets.Html.Utils;
using Xunit;

namespace PeachBrackets.Engine.Tests.Html.Core.Utils
{
    public class BackgroundRepeatNormalizationTests
    {
        [Theory]
        [InlineData("repeat no-repeat", "repeat-x")]
        [InlineData("no-repeat repeat", "repeat-y")]
        [InlineData("repeat repeat", "repeat")]
        [InlineData("no-repeat no-repeat", "no-repeat")]
        [InlineData("  REPEAT   no-repeat ", "repeat-x")]
        [InlineData("repeat-x", "repeat-x")]
        [InlineData("space round", "space round")]
        [InlineData("", "")]
        public void NormalizeRepeat_ReducesPairsToTheKeywordForm(string input, string expected) =>
            Assert.Equal(expected, BackgroundLayerResolver.NormalizeRepeat(input));
    }
}

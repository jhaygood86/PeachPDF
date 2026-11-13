using PeachBrackets.Css;
using PeachBrackets.Html.Utils;

namespace PeachBrackets.Engine.Tests.Html.Core.Utils
{
    /// <summary><c>text-transform: full-size-kana</c> turns each small kana into the full-size kana it is a small form of (CSS Text 3 section 2.1).</summary>
    public class TextTransformerFullSizeKanaTests
    {
        [Theory]
        [InlineData('ぁ', 'あ')]
        [InlineData('っ', 'つ')]
        [InlineData('ゖ', 'け')]
        [InlineData('ァ', 'ア')]
        [InlineData('ッ', 'ツ')]
        [InlineData('ヶ', 'ケ')]
        [InlineData('ㇰ', 'ク')]
        [InlineData('ㇿ', 'ロ')]
        [InlineData('ｯ', 'ﾂ')]
        [InlineData('ｮ', 'ﾖ')]
        public void ASmallKanaBecomesItsFullSizeForm(char small, char full) =>
            Assert.Equal(full, TextTransformer.ToFullSizeKana(small));

        [Theory]
        [InlineData('あ')]
        [InlineData('ア')]
        [InlineData('a')]
        [InlineData('ー')]
        public void AnythingElseIsLeftAlone(char c) => Assert.Equal(c, TextTransformer.ToFullSizeKana(c));

        [Fact]
        public void ApplyingTheTransformMapsTheWholeText() =>
            Assert.Equal("つくば Aあ", TextTransformer.Apply("っくば Aぁ", TextTransform.FullSizeKana));
    }
}

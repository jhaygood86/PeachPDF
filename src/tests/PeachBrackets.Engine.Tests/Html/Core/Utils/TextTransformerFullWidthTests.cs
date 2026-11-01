using PeachBrackets.Html.Utils;

namespace PeachBrackets.Engine.Tests.Html.Core.Utils
{
    /// <summary>
    /// <c>text-transform: full-width</c> maps every code point whose Decomposition_Mapping is tagged &lt;wide&gt; or &lt;narrow&gt;
    /// (CSS Text 3, text-transform), one code point to one code point.
    /// </summary>
    public class TextTransformerFullWidthTests
    {
        [Theory]
        [InlineData('A', 'Ａ')]
        [InlineData(' ', '　')]
        [InlineData('¥', '￥')]
        [InlineData('｡', '。')]
        [InlineData('･', '・')]
        [InlineData('ｦ', 'ヲ')]
        [InlineData('ｧ', 'ァ')]
        [InlineData('ｰ', 'ー')]
        [InlineData('ｱ', 'ア')]
        [InlineData('ｶ', 'カ')]
        [InlineData('ﾊ', 'ハ')]
        [InlineData('ﾔ', 'ヤ')]
        [InlineData('ﾜ', 'ワ')]
        [InlineData('ﾝ', 'ン')]
        [InlineData('ﾞ', '゙')]
        [InlineData('ﾟ', '゚')]
        [InlineData('ﾠ', 'ㅤ')]
        [InlineData('ﾡ', 'ㄱ')]
        [InlineData('ﾾ', 'ㅎ')]
        [InlineData('ￂ', 'ㅏ')]
        [InlineData('ￇ', 'ㅔ')]
        [InlineData('ￊ', 'ㅕ')]
        [InlineData('ￏ', 'ㅚ')]
        [InlineData('ￒ', 'ㅛ')]
        [InlineData('ￗ', 'ㅠ')]
        [InlineData('ￚ', 'ㅡ')]
        [InlineData('ￜ', 'ㅣ')]
        [InlineData('￨', '│')]
        [InlineData('￩', '←')]
        [InlineData('￬', '↓')]
        [InlineData('￭', '■')]
        [InlineData('￮', '○')]
        [InlineData('⦅', '｟')]
        [InlineData('⦆', '｠')]
        public void MapsToTheFullWidthForm(char input, char expected) => Assert.Equal(expected, TextTransformer.ToFullWidth(input));

        [Theory]
        [InlineData('﾿')]
        [InlineData('￈')]
        [InlineData('￐')]
        [InlineData('￝')]
        [InlineData('￧')]
        [InlineData('￯')]
        [InlineData('あ')]
        public void LeavesACharacterWithNoFullWidthFormAlone(char input) => Assert.Equal(input, TextTransformer.ToFullWidth(input));
    }
}

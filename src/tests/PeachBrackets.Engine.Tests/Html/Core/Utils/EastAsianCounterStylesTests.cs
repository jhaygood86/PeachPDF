using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using Xunit;

namespace PeachBrackets.Engine.Tests.Html.Core.Utils
{
    /// <summary>
    /// The longhand East Asian counter styles of CSS Counter Styles Level 3 section 7.1. The expected strings are what Chromium writes
    /// for <c>counter(n, style)</c>, which is the reference for the Chinese zero and grouping rules and for the range each family
    /// covers.
    /// </summary>
    public class EastAsianCounterStylesTests
    {
        [Theory]
        // Informal: no 一 before a 十 that opens a group, 零 for a zero run between digits and between groups, never trailing.
        [InlineData("simp-chinese-informal", 0, "零")]
        [InlineData("simp-chinese-informal", 1, "一")]
        [InlineData("simp-chinese-informal", 10, "十")]
        [InlineData("simp-chinese-informal", 11, "十一")]
        [InlineData("simp-chinese-informal", 20, "二十")]
        [InlineData("simp-chinese-informal", 21, "二十一")]
        [InlineData("simp-chinese-informal", 100, "一百")]
        [InlineData("simp-chinese-informal", 101, "一百零一")]
        [InlineData("simp-chinese-informal", 110, "一百一十")]
        [InlineData("simp-chinese-informal", 111, "一百一十一")]
        [InlineData("simp-chinese-informal", 1000, "一千")]
        [InlineData("simp-chinese-informal", 1001, "一千零一")]
        [InlineData("simp-chinese-informal", 1010, "一千零一十")]
        [InlineData("simp-chinese-informal", 1100, "一千一百")]
        [InlineData("simp-chinese-informal", 2020, "二千零二十")]
        [InlineData("simp-chinese-informal", 9999, "九千九百九十九")]
        [InlineData("simp-chinese-informal", 10000, "一万")]
        [InlineData("simp-chinese-informal", 10001, "一万零一")]
        [InlineData("simp-chinese-informal", 10010, "一万零十")]
        [InlineData("simp-chinese-informal", 10100, "一万零一百")]
        [InlineData("simp-chinese-informal", 11000, "一万一千")]
        [InlineData("simp-chinese-informal", 12345, "一万二千三百四十五")]
        [InlineData("simp-chinese-informal", 20000, "二万")]
        [InlineData("simp-chinese-informal", 100000, "十万")]
        [InlineData("simp-chinese-informal", 100001, "十万零一")]
        [InlineData("simp-chinese-informal", 110000, "十一万")]
        [InlineData("simp-chinese-informal", 1000000, "一百万")]
        [InlineData("simp-chinese-informal", 1010000, "一百零一万")]
        [InlineData("simp-chinese-informal", 10000000, "一千万")]
        [InlineData("simp-chinese-informal", 100000000, "一亿")]
        [InlineData("simp-chinese-informal", 100000001, "一亿零一")]
        [InlineData("simp-chinese-informal", 123456789, "一亿二千三百四十五万六千七百八十九")]
        [InlineData("simp-chinese-informal", int.MaxValue, "二十一亿四千七百四十八万三千六百四十七")]
        [InlineData("simp-chinese-informal", -12345, "负一万二千三百四十五")]
        [InlineData("simp-chinese-informal", -5, "负五")]
        // Formal: every unit keeps its digit, with the formal digits and units.
        [InlineData("simp-chinese-formal", 0, "零")]
        [InlineData("simp-chinese-formal", 10, "壹拾")]
        [InlineData("simp-chinese-formal", 11, "壹拾壹")]
        [InlineData("simp-chinese-formal", 20, "贰拾")]
        [InlineData("simp-chinese-formal", 101, "壹佰零壹")]
        [InlineData("simp-chinese-formal", 1010, "壹仟零壹拾")]
        [InlineData("simp-chinese-formal", 1234, "壹仟贰佰叁拾肆")]
        [InlineData("simp-chinese-formal", 2020, "贰仟零贰拾")]
        [InlineData("simp-chinese-formal", 9999, "玖仟玖佰玖拾玖")]
        [InlineData("simp-chinese-formal", 10000, "壹万")]
        [InlineData("simp-chinese-formal", 10010, "壹万零壹拾")]
        [InlineData("simp-chinese-formal", 100000, "壹拾万")]
        [InlineData("simp-chinese-formal", -5, "负伍")]
        // Traditional Chinese: its own digits, myriad units and negative sign; cjk-ideographic is trad-chinese-informal.
        [InlineData("trad-chinese-informal", 1234, "一千二百三十四")]
        [InlineData("trad-chinese-informal", 10000, "一萬")]
        [InlineData("trad-chinese-informal", 100000000, "一億")]
        [InlineData("trad-chinese-informal", -5, "負五")]
        [InlineData("trad-chinese-formal", 1234, "壹仟貳佰參拾肆")]
        [InlineData("trad-chinese-formal", 10000, "壹萬")]
        [InlineData("trad-chinese-formal", -5, "負伍")]
        [InlineData("cjk-ideographic", 0, "零")]
        [InlineData("cjk-ideographic", 11, "十一")]
        [InlineData("cjk-ideographic", 101, "一百零一")]
        [InlineData("cjk-ideographic", 1010, "一千零一十")]
        [InlineData("cjk-ideographic", 10000, "一萬")]
        [InlineData("cjk-ideographic", -10000, "負一萬")]
        // Japanese: purely additive (no zero digit), 一 left out before a unit in the informal style, stops at 9999.
        [InlineData("japanese-informal", 0, "〇")]
        [InlineData("japanese-informal", 1, "一")]
        [InlineData("japanese-informal", 10, "十")]
        [InlineData("japanese-informal", 11, "十一")]
        [InlineData("japanese-informal", 20, "二十")]
        [InlineData("japanese-informal", 101, "百一")]
        [InlineData("japanese-informal", 110, "百十")]
        [InlineData("japanese-informal", 1000, "千")]
        [InlineData("japanese-informal", 1010, "千十")]
        [InlineData("japanese-informal", 9999, "九千九百九十九")]
        [InlineData("japanese-informal", -5, "マイナス五")]
        [InlineData("japanese-formal", 0, "零")]
        [InlineData("japanese-formal", 1, "壱")]
        [InlineData("japanese-formal", 10, "壱拾")]
        [InlineData("japanese-formal", 11, "壱拾壱")]
        [InlineData("japanese-formal", 20, "弐拾")]
        [InlineData("japanese-formal", 101, "壱百壱")]
        [InlineData("japanese-formal", 1000, "壱阡")]
        [InlineData("japanese-formal", 1010, "壱阡壱拾")]
        [InlineData("japanese-formal", 9999, "九阡九百九拾九")]
        [InlineData("japanese-formal", -5, "マイナス伍")]
        // Korean: additive with no zero digit, plus a 만/萬 group unit past 9999.
        [InlineData("korean-hangul-formal", 0, "영")]
        [InlineData("korean-hangul-formal", 1, "일")]
        [InlineData("korean-hangul-formal", 10, "일십")]
        [InlineData("korean-hangul-formal", 11, "일십일")]
        [InlineData("korean-hangul-formal", 20, "이십")]
        [InlineData("korean-hangul-formal", 101, "일백일")]
        [InlineData("korean-hangul-formal", 1000, "일천")]
        [InlineData("korean-hangul-formal", 1010, "일천일십")]
        [InlineData("korean-hangul-formal", 9999, "구천구백구십구")]
        [InlineData("korean-hangul-formal", 10000, "일만")]
        [InlineData("korean-hangul-formal", -5, "마이너스 오")]
        [InlineData("korean-hangul-formal", -10000, "마이너스 일만")]
        [InlineData("korean-hanja-informal", 0, "零")]
        [InlineData("korean-hanja-informal", 1, "一")]
        [InlineData("korean-hanja-informal", 110, "百十")]
        [InlineData("korean-hanja-informal", 1010, "千十")]
        [InlineData("korean-hanja-informal", 9999, "九千九百九十九")]
        [InlineData("korean-hanja-informal", 10000, "萬")]
        [InlineData("korean-hanja-informal", 20000, "二萬")]
        [InlineData("korean-hanja-informal", 11000, "萬千")]
        [InlineData("korean-hanja-informal", -10000, "마이너스 萬")]
        [InlineData("korean-hanja-formal", 1, "壹")]
        [InlineData("korean-hanja-formal", 10, "壹拾")]
        [InlineData("korean-hanja-formal", 101, "壹百壹")]
        [InlineData("korean-hanja-formal", 1010, "壹仟壹拾")]
        [InlineData("korean-hanja-formal", 9999, "九仟九百九拾九")]
        [InlineData("korean-hanja-formal", 10000, "壹萬")]
        [InlineData("korean-hanja-formal", 100000000, "壹億")]
        public void Format_MatchesTheReferenceNumerals(string style, int number, string expected)
        {
            Assert.Equal(expected, EastAsianCounterStyles.Format(number, style));
        }

        [Theory]
        [InlineData("japanese-informal", 10000)]
        [InlineData("japanese-formal", -10000)]
        public void TheJapaneseStyles_StopAt9999(string style, int number)
        {
            Assert.Null(EastAsianCounterStyles.Format(number, style));
        }

        [Theory]
        [InlineData("japanese-formal", 10000, "一〇〇〇〇")]
        [InlineData("japanese-informal", -10000, "-一〇〇〇〇")]
        [InlineData("simp-chinese-informal", 12, "十二")]
        public void TheCounterEngine_UsesCjkDecimalOutsideAStylesRange(string style, int number, string expected)
        {
            Assert.Equal(expected, CssCounterEngine.FormatCounterValue(number, style));
        }

        [Fact]
        public void AnotherStyle_IsNotHandled()
        {
            Assert.False(EastAsianCounterStyles.IsEastAsianCounterStyle("decimal"));
            Assert.Null(EastAsianCounterStyles.Format(5, "decimal"));
            Assert.Null(EastAsianCounterStyles.SuffixOf("decimal"));
        }

        [Theory]
        [InlineData("simp-chinese-informal", "、")]
        [InlineData("cjk-ideographic", "、")]
        [InlineData("japanese-formal", "、")]
        [InlineData("korean-hangul-formal", ", ")]
        [InlineData("KOREAN-HANJA-FORMAL", ", ")]
        public void TheMarkerSuffix_IsThePerStyleOne(string style, string suffix)
        {
            Assert.Equal(suffix, EastAsianCounterStyles.SuffixOf(style));
        }
    }
}

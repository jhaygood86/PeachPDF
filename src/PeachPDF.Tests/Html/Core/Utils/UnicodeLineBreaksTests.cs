using PeachDrawing.Text.Unicode;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Tests.Html.Core.Utils
{
    /// <summary>
    /// What <see cref="UnicodeLineBreaks"/> adds to the line breaking algorithm: the answer in the coordinates of the text it is given,
    /// with a soft hyphen hidden, the flag an earlier box left open, and the interoperability exceptions.
    /// </summary>
    public class UnicodeLineBreaksTests
    {
        private static LineBreakOpportunity[] Find(string text, int precedingRegionalIndicators = 0, PeachPDF.CSS.WordBreak wordBreak = PeachPDF.CSS.WordBreak.Normal,
            PeachPDF.CSS.LineBreak lineBreak = PeachPDF.CSS.LineBreak.Auto) =>
            UnicodeLineBreaks.Find(text, wordBreak, precedingRegionalIndicators, lineBreak);

        [Theory]
        [InlineData("Auto", 0)]
        [InlineData("Loose", 1)]
        [InlineData("Normal", 0)]
        [InlineData("Strict", 0)]
        public void ASmallKana_MayStartALine_OnlyWhenTheStrictnessAllowsIt(string lineBreak, int expected)
        {
            // A small kana (U+3083) after a full-size one: only loose lets a small kana start a line; auto, normal and strict keep it off.
            var strictness = System.Enum.Parse<PeachPDF.CSS.LineBreak>(lineBreak);
            Assert.Equal((LineBreakOpportunity)expected, Find("\u3042\u3083", lineBreak: strictness)[1]);
        }

        [Fact]
        public void LineBreakAnywhere_AllowsABreakAfterEveryCharacterOfAWord()
        {
            var opportunities = Find("abcd", lineBreak: PeachPDF.CSS.LineBreak.Anywhere);

            Assert.All(new[] { 1, 2, 3 }, i => Assert.Equal(LineBreakOpportunity.Allowed, opportunities[i]));
            Assert.Equal(LineBreakOpportunity.Prohibited, Find("abcd")[2]);
        }

        [Fact]
        public void TheAnswerHasOneEntryForEveryIndexAndOneForTheEnd()
        {
            Assert.Equal("abc def".Length + 1, Find("abc def").Length);
            Assert.Equal([LineBreakOpportunity.Mandatory], Find(""));
        }

        [Fact]
        public void ASoftHyphen_IsNotAnOpportunityOfItsOwn()
        {
            var opportunities = Find("hyphen\u00ADation");

            Assert.All(opportunities.Take(opportunities.Length - 1), o => Assert.NotEqual(LineBreakOpportunity.Allowed, o));
            Assert.Equal("hyphen\u00ADation".Length + 1, opportunities.Length);
        }

        [Fact]
        public void AnOddNumberOfPrecedingRegionalIndicators_MakesTheFirstOneHere_CompleteAFlag()
        {
            const string two = "\U0001F1FA\U0001F1F8";     // a whole flag: two indicators

            // Even before: the pair here is a flag of its own, so no break falls between its two indicators.
            Assert.Equal(LineBreakOpportunity.Prohibited, Find(two, precedingRegionalIndicators: 0)[2]);

            // Odd before: the first indicator here completes the flag left open, so a break may follow it and the answer is in this text's own coordinates.
            var completing = Find(two, precedingRegionalIndicators: 1);
            Assert.Equal(two.Length + 1, completing.Length);
            Assert.Equal(LineBreakOpportunity.Allowed, completing[2]);
        }

        [Theory]
        [InlineData("and/or", 4)]
        [InlineData("!important", 1)]
        [InlineData("a|b", 2)]
        public void ASolidusExclamationMarkOrBarBeforeALatinLetter_DoesNotBreak(string text, int index)
        {
            Assert.Equal(LineBreakOpportunity.Prohibited, Find(text)[index]);
        }

        [Fact]
        public void TheSolidusException_DoesNotReachIdeographs()
        {
            Assert.NotEqual(LineBreakOpportunity.Prohibited, Find("\u4F60/\u597D")[2]);
        }

        [Fact]
        public void KeepAllAndBreakAll_ArePassedThrough()
        {
            Assert.Equal(LineBreakOpportunity.Allowed, Find("ab", wordBreak: PeachPDF.CSS.WordBreak.BreakAll)[1]);
            Assert.Equal(LineBreakOpportunity.Prohibited, Find("\u4F60\u597D", wordBreak: PeachPDF.CSS.WordBreak.KeepAll)[1]);
        }
    }
}

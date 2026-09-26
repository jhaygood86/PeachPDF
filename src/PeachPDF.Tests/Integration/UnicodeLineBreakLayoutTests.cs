using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Where the layout lets a line end inside a text box, which comes from the Unicode line breaking algorithm (UAX #14) with CSS
    /// <c>word-break</c> applied. The rules themselves are checked against Unicode's conformance file in
    /// <c>PeachDrawing.Text.Tests</c>; these tests are about what the layout does with the answer.
    /// </summary>
    public class UnicodeLineBreakLayoutTests
    {
        private static async Task<CssBox> LayOut(string style, string text, bool embedCjk = false)
        {
            var font = embedCjk ? BundledFonts.FontFaceRule(BundledFonts.Cjk, "CJKTest", "font/truetype") : "";
            var html = LayoutHarness.Wrap($"<style>{font}</style><p id='p' style='{style}'>{text}</p>");
            var (root, _) = await LayoutHarness.LayoutAsync(html);
            return LayoutHarness.FindById(root, "p")!;
        }

        private static string[] WordTexts(CssBox box) =>
            LayoutHarness.Descendants(box).SelectMany(b => b.Words).Select(w => w.Text).Where(t => !string.IsNullOrEmpty(t)).Select(t => t!).ToArray();

        private static int Lines(CssBox box) =>
            box.LineBoxes.Count(line => line.Words.Any(word => !string.IsNullOrEmpty(word.Text)));

        private const string Kana = "テキスト";       // te ki su to

        [Fact]
        public async Task Kana_BreaksBetweenCharacters_AndWrapsAtNarrowWidths()
        {
            var box = await LayOut("font-family:CJKTest; font-size:16pt; width:40pt", Kana + Kana, embedCjk: true);

            Assert.Equal(8, WordTexts(box).Length);
            Assert.True(Lines(box) > 1);
        }

        [Fact]
        public async Task WordBreak_KeepAll_KeepsKanaTogether()
        {
            var box = await LayOut("font-family:CJKTest; font-size:16pt; width:40pt; word-break:keep-all", Kana + Kana, embedCjk: true);

            Assert.Equal([Kana + Kana], WordTexts(box));
            Assert.Equal(1, Lines(box));
        }

        [Fact]
        public async Task WordBreak_KeepAll_StillBreaksAtSpaces()
        {
            var box = await LayOut("font-family:CJKTest; font-size:16pt; width:60pt; word-break:keep-all", Kana + " " + Kana, embedCjk: true);

            Assert.Equal([Kana, Kana], WordTexts(box));
            Assert.Equal(2, Lines(box));
        }

        [Fact]
        public async Task Hyphen_AllowsABreakAfterItBetweenLetters_ButNotBeforeADigit()
        {
            Assert.Equal(["well-", "known"], WordTexts(await LayOut("width:400px", "well-known")));
            Assert.Equal(["abc-123"], WordTexts(await LayOut("width:400px", "abc-123")));
        }

        [Fact]
        public async Task Solidus_DoesNotSplitBeforeALetter()
        {
            Assert.Equal(["23/Jan/Feb"], WordTexts(await LayOut("width:400px", "23/Jan/Feb")));
            Assert.Equal(["and/or"], WordTexts(await LayOut("width:400px", "and/or")));
        }

        [Fact]
        public async Task ExclamationMarkBeforeALetter_DoesNotSplit()
        {
            Assert.Equal(["!important"], WordTexts(await LayOut("width:400px", "!important")));
        }

        [Fact]
        public async Task SoftHyphen_IsNotABreakOpportunityOfItsOwn()
        {
            var box = await LayOut("width:400px", "hyphen&shy;ation");
            var words = box.Words.Count > 0 ? box.Words : LayoutHarness.Descendants(box).SelectMany(b => b.Words).ToList();

            Assert.Single(words, w => !string.IsNullOrEmpty(w.Text));
        }

        [Fact]
        public async Task OpeningPunctuationFollowedByASpace_KeepsTheNextWordOnItsLine()
        {
            var box = await LayOut("width:400px", "say ( word");
            var words = LayoutHarness.Descendants(box).SelectMany(b => b.Words).Where(w => !string.IsNullOrEmpty(w.Text)).ToList();

            Assert.Equal(["say", "(", "word"], words.Select(w => w.Text));
            Assert.True(words[1].UnicodeBreakBefore);        // a line may end after "say"
            Assert.False(words[2].UnicodeBreakBefore);       // but not between "(" and "word", even across the space
        }

        [Fact]
        public async Task WordBreak_BreakAll_StillBreaksBetweenEveryLetter()
        {
            var box = await LayOut("width:400px; word-break:break-all", "abc");

            Assert.Equal(["a", "b", "c"], WordTexts(box));
        }
    }
}

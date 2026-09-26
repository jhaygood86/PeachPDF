using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Thai and Khmer wrap at the words a dictionary finds, as they do in a browser. The breaks themselves are checked in
    /// <c>PeachDrawing.Text.Tests</c>; these tests are about how the layout uses them: where the words land, and what
    /// <c>word-break</c>, <c>overflow-wrap</c> and inline boxes do on top. Every character is set in
    /// <see cref="BundledFonts.LineBreak"/>, one em wide (a mark or a vowel sign as much as a letter), so with 16pt text a line 50pt wide
    /// holds three characters and one 100pt wide holds six.
    /// </summary>
    public class DictionaryLineBreakLayoutTests
    {
        private static string Page(string body) =>
            "<!DOCTYPE html><html><head><style>"
            + BundledFonts.FontFaceRule(BundledFonts.LineBreak, "LineBreakTest", "font/truetype") + "</style></head><body style='margin:0'>" + body + "</body></html>";

        private static async Task<CssBox> LayOut(string body)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(Page(body));
            return LayoutHarness.FindById(root, "p")!;
        }

        private static string Style(int width) => $"font-family:LineBreakTest; font-size:16pt; width:{width}pt; ";

        /// <summary>The text of each line, top to bottom.</summary>
        private static string[] LineTexts(CssBox box) =>
            box.LineBoxes
                .Select(line => string.Concat(line.Words.Select(word => word.Text)))
                .Where(text => text.Length > 0)
                .ToArray();

        [Fact]
        public async Task Thai_WrapsBetweenWords()
        {
            var narrow = await LayOut($"<p id='p' style='{Style(50)}'>ฉันรักภาษาไทย</p>");
            Assert.Equal(["ฉัน", "รัก", "ภาษา", "ไทย"], LineTexts(narrow));

            var wide = await LayOut($"<p id='p' style='{Style(100)}'>ฉันรักภาษาไทย</p>");
            Assert.Equal(["ฉันรัก", "ภาษา", "ไทย"], LineTexts(wide));
        }

        [Fact]
        public async Task Khmer_WrapsBetweenWords()
        {
            Assert.Equal(["ខ្ញុំ", "ស្រលាញ់", "ភាសាខ្មែរ"], LineTexts(await LayOut($"<p id='p' style='{Style(50)}'>ខ្ញុំស្រលាញ់ភាសាខ្មែរ</p>")));
        }

        [Fact]
        public async Task LaoAndBurmese_HaveNoWordListYet_AndOverflowAsOneWord()
        {
            Assert.Equal(["ຂ້ອຍຮັກພາສາລາວ"], LineTexts(await LayOut($"<p id='p' style='{Style(50)}'>ຂ້ອຍຮັກພາສາລາວ</p>")));
            Assert.Equal(["ကျွန်တော်မြန်မာစာကိုချစ်တယ်"], LineTexts(await LayOut($"<p id='p' style='{Style(50)}'>ကျွန်တော်မြန်မာစာကိုချစ်တယ်</p>")));
        }

        [Fact]
        public async Task TheWordsLandWhereTheirWidthsPutThem()
        {
            var box = await LayOut($"<p id='p' style='{Style(100)}'>ฉันรักภาษาไทย</p>");
            var words = LayoutHarness.Descendants(box).SelectMany(b => b.Words).Where(w => !string.IsNullOrEmpty(w.Text)).ToList();
            var em = words[0].Width / 3;

            Assert.Equal(["ฉัน", "รัก", "ภาษา", "ไทย"], words.Select(w => w.Text!).ToArray());
            Assert.Equal(3, box.LineBoxes.Count);

            // Two words on the first line, side by side; the next two each start a line, under the first.
            Assert.Equal(words[0].Left, words[2].Left, 3);
            Assert.Equal(words[0].Left, words[3].Left, 3);
            Assert.Equal(words[0].Left + 3 * em, words[1].Left, 3);
            Assert.Equal(words[0].Top, words[1].Top, 3);
            Assert.True(words[2].Top > words[1].Top);
            Assert.True(words[3].Top > words[2].Top);
            Assert.Equal(4 * em, words[2].Width, 3);
        }

        [Fact]
        public async Task TheLanguageOfTheText_IsNotWhatDecidesIt()
        {
            Assert.Equal(["ฉัน", "รัก", "ภาษา", "ไทย"], LineTexts(await LayOut($"<p id='p' lang='en' style='{Style(50)}'>ฉันรักภาษาไทย</p>")));
            Assert.Equal(["ฉัน", "รัก", "ภาษา", "ไทย"], LineTexts(await LayOut($"<p id='p' lang='th' style='{Style(50)}'>ฉันรักภาษาไทย</p>")));
        }

        [Theory]
        [InlineData("word-break:keep-all")]
        [InlineData("line-break:strict")]
        [InlineData("line-break:loose")]
        public async Task TheTailoringsOfTheProperties_LeaveTheWordsAlone(string css)
        {
            Assert.Equal(["ฉัน", "รัก", "ภาษา", "ไทย"], LineTexts(await LayOut($"<p id='p' style='{Style(50)}{css}'>ฉันรักภาษาไทย</p>")));
        }

        [Fact]
        public async Task BreakAll_BreaksBetweenClusters_AndAnywhere_BetweenGraphemes()
        {
            // "ผู้ใช้" is three clusters, which break-all separates and no wider line can join.
            Assert.Equal(["ผู้", "ใ", "ช้"], LineTexts(await LayOut($"<p id='p' style='{Style(16)}word-break:break-all'>ผู้ใช้</p>")));
            Assert.Equal(["ผู้", "ใ", "ช้"], LineTexts(await LayOut($"<p id='p' style='{Style(16)}line-break:anywhere'>ผู้ใช้</p>")));
        }

        [Fact]
        public async Task AWordTooLongForALine_IsBrokenByOverflowWrapAnywhere_NotByTheDictionary()
        {
            // "ภาษา" is one word of four characters on a three-character line: it stays whole unless overflow-wrap says otherwise.
            Assert.Equal(["ภาษา"], LineTexts(await LayOut($"<p id='p' style='{Style(50)}'>ภาษา</p>")));
            Assert.Equal(["ภาษ", "า"], LineTexts(await LayOut($"<p id='p' style='{Style(50)}overflow-wrap:anywhere'>ภาษา</p>")));
        }

        [Fact]
        public async Task AnInlineBox_BreaksAtAWordBoundary_AndNotInsideAWord()
        {
            // The seam is between two words: a line may end there.
            Assert.Equal(["ฉัน", "รัก"], LineTexts(await LayOut($"<p id='p' style='{Style(50)}'>ฉัน<b>รัก</b></p>")));

            // The seam is inside "ภาษา": it is not a place to break, and the word overflows the line whole.
            Assert.Equal(["ภาษา"], LineTexts(await LayOut($"<p id='p' style='{Style(50)}'>ภา<b>ษา</b></p>")));
        }

        [Fact]
        public async Task TextWithSpaces_KeepsBreakingAtThem_AndTheDictionaryWorksBetweenThem()
        {
            var lines = LineTexts(await LayOut($"<p id='p' style='{Style(50)}'>ฉัน รักภาษา hello</p>")).Select(t => t.Trim()).ToArray();

            Assert.Equal(["ฉัน", "รัก", "ภาษา", "hello"], lines);
        }
    }
}

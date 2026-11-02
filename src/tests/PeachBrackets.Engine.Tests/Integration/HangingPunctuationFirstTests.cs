using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// <c>hanging-punctuation: first</c> (CSS Text 3 section 8.1): an opening bracket or quote at the start of a
    /// block's first line hangs outside the start edge and is not measured, so the text that follows it
    /// starts at the content edge and the line gains the quote's width.
    /// </summary>
    public class HangingPunctuationFirstTests
    {
        private static async Task<(CssBox Box, CssRect First)> Layout(string style, string html)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='width:200pt;font-size:10pt;font-family:monospace;margin:0;padding:0;{style}'>{html}</div>"));
            var box = LayoutHarness.FindById(root, "d")!;
            var word = FirstWord(box)!;
            return (box, word);
        }

        private static CssRect? FirstWord(CssBox box)
        {
            foreach (var word in box.Words) return word;
            foreach (var child in box.Boxes)
            {
                if (FirstWord(child) is { } found) return found;
            }

            return null;
        }

        [Theory]
        [InlineData("none", true)]
        [InlineData("first", true)]
        [InlineData("last", true)]
        [InlineData("first last", true)]
        [InlineData("last first allow-end", true)]
        [InlineData("force-end last", true)]
        [InlineData("allow-end force-end", false)]
        [InlineData("first first", false)]
        [InlineData("none first", false)]
        [InlineData("first, last", false)]
        [InlineData("bogus", false)]
        [InlineData("1px", false)]
        public void Grammar_AcceptsOnlyTheSpecValueSpace(string value, bool valid)
        {
            Assert.Equal(valid, HangingPunctuationGrammar.TryParse(value, out _));
        }

        [Fact]
        public void Grammar_ReportsEachComponent()
        {
            Assert.True(HangingPunctuationGrammar.TryParse("force-end first", out var flags));
            Assert.Equal(HangingPunctuationFlags.ForceEnd | HangingPunctuationFlags.First, flags);
        }

        [Fact]
        public async Task TheDeclarationIsKept_AndInherited()
        {
            var (_, word) = await Layout("hanging-punctuation:first last", "<span id='s'>x</span>");
            Assert.Equal("first last", word.OwnerBox.HangingPunctuation);
        }

        [Theory]
        [InlineData("\"quoted\"")]
        [InlineData("(bracket)")]
        [InlineData("“quoted”")]
        public async Task AnOpeningCharacter_HangsOutsideTheStartEdge(string text)
        {
            var (box, plain) = await Layout("", text + " and more words that wrap onto later lines of this block of text");
            var (_, hung) = await Layout("hanging-punctuation:first", text + " and more words that wrap onto later lines of this block of text");

            // Monospace at 10pt: the character is 6pt wide. The word starts that far left of the edge.
            Assert.Equal(plain.Left, box.ClientLeft, 0.01);
            Assert.True(hung.Left < plain.Left - 3, $"hung {hung.Left}, plain {plain.Left}");
        }

        [Fact]
        public async Task TheHungCharacterIsNotMeasured_TheFollowingTextStartsAtTheEdge()
        {
            var (box, hung) = await Layout("hanging-punctuation:first", "\"hello\"");
            var (_, withQuote) = await Layout("", "\"hello\"");
            var (_, withoutQuote) = await Layout("", "hello\"");

            // The word is the quote plus "hello": it starts one quote-advance left of the edge, so the
            // text after the quote begins exactly at the content edge.
            var quoteAdvance = withQuote.Width - withoutQuote.Width;
            Assert.True(quoteAdvance > 3);
            Assert.Equal(box.ClientLeft - quoteAdvance, hung.Left, 0.01);
            Assert.Equal(withQuote.Width, hung.Width, 0.01);
        }

        [Fact]
        public async Task OnlyTheFirstLine_Hangs()
        {
            var (box, _) = await Layout("hanging-punctuation:first", "\"" + string.Join(' ', Enumerable.Repeat("word", 40)));
            Assert.True(box.LineBoxes.Count > 1);
            var second = box.LineBoxes[1].Words[0];
            Assert.Equal(box.ClientLeft, second.Left, 0.01);
        }

        [Fact]
        public async Task AFirstLineThatHangs_GainsTheHungWidth()
        {
            // Size the block from a measured layout: the two words need `natural` points, and the block is
            // half a quote-advance narrower, so only hanging the (unmeasured) quote lets the second fit.
            const string text = "\"aaaa bbbbbbbbbb cccc";
            var (wide, _) = await Layout("width:400pt", text);
            var words = wide.LineBoxes[0].Words.Where(w => !w.IsSpaces).ToList();
            var natural = words[1].Left + words[1].Width - words[0].Left;
            var (_, withQuote) = await Layout("", "\"a");
            var (_, withoutQuote) = await Layout("", "a");
            var quoteAdvance = withQuote.Width - withoutQuote.Width;

            var width = (natural - quoteAdvance / 2).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            var (plainBox, _) = await Layout($"width:{width}pt", text);
            var (hungBox, _) = await Layout($"width:{width}pt;hanging-punctuation:first", text);
            Assert.Equal(2, hungBox.LineBoxes[0].Words.Count(w => !w.IsSpaces));
            Assert.Equal(1, plainBox.LineBoxes[0].Words.Count(w => !w.IsSpaces));
        }

        [Fact]
        public async Task ACharacterThatIsNotOpeningPunctuation_DoesNotHang()
        {
            var (box, word) = await Layout("hanging-punctuation:first", "hello world");
            Assert.Equal(box.ClientLeft, word.Left, 0.01);
        }

        [Fact]
        public async Task AClosingBracketAtTheStart_DoesNotHang_UnderFirst()
        {
            var (box, word) = await Layout("hanging-punctuation:first", ") hello");
            Assert.Equal(box.ClientLeft, word.Left, 0.01);
        }

        [Fact]
        public async Task AnInlineWithStartPadding_BlocksHanging()
        {
            var (box, word) = await Layout("hanging-punctuation:first",
                "<span style='padding-left:4pt'>\"hello\"</span>");
            Assert.True(word.Left >= box.ClientLeft - 0.01, $"word {word.Left}, edge {box.ClientLeft}");
        }

        [Fact]
        public async Task AnOpeningCharacterInsideALeadingInline_Hangs()
        {
            var (box, word) = await Layout("hanging-punctuation:first", "<em>\"hello\"</em> world");
            Assert.True(word.Left < box.ClientLeft - 3, $"word {word.Left}, edge {box.ClientLeft}");
        }

        [Fact]
        public async Task RightToLeft_HangsOutsideTheRightEdge()
        {
            var (box, word) = await Layout("direction:rtl;hanging-punctuation:first", "\"hello\"");
            Assert.True(word.Left + word.Width > box.ClientRight + 3, $"right {word.Left + word.Width}, edge {box.ClientRight}");
        }

        [Fact]
        public async Task WithoutTheProperty_NothingHangs()
        {
            var (box, word) = await Layout("", "\"hello\"");
            Assert.Equal(box.ClientLeft, word.Left, 0.01);
        }

        [Fact]
        public async Task LastAlone_DoesNotShiftTheFirstLine()
        {
            var (box, word) = await Layout("hanging-punctuation:last", "\"hello\"");
            Assert.Equal(box.ClientLeft, word.Left, 0.01);
        }
    }
}

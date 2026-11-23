using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// Which characters collapse as white space (css-text-3 section 4.1.1): the space, tab, segment break and ogham space mark do; the
    /// other Unicode space separators, the ideographic space among them, are ordinary characters that keep their width.
    /// </summary>
    public class CollapsibleWhitespaceTests
    {
        [Theory]
        [InlineData(' ', true)]
        [InlineData('\t', true)]
        [InlineData('\n', true)]
        [InlineData('\r', true)]
        [InlineData(' ', true)]
        [InlineData(' ', false)]
        [InlineData('　', false)]
        [InlineData(' ', false)]
        [InlineData(' ', false)]
        [InlineData('x', false)]
        public void IsCollapsibleWhitespace_FollowsTheCssDefinition(char c, bool expected) =>
            Assert.Equal(expected, HtmlUtils.IsCollapsibleWhitespace(c));

        [Fact]
        public async Task IdeographicSpace_IsNotCollapsedWithTheSpacesAroundIt()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap("<p id='p'>a　　b</p><p id='q'>a  b</p>"));
            var kept = string.Concat(LayoutHarness.Descendants(LayoutHarness.FindById(root, "p")!).SelectMany(b => b.Words).Select(w => w.Text));
            var collapsed = string.Concat(LayoutHarness.Descendants(LayoutHarness.FindById(root, "q")!).SelectMany(b => b.Words).Select(w => w.Text));

            Assert.Equal("a　　b", kept);
            Assert.DoesNotContain("  ", collapsed);
        }

        private static string Page(string body) =>
            "<!DOCTYPE html><html><head><style>" + BundledFonts.FontFaceRule(BundledFonts.LineBreak, "LineBreakTest", "font/truetype")
            + "</style></head><body style='margin:0'>" + body + "</body></html>";

        private static async Task<string[]> Lines(string body)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(Page(body));
            var box = LayoutHarness.FindById(root, "p")!;
            return [.. box.LineBoxes.Select(l => string.Concat(l.Words.Select(w => w.Text))).Where(t => t.Length > 0)];
        }

        /// <summary>css-text-3 section 4.1.3: an ideographic space at the end of a line hangs, so it stays on the line it overflows instead of wrapping.</summary>
        [Fact]
        public async Task TrailingIdeographicSpace_HangsInsteadOfWrapping()
        {
            // Three ems of 16pt fit the 50pt line; the space after them overflows it, and hangs.
            var lines = await Lines("<p id='p' style='font-family:LineBreakTest; font-size:16pt; width:50pt'>あいう\u3000え</p>");
            Assert.Equal(["あいう\u3000", "え"], lines);
        }

        [Theory]
        [InlineData('\u2003')]
        [InlineData('\u205F')]
        public async Task TrailingSpaceSeparator_HangsLikeTheIdeographicSpace(char space)
        {
            var lines = await Lines($"<p id='p' style='font-family:LineBreakTest; font-size:16pt; width:50pt'>あいう{space}え</p>");
            Assert.NotEqual(["あい", $"う{space}え"], lines);
        }

        [Fact]
        public async Task TrailingIdeographicSpace_DoesNotHang_UnderBreakSpaces()
        {
            var lines = await Lines("<p id='p' style='font-family:LineBreakTest; font-size:16pt; width:50pt; white-space:break-spaces'>あいう\u3000え</p>");
            Assert.Equal(["あい", "う\u3000え"], lines);
        }

        /// <summary>The hanging space that ends a word before a break does not count toward <c>min-content</c>, which takes every break.</summary>
        [Fact]
        public async Task MinContent_DoesNotCountTheSpaceSeparatorThatHangsAtEachBreak()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(Page(
                "<div id='p' style='position:absolute; width:min-content; font-family:LineBreakTest; font-size:16pt'>あ\u3000あ</div>"));
            var box = LayoutHarness.FindById(root, "p")!;

            Assert.Equal(16d, box.ActualRight - box.Location.X, 1);       // one em, not two
        }

        /// <summary>
        /// <c>white-space: break-spaces</c>: a line may end only after a preserved space, so the space after a word is not a place to break
        /// and does not hang: the word and its space move to the next line together, rather than the space overflowing the line.
        /// </summary>
        [Fact]
        public async Task BreakSpaces_MovesAWordAndItsSpaceToTheNextLineTogether()
        {
            const string Style = "font-size:16pt; white-space:break-spaces; ";

            // Measure the words and the space on a line wide enough for everything.
            var (wideRoot, _) = await LayoutHarness.LayoutAsync(Page($"<p id='p' style='{Style}'>x yy x</p>"));
            var words = LayoutHarness.Descendants(LayoutHarness.FindById(wideRoot, "p")!).SelectMany(b => b.Words).ToList();
            double x = words.First(w => w.Text == "x").Width, yy = words.First(w => w.Text == "yy").Width, space = words.First(w => w.Text == " ").Width;

            // Room for "x yy" but not for the space after it: that space belongs to "yy ", which does not fit after "x ".
            var lines = await Lines($"<p id='p' style='{Style}width:{x + space + yy + space / 2}pt'>x yy x</p>");

            Assert.Equal(["x ", "yy x"], lines);
        }

        /// <summary>
        /// Preserved white space at a soft wrap hangs (css-text-3 section 4.1.3), so it is not part of the content a line aligns: with
        /// <c>text-align: right</c> the word ends at the edge. Before a forced break, or at the end of the block, it hangs only when it
        /// would overflow, so it still counts.
        /// </summary>
        [Fact]
        public async Task PreWrap_TrailingSpaceAtASoftWrap_DoesNotCountForAlignment()
        {
            const string Style = "font-size:16pt; white-space:pre-wrap; text-align:right; ";
            var (wideRoot, _) = await LayoutHarness.LayoutAsync(Page($"<p id='p' style='{Style}'>aaaa bbbb</p>"));
            var words = LayoutHarness.Descendants(LayoutHarness.FindById(wideRoot, "p")!).SelectMany(b => b.Words).ToList();
            double a = words.First(w => w.Text == "aaaa").Width, b = words.First(w => w.Text == "bbbb").Width, space = words.First(w => w.Text == " ").Width;

            // "aaaa " fits, "bbbb" does not fit after it.
            var width = a + space + b / 2;
            var (root, _) = await LayoutHarness.LayoutAsync(Page($"<p id='p' style='{Style}width:{width}pt'>aaaa bbbb</p>"));
            var wrapped = LayoutHarness.Descendants(LayoutHarness.FindById(root, "p")!).SelectMany(x => x.Words).ToList();
            var first = wrapped.First(w => w.Text == "aaaa");
            var last = wrapped.First(w => w.Text == "bbbb");

            Assert.True(last.Top > first.Top, "the second word wrapped");
            var edge = LayoutHarness.FindById(root, "p")!.ActualRight;
            Assert.Equal(edge, first.Right, 1);                           // flush right: the space after it hangs

            // The same text as the last line of the block: its trailing space fits and counts, so the word is a space short of the edge.
            var (single, _) = await LayoutHarness.LayoutAsync(Page($"<p id='p' style='{Style}width:{width}pt'>aaaa </p>"));
            var only = LayoutHarness.Descendants(LayoutHarness.FindById(single, "p")!).SelectMany(x => x.Words).First(w => w.Text == "aaaa");
            Assert.Equal(LayoutHarness.FindById(single, "p")!.ActualRight - space, only.Right, 1);
        }

        /// <summary>The letter-spacing goes between characters, so the one after the last character of a line is not applied: right-aligned text ends flush.</summary>
        [Fact]
        public async Task LetterSpacing_IsNotAppliedAfterTheLastCharacterOfALine()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(Page("<p id='p' style='font-size:16pt; letter-spacing:10pt; text-align:right; width:200pt'>abc</p>"));
            var box = LayoutHarness.FindById(root, "p")!;
            var word = LayoutHarness.Descendants(box).SelectMany(b => b.Words).Single(w => w.Text == "abc");

            // The word's box still carries the spacing after its last letter, which hangs past the edge.
            Assert.Equal(box.ActualRight + 10, word.Right, 1);
            Assert.Equal(10, word.HungEndWidth, 1);
        }
    }
}

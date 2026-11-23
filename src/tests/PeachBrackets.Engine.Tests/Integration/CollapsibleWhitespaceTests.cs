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
    }
}

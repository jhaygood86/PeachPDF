using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for issue #840: <c>CssLayoutEngine.ApplyCenterAlignment</c> and
    /// <c>ApplyJustifyAlignment</c> shared <c>ApplyRightAlignment</c>'s pre-#797-style overflow guard,
    /// which discarded a negative shift and silently left an overflowing line exactly where natural
    /// (always left-to-right-flowing) layout placed it instead of actively centering/justifying it
    /// around the overflow.
    /// </summary>
    /// <remarks>
    /// <c>center</c> still works that way. <c>justify</c> no longer does, and deliberately: issue #1013
    /// read the rule the other way round out of the spec itself -
    /// <see href="https://www.w3.org/TR/css-text-3/#text-align-property">css-text-3 §6.1</see> says an
    /// overflowing line's contents "are start-aligned: any content that doesn't fit overflows the line
    /// box's end edge", and Chromium does exactly that. The difference is real rather than an
    /// inconsistency: centering an overflowing line is a shift with no correct alternative, while
    /// justifying one would have to distribute negative space, which the spec makes optional and no
    /// browser does. The multi-word case below is unaffected either way - #840's floor-at-natural-
    /// spacing walk already reproduced natural placement exactly.
    /// </remarks>
    public class CenterJustifyOverflowAlignmentTests
    {
        [Fact]
        public async Task Center_NowrapLine_WiderThanContainer_SpillsSymmetricallyPastBothEdges()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='d' style='margin:0;width:50pt;white-space:nowrap;text-align:center;font-size:18pt'>" +
                    "abcdefghijklmnopqrstuvwxyz</div>"));

            var d = LayoutHarness.FindById(root, "d")!;
            Assert.Single(d.LineBoxes);
            var word = Assert.Single(d.LineBoxes[0].Words);

            var boxWidth = d.ClientRight - d.ClientLeft;
            Assert.True(word.Width > boxWidth,
                "fixture must actually overflow the container for this test to be meaningful");

            var leftOverhang = d.ClientLeft - word.Left;
            var rightOverhang = word.Right - d.ClientRight;

            Assert.True(leftOverhang > 0 && rightOverhang > 0,
                $"expected the overflowing centered line to spill past both edges (leftOverhang={leftOverhang:F2}, " +
                $"rightOverhang={rightOverhang:F2}) - the pre-fix guard left it flush-left instead");
            Assert.Equal(leftOverhang, rightOverhang, 1);
        }

        [Fact]
        public async Task Justify_SingleUnbreakableWord_OnNonLastLine_IsStartAligned_SpillsPastRightEdge()
        {
            // A lone word is a line with no justification opportunity at all, which css-text-3 §6.4.3
            // calls unexpandable text: it aligns as text-align-last, whose initial `auto` under
            // `text-align: justify` is start. §6.1 says the same thing about the overflow itself - "if
            // the inline contents of a line box are too long to fit within it, then the contents are
            // start-aligned: any content that doesn't fit overflows the line box's end edge".
            //
            // This test used to assert the opposite (flush to the *end* edge, spilling past the start),
            // which is what the pre-#1013 unconditional last-word override produced. Chromium, measured
            // through Playwright on this same fixture, puts the word at x=0 - the line's start edge.
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='d' style='margin:0;width:60pt;text-align:justify;font-size:14pt'>" +
                    "aa bb ccccccccccccccccccccccccccccccccccccccccccccc dd ee</div>"));

            var d = LayoutHarness.FindById(root, "d")!;
            Assert.True(d.LineBoxes.Count >= 2, "fixture must wrap onto multiple lines to reach a non-last line");

            var overflowLine = d.LineBoxes[1];
            var word = Assert.Single(overflowLine.Words);

            Assert.True(word.Width > d.ClientRight - d.ClientLeft,
                "fixture must actually overflow the container for this test to be meaningful");
            Assert.Equal(d.ClientLeft, word.Left, 1);
            Assert.True(word.Right > d.ClientRight,
                $"expected the overflowing justified line to spill past the right edge (word.Right={word.Right:F2} " +
                $"should be > ClientRight={d.ClientRight:F2})");
        }

        [Fact]
        public async Task Justify_MultiWordOverflowingLine_WordsStayInOrder_NoOverlap()
        {
            // A nested `white-space:nowrap` span keeps two words together as a single unbreakable run,
            // which can overflow a non-last justified line while still holding more than one word - unlike
            // a lone overflowing word, forcing the *last* word to the line's flush-right edge here would
            // move it backward through the first word's own trailing edge, producing overlapping/garbled
            // text instead of a coherent overflowing line.
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='d' style='margin:0;width:60pt;text-align:justify;font-size:14pt'>" +
                    "aa <span style='white-space:nowrap'>bbbbbbbbbbbbbbbbbbbbbbbbb cccccccccccccccccccccccc</span> dd ee</div>"));

            var d = LayoutHarness.FindById(root, "d")!;
            Assert.True(d.LineBoxes.Count >= 2, "fixture must wrap onto multiple lines to reach a non-last line");

            var overflowLine = d.LineBoxes[1];
            Assert.Equal(2, overflowLine.Words.Count);

            var first = overflowLine.Words[0];
            var second = overflowLine.Words[1];

            var lineWidth = first.Width + second.Width;
            Assert.True(lineWidth > d.ClientRight - d.ClientLeft,
                "fixture must actually overflow the container for this test to be meaningful");

            Assert.True(second.Left >= first.Right - 0.01,
                $"expected the second word to start at or after the first word's trailing edge " +
                $"(first.Right={first.Right:F2}, second.Left={second.Left:F2}) - overlap means the words render garbled");

            // white-space:nowrap forbids *breaking* between the two words, not collapsing their real space
            // to nothing - a naive "floor the shared spacing at zero" fix would glue them together with no
            // visible gap at all, which is just as wrong as overlap for a source that has a real space here.
            var gap = second.Left - first.Right;
            Assert.True(gap > 1,
                $"expected a real, non-zero gap between the two words carried over from their source space " +
                $"(gap={gap:F2}) - words rendering flush against each other means natural word-spacing was lost");
        }

        [Fact]
        public async Task Justify_LineThatFits_StillFlushesToBothEdges_NoRegression()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='d' style='margin:0;width:200pt;text-align:justify;font-size:14pt'>" +
                    "aa bb cc dd ee ff gg hh ii jj kk ll mm nn oo pp qq rr ss tt uu vv</div>"));

            var d = LayoutHarness.FindById(root, "d")!;
            Assert.True(d.LineBoxes.Count >= 2, "fixture must wrap onto multiple lines to reach a non-last line");

            var justifiedLine = d.LineBoxes[0];
            Assert.True(justifiedLine.Words.Count > 1);

            var lastWord = justifiedLine.Words[^1];
            Assert.Equal(d.ClientRight, lastWord.Right, 1);
        }
    }
}

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
        public async Task Justify_SingleUnbreakableWord_OnNonLastLine_StaysFlushRight_SpillsPastLeftEdge()
        {
            // A justified line's own words already handle a lone overflowing word correctly (it's both
            // first and last, so the last-word flush override has no earlier sibling to overlap) - this
            // guards that against regressing while the multi-word case below is fixed.
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
            Assert.Equal(d.ClientRight, word.Right, 1);
            Assert.True(word.Left < d.ClientLeft,
                $"expected the overflowing justified line to spill past the left edge (word.Left={word.Left:F2} " +
                $"should be < ClientLeft={d.ClientLeft:F2})");
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

using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for the horizontal analog of issue #797's vertical fix:
    /// <c>CssLayoutEngine.ApplyRightAlignment</c>'s overflow guard used to discard a negative
    /// <c>diff</c>, leaving an overflowing RTL nowrap line (or any overflowing <c>text-align:right</c>
    /// line) exactly where natural (always left-to-right-flowing) layout put it - flush left, spilling
    /// off the physical right edge like LTR content would, instead of staying flush-right and spilling
    /// off the physical left edge the way real browsers render it.
    /// </summary>
    public class RtlOverflowAlignmentTests
    {
        [Fact]
        public async Task RtlNowrapLine_WiderThanContainer_StaysFlushRight_SpillsPastLeftEdge()
        {
            // A single unbroken Hebrew run (no spaces) - one CssRectWord, so there is exactly one line
            // and one word to reason about, and one far wider than the 50pt container at 18pt.
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='d' dir='rtl' style='margin:0;width:50pt;white-space:nowrap;direction:rtl;font-size:18pt'>" +
                    "אאאאאאאאאאאאאאאאאאאאאאאאאאאאאא</div>"));

            var d = LayoutHarness.FindById(root, "d")!;
            Assert.Single(d.LineBoxes);
            var word = Assert.Single(d.LineBoxes[0].Words);

            Assert.True(word.Width > d.ClientRight - d.ClientLeft,
                "fixture must actually overflow the container for this test to be meaningful");

            // Flush-right: the word's own right edge lands on the box's target right edge.
            Assert.Equal(d.ClientRight, word.Right, 1);

            // Spills left: with the pre-fix one-directional guard, word.Left stayed at d.ClientLeft
            // (natural, always-left-to-right flow start) instead of being shifted negative.
            Assert.True(word.Left < d.ClientLeft,
                $"expected the overflowing RTL line to spill past the left edge (word.Left={word.Left:F2} " +
                $"should be < ClientLeft={d.ClientLeft:F2}) - it stayed flush-left instead");
        }

        [Fact]
        public async Task LtrTextAlignRight_NowrapLine_WiderThanContainer_StaysFlushRight_SpillsPastLeftEdge()
        {
            // The same guard also governed plain text-align:right overflow for LTR content - not an
            // RTL-only bug.
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='d' style='margin:0;width:50pt;white-space:nowrap;text-align:right;font-size:18pt'>" +
                    "abcdefghijklmnopqrstuvwxyz</div>"));

            var d = LayoutHarness.FindById(root, "d")!;
            Assert.Single(d.LineBoxes);
            var word = Assert.Single(d.LineBoxes[0].Words);

            Assert.True(word.Width > d.ClientRight - d.ClientLeft,
                "fixture must actually overflow the container for this test to be meaningful");

            Assert.Equal(d.ClientRight, word.Right, 1);
            Assert.True(word.Left < d.ClientLeft,
                $"expected the overflowing right-aligned line to spill past the left edge (word.Left={word.Left:F2} " +
                $"should be < ClientLeft={d.ClientLeft:F2}) - it stayed flush-left instead");
        }
    }
}

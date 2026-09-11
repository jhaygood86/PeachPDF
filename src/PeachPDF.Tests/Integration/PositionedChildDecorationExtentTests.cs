using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A box's own painted border box is sized from its <b>in-flow</b> content. An absolutely- or
    /// fixed-positioned descendant is out of the flow entirely
    /// (<see href="https://www.w3.org/TR/CSS21/visuren.html#absolute-positioning">CSS 2.1 §9.3.1</see>) and
    /// contributes nothing to it (§10.6.3), however far outside the box it is placed.
    /// </summary>
    /// <remarks>
    /// <c>FragmentEmitter.ExtentOf</c> grows a box's extent to whatever its content actually reached — a
    /// real need, for content that overflows bounds pinned before it was known. Counting a positioned child
    /// there dragged the box's border and background down to wherever that child landed, which is a place
    /// authors put one on purpose: Charts.css's axis labels are `position: absolute` with an auto block-start
    /// margin and a negative block-end margin precisely so they hang below their row, and that pulled the
    /// row's own `border-block-end` — the chart's primary axis — down under the labels with them.
    /// </remarks>
    public class PositionedChildDecorationExtentTests
    {
        [Theory]
        [InlineData("absolute")]
        [InlineData("fixed")]
        public async Task PositionedChildBelowItsParent_DoesNotGrowTheParentsDecorationRect(string position)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>" +
                "  #cb { position: relative; width: 200pt; height: 100pt;" +
                "        border-bottom: 2pt solid black; margin: 0; padding: 0; }" +
                $"  #hanging {{ position: {position}; top: 120pt; left: 0; width: 50pt; height: 20pt; }}" +
                "</style>" +
                "<div id='cb'><div id='hanging'>x</div></div>"), margin: 20);

            var cb = LayoutHarness.FindById(root, "cb")!;

            // 100pt content + the 2pt bottom border, whatever the child below it is doing.
            var fragment = FragmentPaintHarness.FragmentOf(container, cb);
            var decoration = Assert.Single(fragment.Lines);

            Assert.Equal(102, decoration.Rect.Height, 0.5);
            Assert.Equal(cb.Bounds.Height, decoration.Rect.Height, 0.5);
        }

        [Fact]
        public async Task InFlowChildOverflowingItsParent_StillGrowsTheDecorationRect()
        {
            // The guard above must not blunt what the extension is for: an in-flow child that overflows its
            // parent's declared bounds still extends what the parent paints.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>" +
                "  #cb { position: relative; width: 200pt; height: 40pt; margin: 0; padding: 0; }" +
                "  #tall { height: 120pt; }" +
                "</style>" +
                "<div id='cb'><div id='tall'>x</div></div>"), margin: 20);

            var cb = LayoutHarness.FindById(root, "cb")!;
            var fragment = FragmentPaintHarness.FragmentOf(container, cb);
            var decoration = Assert.Single(fragment.Lines);

            Assert.True(decoration.Rect.Height > 100,
                $"expected the overflowing in-flow child to extend the rect, got {decoration.Rect.Height}");
        }
    }
}

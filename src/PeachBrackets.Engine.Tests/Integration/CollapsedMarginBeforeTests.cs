using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <see href="https://www.w3.org/TR/CSS21/box.html#collapsing-margins">CSS 2.1 §8.3.1</see>'s
    /// adjoining-margin set, resolved at one break point by the frame the boxes are children of.
    /// </summary>
    /// <remarks>
    /// The set spans two frames, and these pin both halves: what precedes a box is the frame's to see
    /// (a predecessor's bottom margin, and everything a run of self-collapsing predecessors folds in),
    /// while the box's own top margin and the first-in-flow-child chain adjoining it are a walk into its
    /// own subtree. §8.3.1 collapses the whole set at once, so the two are folded together rather than
    /// resolved separately.
    /// </remarks>
    public class CollapsedMarginBeforeTests
    {
        [Fact]
        public async Task AdjoiningSiblingMargins_CollapseToTheLargerPositive()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='height:40pt;margin-bottom:30pt'></div>"
                + "<div id='b' style='height:40pt;margin-top:10pt'></div>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Equal(30, b.Location.Y - a.ActualBottom, 3);
        }

        [Fact]
        public async Task MixedSignSiblingMargins_Sum()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='height:40pt;margin-bottom:30pt'></div>"
                + "<div id='b' style='height:40pt;margin-top:-10pt'></div>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Equal(20, b.Location.Y - a.ActualBottom, 3);
        }

        [Fact]
        public async Task ASelfCollapsingSiblingBetweenTwoBoxes_JoinsTheSet()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='height:40pt;margin-bottom:10pt'></div>"
                + "<div style='margin:40pt 0'></div>"
                + "<div id='b' style='height:40pt;margin-top:10pt'></div>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            // The whole run collapses to one value — the largest member, not a sum of the pairs.
            Assert.Equal(40, b.Location.Y - a.ActualBottom, 3);
        }

        [Fact]
        public async Task AFirstInFlowChildsTopMargin_JoinsItsParentsOwnSet()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='height:40pt'></div>"
                + "<div id='outer' style='margin-top:10pt'>"
                + "<div id='inner' style='height:40pt;margin-top:30pt'></div></div>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var outer = LayoutHarness.FindById(root, "outer")!;
            var inner = LayoutHarness.FindById(root, "inner")!;

            // One collapsed value for the whole chain, taken once: the outer box moves down by it and
            // the inner box sits at its parent's content top rather than being pushed down again.
            Assert.Equal(30, outer.Location.Y - a.ActualBottom, 3);
            Assert.Equal(outer.Location.Y, inner.Location.Y, 3);
        }

        [Fact]
        public async Task ABorderOnTheParent_BlocksTheChainAndLeavesBothMarginsStanding()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='height:40pt'></div>"
                + "<div id='outer' style='margin-top:10pt;border-top:1pt solid black'>"
                + "<div id='inner' style='height:40pt;margin-top:30pt'></div></div>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var outer = LayoutHarness.FindById(root, "outer")!;
            var inner = LayoutHarness.FindById(root, "inner")!;

            Assert.Equal(10, outer.Location.Y - a.ActualBottom, 3);
            Assert.Equal(31, inner.Location.Y - outer.Location.Y, 3);
        }

        [Fact]
        public async Task AFloatsOwnMarginDoesNotMerge_ButThePredecessorsStillOccupiesSpace()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='height:40pt;margin-bottom:20pt'></div>"
                + "<div id='f' style='float:left;width:40pt;height:40pt;margin-top:-5pt'></div>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var f = LayoutHarness.FindById(root, "f")!;

            // Summed, not merged: a float is out of flow so its margin never adjoins anything, but the
            // predecessor's trailing margin is real space it must sit after.
            Assert.Equal(15, f.Location.Y - a.ActualBottom, 3);
        }

        [Fact]
        public async Task AVerticalWritingModeFirstChild_JoinsTheChainOnceButBlocksFurtherDescent()
        {
            // Issue #776's latent-bug fix: a writing-mode change always establishes a new formatting
            // context (CSS Writing Modes 4 SS4.3), so the chain must stop at a vertical-rl first child -
            // its OWN top margin still legitimately joins the outer set (an ordinary physical margin,
            // regardless of the child's own internal writing mode), but the chain must not continue into
            // ITS first grandchild, whose own huge top margin is irrelevant (it's stacked along the
            // vertical child's own block axis, physical left/right, not top/bottom at all).
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='height:40pt'></div>"
                + "<div id='outer'>"
                + "<div id='verticalChild' style='writing-mode:vertical-rl;margin-top:15pt;width:60pt;height:40pt'>"
                + "<div id='grandchild' style='width:20pt;height:10pt;margin-top:200pt'>G</div>"
                + "</div></div>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var outer = LayoutHarness.FindById(root, "outer")!;

            Assert.Equal(15, outer.Location.Y - a.ActualBottom, 3);
        }
    }
}

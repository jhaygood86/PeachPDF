using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A block-level box's position is assigned by the frame above it, from what that frame placed
    /// before it — the seam <c>CssBox.PlaceBlockChild</c> names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What these pin is the <i>inputs</i> the seam resolves against, which is the half a box cannot
    /// answer alone: which box counts as "the one before this one" is a question about the frame's own
    /// child list, and the answer skips every child that is not in the flow the frame is placing.
    /// </para>
    /// <para>
    /// The root is the one box with no frame above it, so it stands in for its own — and the whole of
    /// what that has to mean is that it finds nothing before it, exactly as a box with no parent always
    /// did.
    /// </para>
    /// </remarks>
    public class BlockPlacementSeamTests
    {
        private const double Margin = 20;

        [Fact]
        public async Task BlockChild_TakesItsTop_FromThePreviousInFlowSiblingItsFrameHeld()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='height:40pt'></div><div id='b' style='height:40pt'></div>"),
                margin: Margin);

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Equal(a.ActualBottom, b.Location.Y, 3);
        }

        [Fact]
        public async Task ADisplayNoneSibling_IsNotThePredecessorTheFrameResolvesAgainst()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='height:40pt'></div>"
                + "<div id='hidden' style='display:none;height:200pt'></div>"
                + "<div id='b' style='height:40pt'></div>"),
                margin: Margin);

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Equal(a.ActualBottom, b.Location.Y, 3);
        }

        [Fact]
        public async Task AnOutOfFlowSibling_IsNotThePredecessorTheFrameResolvesAgainst()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='height:40pt'></div>"
                + "<div id='abs' style='position:absolute;top:300pt;height:40pt'></div>"
                + "<div id='b' style='height:40pt'></div>"),
                margin: Margin);

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Equal(a.ActualBottom, b.Location.Y, 3);
        }

        [Fact]
        public async Task AFloatedSibling_IsNotThePredecessorTheFrameResolvesAgainst()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='height:40pt'></div>"
                + "<div id='f' style='float:left;width:40pt;height:40pt'></div>"
                + "<div id='b' style='height:40pt'></div>"),
                margin: Margin);

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Equal(a.ActualBottom, b.Location.Y, 3);
        }

        [Fact]
        public async Task TheRoot_HasNothingBeforeIt_AndIsPlacedAtTheInitialContainingBlocksTop()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html style='margin:0'><body style='margin:0'>"
                + "<div style='height:40pt'></div></body></html>",
                margin: Margin);

            Assert.Equal(container.Location.Y, root.Location.Y, 3);
        }
    }
}

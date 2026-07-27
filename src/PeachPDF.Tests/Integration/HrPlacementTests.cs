using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>&lt;hr&gt;</c> resolves its own size but is positioned by the frame above it, like every other
    /// block-level box.
    /// </summary>
    /// <remarks>
    /// It used to carry a copy of the placement formula, and each of these is a rule that copy had drifted
    /// on. The <c>margin: 0</c> in the fixtures keeps the rule's own default margin out of the arithmetic;
    /// the assertions are all differences against the predecessor, so the UA's own <c>&lt;hr&gt;</c> margin
    /// floor never enters them.
    /// </remarks>
    public class HrPlacementTests
    {
        [Fact]
        public async Task ARelativelyPositionedPredecessor_DoesNotDragTheRuleWithIt()
        {
            var (staticRoot, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='height:40pt'></div><hr id='h' style='margin:0'>"));
            var (offsetRoot, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='height:40pt;position:relative;top:20pt'></div><hr id='h' style='margin:0'>"));

            // CSS 2.1 §9.4.3: a relative offset is purely visual and never moves following content.
            Assert.Equal(
                LayoutHarness.FindById(staticRoot, "h")!.Location.Y,
                LayoutHarness.FindById(offsetRoot, "h")!.Location.Y,
                3);
        }

        [Fact]
        public async Task AFloatedPredecessor_IsNotTheRulesMarginCollapsePartner()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='height:40pt;margin-bottom:30pt'></div>"
                + "<div style='float:left;width:10pt;height:10pt;margin-bottom:5pt'></div>"
                + "<hr id='h' style='margin-top:10pt'>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var h = LayoutHarness.FindById(root, "h")!;

            // §8.3.1: margins collapse against the nearest *in-flow* predecessor, so the set is
            // max(30, 10) measured from the block above, not anything resolved against the float.
            Assert.Equal(30, h.Location.Y - a.ActualBottom, 3);
        }

        [Fact]
        public async Task ABorderedPredecessorsBottomBorder_IsNotCountedTwice()
        {
            var (borderless, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='height:40pt'></div><hr id='h' style='margin:0'>"));
            var (bordered, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='height:40pt;border-bottom:10pt solid black'></div><hr id='h' style='margin:0'>"));

            var plainGap = LayoutHarness.FindById(borderless, "h")!.Location.Y
                           - LayoutHarness.FindById(borderless, "a")!.ActualBottom;
            var borderedGap = LayoutHarness.FindById(bordered, "h")!.Location.Y
                              - LayoutHarness.FindById(bordered, "a")!.ActualBottom;

            // ActualBottom is already the outer border-box edge, so a bottom border changes where the
            // rule sits but not how far below that edge it lands.
            Assert.Equal(plainGap, borderedGap, 3);
        }

        [Fact]
        public async Task AnOversizedTopMarginAdjoiningAnUnforcedBreak_IsTruncated()
        {
            // 200pt sheet, 20pt margins — page k's band is [20 + 160k, 180 + 160k).
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='height:40pt'></div><hr id='h' style='margin-top:300pt'>"),
                pageHeight: 200, margin: 20);

            var h = LayoutHarness.FindById(root, "h")!;

            // CSS Fragmentation 3 §5.2: the margin adjoining the break is truncated and the rule starts
            // flush at the next fragmentainer's content top — it does not paginate through as blank
            // space. Going through the shared placement is what gets `<hr>` this at all.
            Assert.Equal(180, h.Location.Y, 3);
        }

        [Fact]
        public async Task TheRule_StillSpansItsContainingBlocksContentWidth()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='box' style='width:200pt;padding:0 10pt'><hr id='h' style='margin:0'></div>"));

            var h = LayoutHarness.FindById(root, "h")!;
            var box = LayoutHarness.FindById(root, "box")!;

            Assert.Equal(box.ClientLeft, h.Location.X, 3);
            Assert.Equal(180, h.ActualRight - h.Location.X, 3);
        }
    }
}

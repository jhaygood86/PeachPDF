using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Where an in-flow block-level box sits between the containing block's inline-start and inline-end
    /// edges when its <c>margin-left + border + padding + width + margin-right</c> does not add up to the
    /// containing block's width (<see href="https://www.w3.org/TR/CSS21/visudet.html#blockwidth">CSS 2.1
    /// §10.3.3</see>).
    /// </summary>
    /// <remarks>
    /// Two rules decide it, and neither is specific to <c>&lt;hr&gt;</c>:
    /// <list type="bullet">
    /// <item>Exactly one <c>auto</c> margin takes the whole slack - "its used value follows from the
    /// equality" - so <c>margin-left: auto</c> against a fixed <c>margin-right</c> pushes the box to the
    /// end edge. Both <c>auto</c> split it evenly.</item>
    /// <item>An <i>over</i>-constrained box ignores <c>margin-right</c> in an <c>ltr</c> containing block
    /// and <c>margin-left</c> in an <c>rtl</c> one, so <c>rtl</c> end-aligns it and sends any overflow
    /// toward the start edge.</item>
    /// </list>
    /// Every case is asserted on a plain <c>&lt;div&gt;</c> and on an <c>&lt;hr&gt;</c> with the same
    /// declaration, in <i>edge</i> coordinates (left and right offsets within the containing block's content
    /// box), so the rule's default border does not enter the arithmetic.
    /// </remarks>
    public class BlockInlinePlacementTests
    {
        private const string Box = "height: auto; border: 0 none; padding: 0";

        private static async Task<(double Left, double Right)> EdgesAsync(
            string tag, string containerDeclaration, string declaration)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body style='margin: 0'>"
                + $"<div id='container' style='width: 200pt; {containerDeclaration}'>"
                + $"<{tag} id='box' style='{Box}; {declaration}'>{(tag == "div" ? "</div>" : string.Empty)}"
                + "</div></body></html>");

            var container = LayoutHarness.FindById(root, "container")!;
            var box = LayoutHarness.FindById(root, "box")!;

            var contentLeft = container.ClientLeft;

            return (box.Location.X - contentLeft, box.ActualRight - contentLeft);
        }

        private static async Task AssertEdgesAsync(
            string containerDeclaration, string declaration, double expectedLeft, double expectedRight)
        {
            foreach (var tag in new[] { "div", "hr" })
            {
                var (left, right) = await EdgesAsync(tag, containerDeclaration, declaration);

                Assert.True(
                    Math.Abs(expectedLeft - left) < 0.001 && Math.Abs(expectedRight - right) < 0.001,
                    $"<{tag}> [{containerDeclaration}] {{{declaration}}}: expected edges {expectedLeft}..{expectedRight}, got {left}..{right}");
            }
        }

        // ── one auto margin absorbs the slack ─────────────────────────────────────

        [Theory]
        [InlineData("ltr")]
        [InlineData("rtl")]
        public async Task MarginLeftAuto_AgainstAFixedMarginRight_PushesTheBoxToTheEndEdge(string direction) =>
            await AssertEdgesAsync($"direction: {direction}", "width: 100pt; margin-left: auto; margin-right: 0", 100, 200);

        [Theory]
        [InlineData("ltr")]
        [InlineData("rtl")]
        public async Task MarginRightAuto_AgainstAFixedMarginLeft_PushesTheBoxToTheStartEdge(string direction) =>
            await AssertEdgesAsync($"direction: {direction}", "width: 100pt; margin-left: 0; margin-right: auto", 0, 100);

        [Theory]
        [InlineData("ltr")]
        [InlineData("rtl")]
        public async Task MarginLeftAuto_LeavesAFixedMarginRightBetweenTheBoxAndTheEdge(string direction) =>
            await AssertEdgesAsync($"direction: {direction}", "width: 100pt; margin-left: auto; margin-right: 30pt", 70, 170);

        [Theory]
        [InlineData("ltr")]
        [InlineData("rtl")]
        public async Task BothMarginsAuto_SplitTheSlackEvenly(string direction) =>
            await AssertEdgesAsync($"direction: {direction}", "width: 100pt; margin: 0 auto", 50, 150);

        [Fact]
        public async Task AnAutoMargin_ThatWouldBeNegative_ResolvesToZero() =>
            // 240pt of box in a 200pt block leaves no slack for an auto margin to take.
            await AssertEdgesAsync("direction: ltr", "width: 240pt; margin-left: auto; margin-right: 0", 0, 240);

        // ── over-constrained: which margin is ignored depends on direction ────────

        [Fact]
        public async Task Ltr_ANarrowerBoxWithBothMarginsFixed_SitsAtTheStartEdge() =>
            await AssertEdgesAsync("direction: ltr", "width: 100pt; margin: 0", 0, 100);

        [Fact]
        public async Task Rtl_ANarrowerBoxWithBothMarginsFixed_SitsAtTheEndEdge() =>
            await AssertEdgesAsync("direction: rtl", "width: 100pt; margin: 0", 100, 200);

        [Fact]
        public async Task Rtl_ANarrowerBoxKeepsItsMarginRightBetweenItAndTheEndEdge() =>
            await AssertEdgesAsync("direction: rtl", "width: 100pt; margin: 0 30pt 0 0", 70, 170);

        [Fact]
        public async Task Ltr_AnOverConstrainedBox_IgnoresMarginRightAndOverflowsTowardTheEnd() =>
            await AssertEdgesAsync("direction: ltr", "width: 120%; margin-left: 20pt", 20, 260);

        [Fact]
        public async Task Rtl_AnOverConstrainedBox_IgnoresMarginLeftAndOverflowsTowardTheStart() =>
            await AssertEdgesAsync("direction: rtl", "width: 120%; margin-left: 20pt", -40, 200);

        [Fact]
        public async Task Rtl_AutoWidth_StillFillsTheContainingBlock() =>
            await AssertEdgesAsync("direction: rtl", "margin: 0", 0, 200);

        [Fact]
        public async Task Rtl_PlacementIsFromTheContainingBlocksContentEdge() =>
            // A content-box container: 10pt of padding on each side, so its content box spans 200pt
            // between them, and the box is flush against the padding edge rather than the border edge.
            await AssertEdgesAsync("direction: rtl; padding: 0 10pt; border: 5pt solid", "width: 100pt; margin: 0", 100, 200);

        [Fact]
        public async Task Rtl_ARelativelyPositionedBox_StillMovesByItsOffset() =>
            await AssertEdgesAsync("direction: rtl", "width: 100pt; margin: 0; position: relative; left: 10pt", 110, 210);
    }
}

using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// Where an in-flow <c>&lt;table&gt;</c> narrower than its containing block sits: a table wrapper box is
    /// placed like a block-level box, so <see href="https://www.w3.org/TR/CSS21/visudet.html#blockwidth">CSS 2.1
    /// §10.3.3</see> applies - an over-constrained one ignores <c>margin-left</c> in an <c>rtl</c> containing block,
    /// which end-aligns it and sends any overflow toward the start edge.
    /// </summary>
    /// <remarks>
    /// Asserted in <i>edge</i> coordinates (left and right offsets within the containing block's content box),
    /// as <see cref="BlockInlinePlacementTests"/> does for blocks, so the two agree on the same declarations.
    /// </remarks>
    public class TableInlinePlacementTests
    {
        private static async Task AssertEdgesAsync(
            string containerDeclaration, string declaration, double expectedLeft, double expectedRight)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body style='margin: 0'>"
                + $"<div id='container' style='width: 200pt; {containerDeclaration}'>"
                + $"<table id='box' style='border-collapse: collapse; {declaration}'><tr><td style='padding: 0; height: 5pt'></td></tr></table>"
                + "</div></body></html>");

            var container = LayoutHarness.FindById(root, "container")!;
            var box = LayoutHarness.FindById(root, "box")!;

            var left = box.Location.X - container.ClientLeft;
            var right = box.ActualRight - container.ClientLeft;

            Assert.True(
                Math.Abs(expectedLeft - left) < 0.001 && Math.Abs(expectedRight - right) < 0.001,
                $"[{containerDeclaration}] {{{declaration}}}: expected edges {expectedLeft}..{expectedRight}, got {left}..{right}");
        }

        [Fact]
        public async Task ANarrowTable_InAnRtlContainer_SitsAgainstTheRightEdge() =>
            await AssertEdgesAsync("direction: rtl", "width: 100pt", 100, 200);

        [Fact]
        public async Task ANarrowTable_InAnLtrContainer_SitsAgainstTheLeftEdge() =>
            await AssertEdgesAsync("direction: ltr", "width: 100pt", 0, 100);

        [Theory]
        [InlineData("ltr")]
        [InlineData("rtl")]
        public async Task MarginLeftAuto_PushesTheTableToTheEndEdge(string direction) =>
            await AssertEdgesAsync($"direction: {direction}", "width: 100pt; margin-left: auto", 100, 200);

        [Theory]
        [InlineData("ltr")]
        [InlineData("rtl")]
        public async Task BothMarginsAuto_CentreTheTable(string direction) =>
            await AssertEdgesAsync($"direction: {direction}", "width: 100pt; margin: 0 auto", 50, 150);

        [Fact]
        public async Task AnRtlTable_LeavesItsRightMarginBetweenItselfAndTheEdge() =>
            await AssertEdgesAsync("direction: rtl", "width: 100pt; margin-left: 30pt; margin-right: 20pt", 80, 180);

        [Fact]
        public async Task AnOverWideRtlTable_OverflowsTowardTheStartEdge() =>
            await AssertEdgesAsync("direction: rtl", "width: 300pt", -100, 200);

        [Fact]
        public async Task AFullWidthRtlTable_FillsTheContainer() =>
            await AssertEdgesAsync("direction: rtl", "width: 100%", 0, 200);

        [Fact]
        public async Task ARtlTableWithNoWidth_ShrinksToFitAgainstTheRightEdge()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body style='margin: 0'><div id='container' style='width: 200pt; direction: rtl'>"
                + "<table id='box' style='border-collapse: collapse'><tr><td style='padding: 0'>x</td></tr></table></div></body></html>");

            var container = LayoutHarness.FindById(root, "container")!;
            var box = LayoutHarness.FindById(root, "box")!;

            Assert.True(box.Location.X - container.ClientLeft > 100, "a shrink-to-fit table is end-aligned, not left");
            Assert.Equal(200, box.ActualRight - container.ClientLeft, 3);
        }
    }
}

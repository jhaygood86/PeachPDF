using System.Threading.Tasks;
using PeachBrackets.Engine.Tests.TestSupport;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// CSS Grid Layout resolves its tracks, auto-placement and alignment against the grid container's
    /// <c>writing-mode</c> and <c>direction</c> (css-grid-2 section 1.2/8: grid lines are numbered from the
    /// inline-start/block-start edge). Box-position assertions through the lightweight layout harness; every
    /// fixture is in pt so expected values read literally.
    /// </summary>
    public class GridWritingModeDirectionTests
    {
        private const string Reset = "margin:0; padding:0; border:0;";

        private static async Task<CssBoxLookup> LayoutAsync(string body)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body), margin: 0);
            return new CssBoxLookup(root);
        }

        private sealed class CssBoxLookup(PeachBrackets.Html.Dom.CssBox root)
        {
            public PeachBrackets.Html.Dom.CssBox this[string id] => LayoutHarness.FindById(root, id)!;
        }

        // ─── direction: rtl (horizontal-tb) ────────────────────────────────────────

        [Fact]
        public async Task Rtl_ColumnOneIsTheRightmostTrackAndAutoPlacementFillsRightToLeft()
        {
            var b = await LayoutAsync(
                $"<div id='g' style='display:grid; direction:rtl; width:200pt; grid-template-columns:40pt 60pt; grid-template-rows:20pt 20pt; {Reset}'>" +
                "<div id='a'>a</div><div id='b'>b</div><div id='c'>c</div></div>");

            // Column 1 (40pt) is at the inline-start (right) edge, column 2 (60pt) to its left.
            Assert.Equal(160, b["a"].Location.X, 0.5);
            Assert.Equal(200, b["a"].ActualRight, 0.5);
            Assert.Equal(100, b["b"].Location.X, 0.5);
            Assert.Equal(160, b["b"].ActualRight, 0.5);
            // Row 2 starts again at the right edge.
            Assert.Equal(160, b["c"].Location.X, 0.5);
            Assert.Equal(b["a"].Location.Y + 20, b["c"].Location.Y, 0.5);
        }

        [Fact]
        public async Task Rtl_JustifyContentStartPacksTracksAgainstTheRightEdge()
        {
            var b = await LayoutAsync(
                $"<div style='display:grid; direction:rtl; width:200pt; grid-template-columns:40pt 40pt; justify-content:start; {Reset}'>" +
                "<div id='a'>a</div><div id='b'>b</div></div>");

            Assert.Equal(160, b["a"].Location.X, 0.5);
            Assert.Equal(120, b["b"].Location.X, 0.5);
        }

        [Fact]
        public async Task Rtl_JustifyContentEndPacksTracksAgainstTheLeftEdge()
        {
            var b = await LayoutAsync(
                $"<div style='display:grid; direction:rtl; width:200pt; grid-template-columns:40pt 40pt; justify-content:end; {Reset}'>" +
                "<div id='a'>a</div><div id='b'>b</div></div>");

            Assert.Equal(40, b["a"].Location.X, 0.5);
            Assert.Equal(0, b["b"].Location.X, 0.5);
        }

        [Fact]
        public async Task Rtl_JustifySelfStartAndEndAreLogical_LeftAndRightStayPhysical()
        {
            var b = await LayoutAsync(
                $"<div style='display:grid; direction:rtl; width:200pt; grid-template-columns:200pt; {Reset}'>" +
                "<div id='s' style='justify-self:start; width:50pt'>s</div>" +
                "<div id='e' style='justify-self:end; width:50pt'>e</div>" +
                "<div id='l' style='justify-self:left; width:50pt'>l</div>" +
                "<div id='r' style='justify-self:right; width:50pt'>r</div></div>");

            Assert.Equal(150, b["s"].Location.X, 0.5); // inline-start = right
            Assert.Equal(0, b["e"].Location.X, 0.5);   // inline-end = left
            Assert.Equal(0, b["l"].Location.X, 0.5);   // physical left
            Assert.Equal(150, b["r"].Location.X, 0.5); // physical right
        }

        [Fact]
        public async Task Rtl_ItemMarginsStayOnTheirPhysicalSides()
        {
            var b = await LayoutAsync(
                $"<div style='display:grid; direction:rtl; width:200pt; grid-template-columns:200pt; {Reset}'>" +
                "<div id='m' style='justify-self:start; width:50pt; margin-left:10pt; margin-right:4pt'>m</div></div>");

            // The 64pt margin box hangs from the right edge: border box [200-4-50, 200-4].
            Assert.Equal(146, b["m"].Location.X, 0.5);
            Assert.Equal(196, b["m"].ActualRight, 0.5);
        }

        [Fact]
        public async Task Rtl_ExplicitColumnLinesNumberFromTheRight()
        {
            var b = await LayoutAsync(
                $"<div style='display:grid; direction:rtl; width:200pt; grid-template-columns:50pt 50pt 100pt; {Reset}'>" +
                "<div id='a' style='grid-column:1 / span 2'>a</div><div id='c' style='grid-column:3'>c</div></div>");

            Assert.Equal(100, b["a"].Location.X, 0.5);
            Assert.Equal(200, b["a"].ActualRight, 0.5);
            Assert.Equal(0, b["c"].Location.X, 0.5);
            Assert.Equal(100, b["c"].ActualRight, 0.5);
        }

        [Fact]
        public async Task Rtl_ColumnAutoFlowStartsAtTheRightColumn()
        {
            var b = await LayoutAsync(
                $"<div style='display:grid; direction:rtl; width:200pt; grid-template-rows:20pt 20pt; grid-auto-flow:column; grid-auto-columns:50pt; {Reset}'>" +
                "<div id='a'>a</div><div id='b'>b</div><div id='c'>c</div></div>");

            Assert.Equal(150, b["a"].Location.X, 0.5);
            Assert.Equal(150, b["b"].Location.X, 0.5);
            Assert.Equal(100, b["c"].Location.X, 0.5);
        }

        [Fact]
        public async Task Rtl_ASubgridAdoptsTheMirroredParentColumns()
        {
            var b = await LayoutAsync(
                $"<div style='display:grid; direction:rtl; width:200pt; grid-template-columns:50pt 150pt; {Reset}'>" +
                $"<div id='sub' style='grid-column:1 / span 2; display:grid; grid-template-columns:subgrid; {Reset}'>" +
                "<div id='a'>a</div><div id='b'>b</div></div></div>");

            Assert.Equal(150, b["a"].Location.X, 0.5);
            Assert.Equal(200, b["a"].ActualRight, 0.5);
            Assert.Equal(0, b["b"].Location.X, 0.5);
            Assert.Equal(150, b["b"].ActualRight, 0.5);
        }

        [Fact]
        public async Task Rtl_AnInlineGridShrinksToItsTracksAndMirrorsInsideThem()
        {
            var b = await LayoutAsync(
                $"<div id='g' style='display:inline-grid; direction:rtl; grid-template-columns:40pt 30pt; {Reset}'>" +
                "<div id='a'>a</div><div id='b'>b</div></div>");

            var g = b["g"];
            Assert.Equal(70, g.ActualRight - g.Location.X, 0.5);
            Assert.Equal(g.ActualRight, b["a"].ActualRight, 0.5);
            Assert.Equal(g.Location.X, b["b"].Location.X, 0.5);
        }

        [Fact]
        public async Task VerticalRl_MinContentAndMaxContentColumnsMeasureTheInlineExtent()
        {
            var b = await LayoutAsync(
                $"<div style='display:grid; writing-mode:vertical-rl; font-size:10pt; line-height:12pt; grid-template-columns:min-content max-content; {Reset}'>" +
                "<div id='a'>aaaa bbbbbbbb cc</div><div id='b'>aaaa bbbbbbbb cc</div></div>");

            var minLen = b["a"].ActualBottom - b["a"].Location.Y;
            var maxLen = b["b"].ActualBottom - b["b"].Location.Y;
            Assert.True(maxLen > minLen * 1.5, $"max-content ({maxLen}) is the whole run, min-content ({minLen}) the longest word");
            // The min-content column wraps the text onto three lines of 12pt, and the row (shared by both
            // items) is as wide across as the taller of them.
            Assert.Equal(36, b["a"].ActualRight - b["a"].Location.X, 0.5);
            Assert.Equal(36, b["b"].ActualRight - b["b"].Location.X, 0.5);
        }

        [Fact]
        public async Task VerticalRl_AutoPlacementInColumnFlowFillsDownTheBlockAxis()
        {
            var b = await LayoutAsync(
                $"<div id='g' style='display:grid; writing-mode:vertical-rl; height:100pt; grid-template-rows:20pt 20pt; grid-auto-columns:50pt; grid-auto-flow:column; {Reset}'>" +
                "<div id='a'>a</div><div id='b'>b</div><div id='c'>c</div></div>");

            var g = b["g"];
            // Column flow fills the first column's rows before starting the next: a and b share column 1
            // (rows 1 and 2, right to left), c is in column 2 below them.
            Assert.Equal(g.ActualRight, b["a"].ActualRight, 0.5);
            Assert.Equal(g.Location.Y, b["a"].Location.Y, 0.5);
            Assert.Equal(g.ActualRight - 20, b["b"].ActualRight, 0.5);
            Assert.Equal(g.Location.Y, b["b"].Location.Y, 0.5);
            Assert.Equal(g.ActualRight, b["c"].ActualRight, 0.5);
            Assert.Equal(g.Location.Y + 50, b["c"].Location.Y, 0.5);
        }

        [Fact]
        public async Task VerticalRl_GridIsPlacedAtTheContainingBlocksBlockStartEdge()
        {
            var b = await LayoutAsync(
                $"<div id='g' style='display:grid; writing-mode:vertical-rl; height:100pt; grid-template-columns:100pt; grid-template-rows:25pt 25pt; {Reset}'>" +
                "<div id='a'>a</div><div id='b'>b</div></div>");

            Assert.Equal(595, b["g"].ActualRight, 0.5);
            Assert.Equal(545, b["g"].Location.X, 0.5);
        }

        [Fact]
        public async Task Ltr_IsUnchanged()
        {
            var b = await LayoutAsync(
                $"<div style='display:grid; width:200pt; grid-template-columns:40pt 60pt; {Reset}'>" +
                "<div id='a'>a</div><div id='b'>b</div></div>");

            Assert.Equal(0, b["a"].Location.X, 0.5);
            Assert.Equal(40, b["b"].Location.X, 0.5);
        }

        // ─── vertical writing modes ────────────────────────────────────────────────

        [Fact]
        public async Task VerticalRl_ColumnsRunDownAndRowsStackFromTheRight()
        {
            var b = await LayoutAsync(
                $"<div id='g' style='display:grid; writing-mode:vertical-rl; height:100pt; grid-template-columns:30pt 40pt; grid-template-rows:20pt 20pt; {Reset}'>" +
                "<div id='a'>a</div><div id='b'>b</div><div id='c'>c</div></div>");

            var g = b["g"];
            var top = g.Location.Y;

            // Columns (the inline axis) run top to bottom.
            Assert.Equal(top, b["a"].Location.Y, 0.5);
            Assert.Equal(30, b["a"].ActualBottom - b["a"].Location.Y, 0.5);
            Assert.Equal(top + 30, b["b"].Location.Y, 0.5);
            Assert.Equal(40, b["b"].ActualBottom - b["b"].Location.Y, 0.5);

            // Rows (the block axis) stack from the right edge towards the left; the container is as wide
            // as its two rows.
            Assert.Equal(40, g.ActualRight - g.Location.X, 0.5);
            Assert.Equal(g.ActualRight, b["a"].ActualRight, 0.5);
            Assert.Equal(g.ActualRight, b["b"].ActualRight, 0.5);
            Assert.Equal(20, b["a"].ActualRight - b["a"].Location.X, 0.5);
            // Row 2 sits to the left of row 1, and the third item starts the second row in column 1.
            Assert.Equal(g.ActualRight - 20, b["c"].ActualRight, 0.5);
            Assert.Equal(top, b["c"].Location.Y, 0.5);
        }

        [Fact]
        public async Task VerticalLr_RowsStackFromTheLeft()
        {
            var b = await LayoutAsync(
                $"<div id='g' style='display:grid; writing-mode:vertical-lr; height:100pt; grid-template-columns:30pt 40pt; grid-template-rows:20pt 20pt; {Reset}'>" +
                "<div id='a'>a</div><div id='b'>b</div><div id='c'>c</div></div>");

            var g = b["g"];
            Assert.Equal(g.Location.X, b["a"].Location.X, 0.5);
            Assert.Equal(g.Location.X + 20, b["c"].Location.X, 0.5);
            Assert.Equal(g.Location.Y + 30, b["b"].Location.Y, 0.5);
        }

        [Fact]
        public async Task VerticalRl_RtlDirectionNumbersColumnsFromTheBottom()
        {
            var b = await LayoutAsync(
                $"<div id='g' style='display:grid; writing-mode:vertical-rl; direction:rtl; height:100pt; grid-template-columns:30pt 40pt; grid-template-rows:20pt; {Reset}'>" +
                "<div id='a'>a</div><div id='b'>b</div></div>");

            var g = b["g"];
            Assert.Equal(g.Location.Y + 100, b["a"].ActualBottom, 0.5);
            Assert.Equal(g.Location.Y + 70, b["b"].ActualBottom, 0.5);
        }

        [Fact]
        public async Task VerticalRl_JustifyContentAndAlignContentAreLogical()
        {
            var b = await LayoutAsync(
                $"<div id='g' style='display:grid; writing-mode:vertical-rl; height:100pt; width:100pt; grid-template-columns:30pt; grid-template-rows:20pt; justify-content:end; align-content:end; {Reset}'>" +
                "<div id='a'>a</div></div>");

            var g = b["g"];
            // inline-end is the bottom, block-end is the left in vertical-rl.
            Assert.Equal(g.Location.Y + 100, b["a"].ActualBottom, 0.5);
            Assert.Equal(g.Location.X, b["a"].Location.X, 0.5);
        }

        [Fact]
        public async Task VerticalRl_ItemAlignmentIsLogical()
        {
            var b = await LayoutAsync(
                $"<div id='g' style='display:grid; writing-mode:vertical-rl; height:100pt; width:100pt; grid-template-columns:100pt; grid-template-rows:100pt; {Reset}'>" +
                "<div id='a' style='justify-self:end; align-self:start; height:20pt; width:10pt'>a</div></div>");

            var g = b["g"];
            // justify-self:end -> bottom of the cell; align-self:start -> the right (block-start) edge.
            Assert.Equal(g.Location.Y + 100, b["a"].ActualBottom, 0.5);
            Assert.Equal(g.ActualRight, b["a"].ActualRight, 0.5);
        }

        [Fact]
        public async Task VerticalRl_FrTracksShareTheDefiniteHeight()
        {
            var b = await LayoutAsync(
                $"<div id='g' style='display:grid; writing-mode:vertical-rl; height:120pt; grid-template-columns:1fr 2fr; {Reset}'>" +
                "<div id='a'>a</div><div id='b'>b</div></div>");

            var g = b["g"];
            Assert.Equal(40, b["a"].ActualBottom - b["a"].Location.Y, 0.5);
            Assert.Equal(80, b["b"].ActualBottom - b["b"].Location.Y, 0.5);
            Assert.Equal(g.Location.Y + 40, b["b"].Location.Y, 0.5);
        }

        [Fact]
        public async Task VerticalRl_AutoColumnsAreSizedFromTheirContentsInlineExtent()
        {
            var b = await LayoutAsync(
                $"<div id='g' style='display:grid; writing-mode:vertical-rl; font-size:10pt; line-height:12pt; grid-template-columns:auto auto; {Reset}'>" +
                "<div id='a'>aaaaaaaaaa</div><div id='b'>bbbbbbbbbbbbbbbbbbbb</div></div>");

            var aLen = b["a"].ActualBottom - b["a"].Location.Y;
            var bLen = b["b"].ActualBottom - b["b"].Location.Y;
            Assert.True(aLen > 20, $"first column is as long as its text ({aLen})");
            Assert.True(bLen > aLen * 1.5, $"second column is about twice as long ({bLen} vs {aLen})");
            Assert.Equal(b["a"].ActualBottom, b["b"].Location.Y, 0.5);
        }
    }
}

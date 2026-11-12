using PeachBrackets.Css;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// The legacy <c>&lt;table&gt;</c> attributes that the
    /// <see href="https://html.spec.whatwg.org/multipage/rendering.html#tables-2">HTML Standard's table rendering
    /// rules</see> map to something other than the generic translation: <c>align</c> places the table itself
    /// (<c>float</c>, or auto margins) and leaves the cells' <c>text-align</c> alone, and <c>cellpadding</c>/<c>border</c>
    /// are presentational hints of the cells, so any author declaration on the cell beats them.
    /// </summary>
    /// <remarks>
    /// Every literal was measured in headless Chrome (1px = 0.75pt). An HTML email is the case that relies on all of it:
    /// <c>&lt;table align="center" cellpadding="0"&gt;</c> holding cells that carry their own inline <c>padding</c>.
    /// </remarks>
    public class TableLegacyAttributeTests
    {
        private const double Px = 0.75;

        private static Task<(CssBox Root, HtmlContainerInt Container)> LayoutAsync(string body, string css = "") =>
            LayoutHarness.LayoutAsync(
                $"<!DOCTYPE html><html><head><style>body {{ margin: 0 }} {css}</style></head><body>{body}</body></html>");

        private static async Task<CssBox> CellAsync(string body, string css = "")
        {
            var (root, _) = await LayoutAsync(body, css);
            // A table head or foot repeats through a proxy, which detaches the original from the live tree.
            return LayoutHarness.FindByIdIncludingHeaderFooterProxies(root, "c")!;
        }

        private static async Task<double> TableLeftAsync(string tableAttributes, string css = "")
        {
            var (root, _) = await LayoutAsync(
                $"<div id='c'><table id='t' style='width: 100pt' {tableAttributes}><tr><td>x</td></tr></table></div>",
                $"#c {{ width: 200pt }} {css}");
            var table = LayoutHarness.FindById(root, "t")!;
            var container = LayoutHarness.FindById(root, "c")!;

            return table.Location.X - container.ClientLeft;
        }

        // ── align ─────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("align='center'", 50)]
        [InlineData("align='CENTER'", 50)]
        [InlineData("align='right'", 100)]
        [InlineData("align='RIGHT'", 100)]
        [InlineData("align='left'", 0)]
        [InlineData("align='LEFT'", 0)]
        // The match is on the exact value, so padding it with whitespace matches nothing and the table stays at the start.
        [InlineData("align=' center '", 0)]
        [InlineData("align='middle'", 0)]
        public async Task Align_PlacesTheTable(string attribute, double expectedLeft)
        {
            Assert.Equal(expectedLeft, await TableLeftAsync(attribute), 2);
        }

        [Theory]
        [InlineData("table { margin: 0 }", 0)]
        [InlineData("table { margin: 0 0 0 20pt }", 20)]
        public async Task Align_LosesToAnAuthorMargin(string css, double expectedLeft)
        {
            // Guards the order of the hint and the author rule (the hint is applied first, so the rule overrides it); it cannot
            // fail on a build that never centred a table at all.
            Assert.Equal(expectedLeft, await TableLeftAsync("align='center'", css), 2);
        }

        private static async Task<(double Left, double Width, double SiblingLeft)> PlacedAsync(string tableAttributes, string css = "")
        {
            // The sibling after the table is what puts the table through the block flow's float placement.
            var (root, _) = await LayoutAsync(
                $"<div id='c'><table id='t' {tableAttributes}><tr><td>x</td></tr></table><div id='s'>after</div></div>",
                $"#c {{ width: 200pt }} {css}");
            var table = LayoutHarness.FindById(root, "t")!;
            var container = LayoutHarness.FindById(root, "c")!;
            var sibling = LayoutHarness.FindById(root, "s")!;

            return (table.Location.X - container.ClientLeft, table.ActualRight - table.Location.X, sibling.Location.X - container.ClientLeft);
        }

        [Theory]
        [InlineData("align='right' style='width: 100pt'")]
        [InlineData("style='float: right; width: 100pt'")]
        public async Task RightFloatedTable_FollowedByABlock_EndsAtTheContainersRightEdge(string attributes)
        {
            // The table's width is only known once its columns have settled, which is after the float is first placed: it used to be
            // anchored with no width to subtract, at the right edge itself, so a visible table moved off the page.
            var (left, width, siblingLeft) = await PlacedAsync(attributes);

            Assert.Equal(100, width, 2);
            Assert.Equal(100, left, 2);
            Assert.Equal(0, siblingLeft, 2);
        }

        [Fact]
        public async Task RightFloatedTable_AsWideAsItsContainer_StartsAtTheContainersLeftEdge()
        {
            var (left, width, _) = await PlacedAsync("align='right' style='width: 100%'");

            Assert.Equal(200, width, 2);
            Assert.Equal(0, left, 2);
        }

        [Fact]
        public async Task LeftFloatedTable_FollowedByABlock_StartsAtTheContainersLeftEdge()
        {
            var (left, width, _) = await PlacedAsync("align='left' style='width: 100pt'");

            Assert.Equal(100, width, 2);
            Assert.Equal(0, left, 2);
        }

        [Theory]
        [InlineData("align='center' style='width: 300pt'")]
        [InlineData("style='margin-left: auto; margin-right: auto; width: 300pt'")]
        public async Task CenteredTable_WiderThanItsContainer_StartsAtTheStartEdge(string attributes)
        {
            // CSS 2.1 §10.3.3: an over-constrained box's auto margins are 0, so the extra width overflows at the end. It used to be
            // centred through a negative margin, which pulled the table out past the start edge.
            var (left, width, _) = await PlacedAsync(attributes);

            Assert.Equal(0, left, 2);
            Assert.True(width >= 300 * 0.99, $"the table keeps its own width, was {width}");
        }

        private static async Task<(double Left, double Width, double CellOffset)> PlacedWithCellAsync(string tableAttributes, string containerAttributes = "")
        {
            var (root, _) = await LayoutAsync(
                $"<div id='c' {containerAttributes}><table id='t' {tableAttributes}><tr><td id='cell'>x</td></tr></table><div>after</div></div>",
                "#c { width: 200pt }");
            var table = LayoutHarness.FindById(root, "t")!;
            var container = LayoutHarness.FindById(root, "c")!;
            var cell = LayoutHarness.FindById(root, "cell")!;

            return (table.Location.X - container.ClientLeft, table.ActualRight - table.Location.X, cell.Location.X - table.Location.X);
        }

        [Theory]
        [InlineData("align='right' style='width: 300pt'")]
        [InlineData("style='float: right; width: 300pt'")]
        [InlineData("align='right' style='width: 150%'")]
        public async Task RightFloatedTable_WiderThanItsContainer_KeepsItsCellsInsideItsBox(string attributes)
        {
            // Chrome: the table's end edge meets the container's, so it overflows at the start, and its cell sits one border-spacing
            // (2px) inside its box. The cells used to stay at the page's left edge while the box moved out to the left.
            var (left, width, cellOffset) = await PlacedWithCellAsync(attributes);

            Assert.Equal(300, width, 0);
            Assert.Equal(-(width - 200), left, 2);
            Assert.Equal(2 * Px, cellOffset, 2);
        }

        [Theory]
        [InlineData("align='center' style='width: 300pt'")]
        [InlineData("style='margin-left: auto; margin-right: auto; width: 300pt'")]
        [InlineData("style='margin-left: auto; width: 300pt'")]
        [InlineData("style='margin-left: 0; margin-right: auto; width: 300pt'")]
        public async Task AutoMarginTable_WiderThanAnRtlContainer_OverflowsTowardTheStartEdge(string attributes)
        {
            // CSS 2.1 §10.3.3: the auto margin is 0 and margin-left is the ignored one in an rtl container, so the table's right
            // edge meets the container's and it overflows at the left, as the box and its cell do in Chrome.
            var (left, width, cellOffset) = await PlacedWithCellAsync(attributes, "dir='rtl'");

            Assert.Equal(300, width, 0);
            Assert.Equal(-(width - 200), left, 2);
            Assert.Equal(2 * Px, cellOffset, 2);
        }

        [Fact]
        public async Task AutoMarginTable_ExactlyAsWideAsAnRtlContainer_FillsIt()
        {
            var (left, width, cellOffset) = await PlacedWithCellAsync("align='center' style='width: 200pt'", "dir='rtl'");

            Assert.Equal(200, width, 0);
            Assert.Equal(0, left, 2);
            Assert.Equal(2 * Px, cellOffset, 2);
        }

        [Fact]
        public async Task AutoMarginTable_NarrowerThanAnRtlContainer_StillCenters()
        {
            var (left, width, _) = await PlacedWithCellAsync("align='center' style='width: 100pt'", "dir='rtl'");

            Assert.Equal(100, width, 1);
            Assert.Equal(50, left, 2);
        }

        [Theory]
        [InlineData("left", "Left")]
        [InlineData("right", "Right")]
        [InlineData("center", "None")]
        public async Task Align_FloatsOnlyForLeftAndRight(string value, string expected)
        {
            var (root, _) = await LayoutAsync($"<table id='t' align='{value}'><tr><td>x</td></tr></table>");

            Assert.Equal(expected, LayoutHarness.FindById(root, "t")!.Float.Value.ToString());
        }

        [Theory]
        [InlineData("center")]
        [InlineData("right")]
        public async Task Align_DoesNotChangeTheTextAlignmentOfItsCells(string value)
        {
            // The old translation set `text-align` on the table, which every cell then inherited.
            var (root, _) = await LayoutAsync($"<table id='t' align='{value}'><tr><td id='c'>x</td></tr></table>");
            var table = LayoutHarness.FindById(root, "t")!;
            var cell = LayoutHarness.FindById(root, "c")!;

            Assert.Equal(HorizontalAlignment.Start, table.ActualTextAlignAll);
            Assert.Equal(HorizontalAlignment.Start, cell.ActualTextAlignAll);
        }

        [Theory]
        [InlineData("<td align='center'>", "left")]
        [InlineData("<td align='right'>", "right")]
        [InlineData("<td align='right'>", "center")]
        public async Task Align_StopsALegacyParentAlignmentFromReachingTheCells(string parentCell, string tableAlign)
        {
            // A browser does not pass a <td align> / <center> alignment into a table, so the cells keep their start edge.
            var (root, _) = await LayoutAsync(
                $"<table><tr>{parentCell}<table align='{tableAlign}'><tr><td id='c'>x</td></tr></table></td></tr></table>");
            var cell = LayoutHarness.FindById(root, "c")!;

            Assert.Equal(HorizontalAlignment.Start, cell.ActualTextAlignAll);
        }

        [Fact]
        public async Task Align_OnACell_StillSetsTextAlign()
        {
            var cell = await CellAsync("<table><tr><td id='c' align='center'>x</td></tr></table>");

            Assert.Equal(HorizontalAlignment.Center, cell.ActualTextAlignAll);
        }

        // ── cellpadding ───────────────────────────────────────────────────────────

        private static void AssertPadding(CssBox cell, double top, double right, double bottom, double left)
        {
            Assert.Equal(top * Px, cell.ActualPaddingTop, 2);
            Assert.Equal(right * Px, cell.ActualPaddingRight, 2);
            Assert.Equal(bottom * Px, cell.ActualPaddingBottom, 2);
            Assert.Equal(left * Px, cell.ActualPaddingLeft, 2);
        }

        // The browser's own default (HTML Standard section 15.3.12: td, th { padding: 1px }), which a table with no cellpadding attribute leaves in place.

        [Theory]
        [InlineData("td")]
        [InlineData("th")]
        public async Task ACellOfATableWithNoCellpaddingHasThe1pxDefaultPadding(string tag)
        {
            var cell = await CellAsync($"<table><tr><{tag} id='c'>x</{tag}></tr></table>");

            AssertPadding(cell, 1, 1, 1, 1);
        }

        [Fact]
        public async Task ACellpaddingOfZeroTakesTheDefaultAway()
        {
            var cell = await CellAsync("<table cellpadding='0'><tr><td id='c'>x</td></tr></table>");

            AssertPadding(cell, 0, 0, 0, 0);
        }

        [Fact]
        public async Task AnAuthorPaddingOfZeroTakesTheDefaultAway()
        {
            var cell = await CellAsync("<table><tr><td id='c' style='padding: 0'>x</td></tr></table>");

            AssertPadding(cell, 0, 0, 0, 0);
        }

        [Fact]
        public async Task ADivInsideACellHasNoPaddingOfItsOwn()
        {
            var cell = await CellAsync("<table><tr><td><div id='c'>x</div></td></tr></table>");

            AssertPadding(cell, 0, 0, 0, 0);
        }

        [Fact]
        public async Task Cellpadding_YieldsToTheCellsInlinePadding()
        {
            var cell = await CellAsync("<table cellpadding='0'><tr><td id='c' style='padding: 30px 25px'>x</td></tr></table>");

            AssertPadding(cell, 30, 25, 30, 25);
        }

        [Fact]
        public async Task Cellpadding_YieldsToAStylesheetRule()
        {
            var cell = await CellAsync("<table cellpadding='20'><tr><td id='c'>x</td></tr></table>", "td { padding: 7px }");

            AssertPadding(cell, 7, 7, 7, 7);
        }

        [Fact]
        public async Task Cellpadding_YieldsOnlyForTheSideTheAuthorDeclared()
        {
            var cell = await CellAsync("<table cellpadding='20'><tr><td id='c' style='padding-left: 12px'>x</td></tr></table>");

            AssertPadding(cell, 20, 20, 20, 12);
        }

        [Fact]
        public async Task Cellpadding_YieldsToAnImportantRule()
        {
            var cell = await CellAsync("<table cellpadding='9'><tr><td id='c'>x</td></tr></table>", "td { padding: 0 !important }");

            AssertPadding(cell, 0, 0, 0, 0);
        }

        [Fact]
        public async Task Cellpadding_ReachesAHeaderCell()
        {
            // A th is a cell like a td.
            var cell = await CellAsync("<table cellpadding='9'><tr><th id='c'>x</th></tr></table>");

            AssertPadding(cell, 9, 9, 9, 9);
        }

        [Fact]
        public async Task Cellpadding_ReachesACellInATableBody()
        {
            var cell = await CellAsync("<table cellpadding='9'><tbody><tr><td id='c'>x</td></tr></tbody></table>");

            AssertPadding(cell, 9, 9, 9, 9);
        }

        [Fact]
        public async Task Cellpadding_OfTheNearestTableWins_OverAnOuterTables()
        {
            var cell = await CellAsync(
                "<table cellpadding='20'><tr><td><table cellpadding='4'><tr><td id='c'>x</td></tr></table></td></tr></table>");

            AssertPadding(cell, 4, 4, 4, 4);
        }

        [Fact]
        public async Task Cellpadding_ReachesACellInATableFoot()
        {
            var cell = await CellAsync("<table cellpadding='9'><tfoot><tr><td id='c'>x</td></tr></tfoot></table>");

            AssertPadding(cell, 9, 9, 9, 9);
        }

        [Fact]
        public async Task Cellpadding_OfAnOuterTable_DoesNotReachANestedTablesCells()
        {
            // Only the nearest ancestor table's cellpadding counts; the nested table has none, so its cell keeps the UA's default of 1px, not 20px.
            var cell = await CellAsync(
                "<table cellpadding='20'><tr><td><table><tr><td id='c'>x</td></tr></table></td></tr></table>");

            AssertPadding(cell, 1, 1, 1, 1);
        }

        // ── border ────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Border_YieldsToTheCellsOwnBorder()
        {
            var cell = await CellAsync("<table border='1'><tr><td id='c' style='border: none'>x</td></tr></table>");

            Assert.Equal(0, cell.ActualBorderTopWidth, 2);
            Assert.Equal(0, cell.ActualBorderLeftWidth, 2);
        }

        [Fact]
        public async Task Border_YieldsToAStylesheetBorder()
        {
            var cell = await CellAsync("<table border='1'><tr><td id='c'>x</td></tr></table>", "td { border: 3px solid red }");

            Assert.Equal(3 * Px, cell.ActualBorderTopWidth, 2);
            Assert.Equal(3 * Px, cell.ActualBorderLeftWidth, 2);
        }

        [Fact]
        public async Task BorderZero_DoesNotReachTheCells()
        {
            var cell = await CellAsync("<table border='0'><tr><td id='c'>x</td></tr></table>");

            Assert.Equal(0, cell.ActualBorderTopWidth, 2);
        }

        [Fact]
        public async Task Border_ReachesACellInATableHead()
        {
            var cell = await CellAsync("<table border='1'><thead><tr><th id='c'>x</th></tr></thead></table>");

            Assert.Equal(1 * Px, cell.ActualBorderTopWidth, 2);
            // Not a Chrome measurement: a browser computes `inset` for a `border` attribute, this engine paints it `solid`.
            Assert.Equal(LineStyle.Solid.ToString(), cell.BorderLeftStyle.Value.ToString());
            Assert.Equal(LineStyle.Solid.ToString(), cell.BorderBottomStyle.Value.ToString());
        }

        [Fact]
        public async Task Border_ReachesAHeaderCell()
        {
            var cell = await CellAsync("<table border='1'><tr><th id='c'>x</th></tr></table>");

            Assert.Equal(1 * Px, cell.ActualBorderTopWidth, 2);
        }
    }
}

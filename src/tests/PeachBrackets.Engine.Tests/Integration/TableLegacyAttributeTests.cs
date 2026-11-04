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

        [Fact]
        public async Task Align_LosesToAnAuthorMargin()
        {
            Assert.Equal(0, await TableLeftAsync("align='center'", "table { margin: 0 }"), 2);
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
            // Only the nearest ancestor table's cellpadding counts; the nested table has none, so its cell keeps the UA's padding (none here; a browser's is 1px).
            var cell = await CellAsync(
                "<table cellpadding='20'><tr><td><table><tr><td id='c'>x</td></tr></table></td></tr></table>");

            AssertPadding(cell, 0, 0, 0, 0);
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

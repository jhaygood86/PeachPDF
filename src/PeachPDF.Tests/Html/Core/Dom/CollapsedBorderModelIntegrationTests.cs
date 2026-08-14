using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// <see cref="CollapsedBorderModel"/> wired into <see cref="CssLayoutEngineTable"/> against real,
    /// laid-out tables - resolved values only (cell/row/row-group/table origins; the column/column-group
    /// tier lands separately). No geometry or paint assertions here - <see cref="CssBox.CollapsedBorders"/>
    /// is set but nothing yet reads it to position or paint anything.
    /// </summary>
    public class CollapsedBorderModelIntegrationTests
    {
        private static async Task<CssBox> LayoutTable(string bodyHtml)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(bodyHtml));
            return LayoutHarness.Descendants(root).First(b => b.DerivedStyle.ActualDisplay is Keywords.Table or Keywords.InlineTable);
        }

        [Fact]
        public async Task SeparateTable_BuildsNoGridOrModel()
        {
            var table = await LayoutTable(@"
                <table style='border-collapse:separate'>
                    <tr><td>a</td><td>b</td></tr>
                </table>");

            Assert.Null(table.CollapsedBorderGrid);
            Assert.Null(table.CollapsedBorders);
        }

        [Fact]
        public async Task CollapseTable_BuildsGridAndModel()
        {
            var table = await LayoutTable(@"
                <table style='border-collapse:collapse'>
                    <tr><td>a</td><td>b</td></tr>
                </table>");

            Assert.NotNull(table.CollapsedBorderGrid);
            Assert.NotNull(table.CollapsedBorders);
            Assert.Equal(1, table.CollapsedBorderGrid!.RowCount);
            Assert.Equal(2, table.CollapsedBorderGrid!.ColumnCount);
        }

        [Fact]
        public async Task SharedEdge_OnlyOneSideDeclaresABorder_ThatOneWins()
        {
            var table = await LayoutTable(@"
                <table style='border-collapse:collapse'>
                    <tr><td id='a' style='border-bottom:2pt solid black'>a</td></tr>
                    <tr><td id='b'>b</td></tr>
                </table>");

            var resolved = table.CollapsedBorders!.Horizontal(1, 0); // between row 0 and row 1

            Assert.Equal(LineStyle.Solid, resolved.Style);
            Assert.Equal(2, resolved.Width);
        }

        [Fact]
        public async Task SharedEdge_BothSidesDeclare_WiderWins()
        {
            var table = await LayoutTable(@"
                <table style='border-collapse:collapse'>
                    <tr><td style='border-bottom:1pt solid black'>a</td></tr>
                    <tr><td style='border-top:5pt dashed blue'>b</td></tr>
                </table>");

            var resolved = table.CollapsedBorders!.Horizontal(1, 0);

            Assert.Equal(LineStyle.Dashed, resolved.Style);
            Assert.Equal(5, resolved.Width);
        }

        [Fact]
        public async Task SharedEdge_EqualWidth_HigherStylePriorityWins()
        {
            var table = await LayoutTable(@"
                <table style='border-collapse:collapse'>
                    <tr><td style='border-bottom:2pt dotted black'>a</td></tr>
                    <tr><td style='border-top:2pt double black'>b</td></tr>
                </table>");

            var resolved = table.CollapsedBorders!.Horizontal(1, 0);

            Assert.Equal(LineStyle.Double, resolved.Style); // double outranks dotted
        }

        [Fact]
        public async Task Hidden_SuppressesTheSharedEdge_EvenAgainstAWiderSolidBorder()
        {
            var table = await LayoutTable(@"
                <table style='border-collapse:collapse'>
                    <tr><td style='border-bottom:10pt solid black'>a</td></tr>
                    <tr><td style='border-top:hidden'>b</td></tr>
                </table>");

            var resolved = table.CollapsedBorders!.Horizontal(1, 0);

            Assert.Equal(LineStyle.Hidden, resolved.Style);
            Assert.False(resolved.IsPainted);
        }

        [Fact]
        public async Task CellBorder_OutranksRowBorder_AtEqualWidthAndStyle()
        {
            var table = await LayoutTable(@"
                <table style='border-collapse:collapse'>
                    <tr style='border-bottom:3pt solid red'><td style='border-bottom:3pt solid blue'>a</td></tr>
                    <tr><td>b</td></tr>
                </table>");

            var resolved = table.CollapsedBorders!.Horizontal(1, 0);

            // Cell (blue) beats row (red) - both 3pt solid, cell has higher origin priority.
            Assert.Equal(RColorBlue(), resolved.Color);
        }

        private static PeachPDF.Html.Adapters.Entities.RColor RColorBlue() =>
            PeachPDF.Html.Adapters.Entities.RColor.FromArgb(0, 0, 255);

        [Fact]
        public async Task RowBorder_WinsWhenNoCellDeclaresOne()
        {
            var table = await LayoutTable(@"
                <table style='border-collapse:collapse'>
                    <tr style='border-bottom:4pt solid green'><td>a</td></tr>
                    <tr><td>b</td></tr>
                </table>");

            var resolved = table.CollapsedBorders!.Horizontal(1, 0);

            Assert.Equal(4, resolved.Width);
            Assert.True(resolved.IsPainted);
        }

        [Fact]
        public async Task TableBorder_ParticipatesAtTheOuterTopEdge()
        {
            var table = await LayoutTable(@"
                <table style='border-collapse:collapse;border:6pt solid black'>
                    <tr><td>a</td></tr>
                </table>");

            var top = table.CollapsedBorders!.Horizontal(0, 0);
            var bottom = table.CollapsedBorders!.Horizontal(1, 0);
            var left = table.CollapsedBorders!.Vertical(0, 0);
            var right = table.CollapsedBorders!.Vertical(0, 1);

            Assert.Equal(6, top.Width);
            Assert.Equal(6, bottom.Width);
            Assert.Equal(6, left.Width);
            Assert.Equal(6, right.Width);
        }

        [Fact]
        public async Task TableBorder_LosesToAWiderCellBorder_AtTheOuterEdge()
        {
            var table = await LayoutTable(@"
                <table style='border-collapse:collapse;border:1pt solid black'>
                    <tr><td style='border-top:9pt solid red'>a</td></tr>
                </table>");

            var top = table.CollapsedBorders!.Horizontal(0, 0);

            Assert.Equal(9, top.Width);
        }

        [Fact]
        public async Task RowGroupBorder_ParticipatesAtItsOwnBoundary()
        {
            var table = await LayoutTable(@"
                <table style='border-collapse:collapse'>
                    <tbody style='border-bottom:7pt solid black'>
                        <tr><td>a</td></tr>
                    </tbody>
                    <tbody>
                        <tr><td>b</td></tr>
                    </tbody>
                </table>");

            var resolved = table.CollapsedBorders!.Horizontal(1, 0); // boundary between the two tbodies

            Assert.Equal(7, resolved.Width);
        }

        [Fact]
        public async Task NoBordersDeclaredAnywhere_EveryLineResolvesToNone()
        {
            var table = await LayoutTable(@"
                <table style='border-collapse:collapse'>
                    <tr><td>a</td><td>b</td></tr>
                </table>");

            for (var line = 0; line <= table.CollapsedBorderGrid!.RowCount; line++)
            for (var column = 0; column < table.CollapsedBorderGrid!.ColumnCount; column++)
            {
                var resolved = table.CollapsedBorders!.Horizontal(line, column);
                Assert.False(resolved.IsPainted);
            }
        }

        [Fact]
        public async Task Issue735Repro_SharedEdgeBetweenTwoBackgroundColoredRows_ResolvesToAVisibleBorder()
        {
            // The reported bug's own shape: two adjacent rows, each cell colored AND carrying the shared
            // border-bottom - this asserts the RESOLUTION is correct (a real border wins the shared
            // edge); the paint-order fix that makes it actually render is Phase 3.
            var td = "style='background-color:#C00000;border-bottom:solid #000 1pt'";
            var table = await LayoutTable($@"
                <table style='border-collapse:collapse;width:100%'>
                    <tr style='border-bottom:solid #000 1pt'><td {td}>!</td><td style='border-bottom:solid #000 1pt'>Missing Emergency Contact</td></tr>
                    <tr style='border-bottom:solid #000 1pt'><td {td}>!</td><td style='border-bottom:solid #000 1pt'>Missing Emergency Contact</td></tr>
                </table>");

            var sharedEdgeColumn0 = table.CollapsedBorders!.Horizontal(1, 0);
            var sharedEdgeColumn1 = table.CollapsedBorders!.Horizontal(1, 1);

            Assert.True(sharedEdgeColumn0.IsPainted);
            Assert.Equal(LineStyle.Solid, sharedEdgeColumn0.Style);
            Assert.True(sharedEdgeColumn1.IsPainted);
        }

        [Fact]
        public async Task Issue736Repro_RowspanInMultiRowThead_LaterRowCellPlacedInCorrectColumn()
        {
            // https://github.com/jhaygood86/PeachPDF/issues/736: a rowspan cell (A) starting in a
            // detached <thead>'s first row and reaching into its second row left that second row's
            // own remaining cell (D) computed at the wrong starting column - InsertEmptyBoxes pads
            // exactly this kind of gap with a CssSpacingBox placeholder for an ordinary body row,
            // but never touches a detached header/footer group's own rows, so D's naive
            // Boxes-list-summing column index came out one column short (0 instead of 1) and
            // TableGrid.Build's first-writer-wins slot fill silently dropped D from the grid.
            var table = await LayoutTable(@"
                <table style='border-collapse:collapse'>
                    <thead>
                        <tr><th rowspan='2'>A</th><th style='border-bottom:1px solid black'>B</th></tr>
                        <tr><th style='border-bottom:9px solid red'>D</th></tr>
                    </thead>
                    <tbody>
                        <tr><td>x</td><td>y</td></tr>
                    </tbody>
                </table>");

            var grid = table.CollapsedBorderGrid!;
            Assert.Equal(3, grid.RowCount);
            Assert.Equal(2, grid.ColumnCount);

            var a = grid.CellAt(0, 0);
            var b = grid.CellAt(0, 1);
            var d = grid.CellAt(1, 1);

            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.NotNull(d);
            Assert.Same(a, grid.CellAt(1, 0)); // A's rowspan reaches row 1, column 0.
            Assert.NotSame(d, b);

            // D's own border-bottom (9px = 6.75pt) should win the header-to-body boundary at column 1.
            var resolved = table.CollapsedBorders!.Horizontal(2, 1);
            Assert.Equal(6.75, resolved.Width, 2);
        }
    }
}

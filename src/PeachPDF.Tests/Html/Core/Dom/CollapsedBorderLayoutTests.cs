using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// The row/column spacing rework - <c>HorizontalSpacingAt</c>/<c>VerticalSpacingAt</c> replacing the
    /// old flat <c>-1</c> border-collapse constant with CSS 2.1 §17.6.2's actual resolved border width at
    /// each grid line, and (issue #1138) the interior-line cursor advance itself dropping to <c>0</c>:
    /// <c>ApplyCollapsedUsedBorderWidths</c> already gives each side of a shared grid line half the
    /// resolved width as its own used border, so the two neighbors' own box-model insets already sum to
    /// the full width between them - an additional cursor pull there double-counted the one shared
    /// border and made adjacent boxes overlap instead of meeting flush.
    /// </summary>
    public class CollapsedBorderLayoutTests
    {
        private static CssBox FindById(CssBox root, string id) => LayoutHarness.FindById(root, id)!;

        [Fact]
        public async Task AdjacentCells_MeetFlush_RegardlessOfTheResolvedWidth()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse'>
                    <tr><td id='a' style='border-right:3pt solid black'>a</td><td id='b'>b</td></tr>
                </table>"));

            var a = FindById(root, "a");
            var b = FindById(root, "b");

            Assert.Equal(0, b.Location.X - a.ActualRight, 1);
        }

        [Fact]
        public async Task Rows_MeetFlush_RegardlessOfTheResolvedWidth()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse'>
                    <tr><td id='a' style='border-bottom:4pt solid black'>a</td></tr>
                    <tr><td id='b'>b</td></tr>
                </table>"));

            var a = FindById(root, "a");
            var b = FindById(root, "b");

            Assert.Equal(0, b.Location.Y - a.ActualBottom, 1);
        }

        [Fact]
        public async Task RowBorder_DoesNotShiftTheNextRowsColumnBoundaries()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse'>
                    <tr style='border-bottom:6pt solid red'><td id='a'>a</td><td id='b'>b</td></tr>
                    <tr><td id='c'>c</td><td id='d'>d</td></tr>
                </table>"));

            var a = FindById(root, "a");
            var c = FindById(root, "c");

            Assert.Equal(0, c.Location.Y - a.ActualBottom, 1);
        }

        [Fact]
        public async Task MixedWidthBoundary_StillMeetsFlush_UnderTheWiderSegmentsInset()
        {
            // Column 0's boundary resolves to 8pt (a's own border), column 1's to 2pt (c's) - the row
            // below must start clear of the WIDER of the two (both columns share one flat row cursor),
            // but still flush against it, not offset by any further gap.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse'>
                    <tr><td id='a' style='border-bottom:8pt solid black'>a</td><td id='b' style='border-bottom:2pt solid black'>b</td></tr>
                    <tr><td id='c'>c</td><td id='d'>d</td></tr>
                </table>"));

            var a = FindById(root, "a");
            var b = FindById(root, "b");
            var c = FindById(root, "c");
            var d = FindById(root, "d");

            // Both columns' rows start at the SAME Y (a row is one flat cursor position), reserving the
            // wider (8pt) boundary's room even under column 1, whose own resolved border is only 2pt.
            Assert.Equal(c.Location.Y, d.Location.Y, 1);
            Assert.Equal(0, c.Location.Y - a.ActualBottom, 1);
        }

        [Fact]
        public async Task Hidden_RemovesReservedRoomEntirely()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse'>
                    <tr><td id='a' style='border-bottom:10pt solid black'>a</td></tr>
                    <tr><td id='b' style='border-top:hidden'>b</td></tr>
                </table>"));

            var a = FindById(root, "a");
            var b = FindById(root, "b");

            // hidden suppresses the shared edge outright - the cells become flush, not merely narrower.
            Assert.Equal(0, b.Location.Y - a.ActualBottom, 1);
        }

        [Fact]
        public async Task NoBorderAnywhere_CellsAreExactlyFlush()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse'>
                    <tr><td id='a'>a</td></tr>
                    <tr><td id='b'>b</td></tr>
                </table>"));

            var a = FindById(root, "a");
            var b = FindById(root, "b");

            Assert.Equal(0, b.Location.Y - a.ActualBottom, 1);
        }

        [Fact]
        public async Task TableOuterEdges_SitHalfAGridLineOutsideTheFirstCellsOwnEdges()
        {
            // The table's own border box holds the WHOLE outermost grid line and the first cell holds
            // its inner half, so the two edges are exactly half a line apart. Issue #1257: the table's
            // edge used to sit on the line's centre, coinciding with the cell's, which painted the
            // line's outer half outside the table - over whatever preceded it, or clipped away at a page
            // edge. Verified against GetWidthSum's own independently-derived total, not just asserted
            // (see StartXSpacing's remarks in CssLayoutEngineTable).
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table id='t' style='border-collapse:collapse;border:6pt solid black'>
                    <tr><td id='a' style='border-top:6pt solid black;border-left:6pt solid black'>a</td></tr>
                </table>"));

            var table = FindById(root, "t");
            var a = FindById(root, "a");

            Assert.Equal(3, a.Location.Y - table.Location.Y, 1);
            Assert.Equal(3, a.Location.X - table.Location.X, 1);

            // The other half is the used border width the table itself took - the whole 6pt line, not
            // CSS 2.1 §17.6.2's "half of the maximum collapsed border".
            Assert.Equal(6, table.ActualBorderTopWidth, 1);
            Assert.Equal(6, table.ActualBorderLeftWidth, 1);
        }

        [Fact]
        public async Task TableOuterEdge_WonByACellAlone_StillTakesTheWholeLine()
        {
            // The table itself declares no border at all - only the cell touching the outer edge does.
            // Without DerivedStyle.SetCollapsedUsedBorderWidths the table's own (still-computed-from-its-
            // own-declared-style) ActualBorderTopWidth/ActualBorderLeftWidth would stay 0, which is
            // exactly what this asserts is overridden: the table takes the whole resolved line even
            // though it declared none of it.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table id='t' style='border-collapse:collapse'>
                    <tr><td id='a' style='border-top:6pt solid black;border-left:6pt solid black'>a</td></tr>
                </table>"));

            var table = FindById(root, "t");
            var a = FindById(root, "a");

            Assert.Equal(3, a.Location.Y - table.Location.Y, 1);
            Assert.Equal(3, a.Location.X - table.Location.X, 1);

            Assert.Equal(6, table.ActualBorderTopWidth, 1);
            Assert.Equal(6, table.ActualBorderLeftWidth, 1);
        }

        [Fact]
        public async Task TableWidthMatchesColumnWidthsMinusInteriorGaps_NoOuterEdgeResidual()
        {
            // The regression this test was originally written for: a 3-column, uniformly 1px-bordered
            // table's rendered width must equal its columns' own widths exactly (columns meet flush at
            // every interior boundary, contributing no gap - issue #1138), with no leftover half-border
            // residual from the outer edges either, which independently-but-inconsistently computed
            // startX/ActualRight formulas produced (200.375 instead of 200.000, for the fixture this
            // mirrors) before StartXSpacing/StartYSpacing accounted for ClientLeft/ClientTop already
            // including the table's own outer used-border-width.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table id='t' style='border-collapse:collapse;width:200pt'>
                    <tr>
                        <td id='a' style='border:1px solid black'>a</td>
                        <td id='b' style='border:1px solid black'>b</td>
                        <td id='c' style='border:1px solid black'>c</td>
                    </tr>
                </table>"));

            var table = FindById(root, "t");

            Assert.Equal(200, table.ActualRight - table.Location.X, 1);
        }

        [Fact]
        public async Task ClientTop_ReflectsTheCellsOwnUsedBorderWidth_NotItsDeclaredOne()
        {
            // The cell's own border-top is 10pt, but half the RESOLVED width (also 10pt here, since
            // nothing else contests this edge) is 5pt - content must be inset by the used (halved) value.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse'>
                    <tr><td id='a' style='border-top:10pt solid black;padding-top:2pt'>a</td></tr>
                </table>"));

            var a = FindById(root, "a");

            Assert.Equal(5 + 2, a.ClientTop - a.Location.Y, 1);
        }

        [Fact]
        public async Task SeparateTable_StillUsesRealBorderSpacing_Unaffected()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table style='border-collapse:separate;border-spacing:5pt'>
                    <tr><td id='a'>a</td><td id='b'>b</td></tr>
                </table>"));

            var a = FindById(root, "a");
            var b = FindById(root, "b");

            Assert.Equal(5, b.Location.X - a.ActualRight, 1);
        }

        [Fact]
        public async Task Colspan_InteriorBoundary_ContributesNoSpacing()
        {
            // The line "inside" a colspan cell's own span is None (CollapsedBorderModel never registers
            // a candidate there), so the cell measures as one continuous span with no internal gap.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse;width:400pt'>
                    <tr><td id='wide' colspan='2'>wide</td></tr>
                    <tr><td id='c'>c</td><td id='d'>d</td></tr>
                </table>"));

            var wide = FindById(root, "wide");
            var d = FindById(root, "d");

            // The wide cell's own right edge reaches at least as far as column 2's own right edge (d's) -
            // no interior gap was subtracted out of its span.
            Assert.True(wide.ActualRight >= d.ActualRight - 1);
        }

        [Fact]
        public async Task MultiRowTable_RowHeightsSumExactly_NoCompoundingDrift()
        {
            // Issue #1138: before the fix, every interior row boundary lost a full border width from the
            // table's total height, and the error compounded once per row - three rows of a fixed height
            // plus a uniform border landed the third row short by two whole border widths, not one.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table id='t' style='border-collapse:collapse'>
                    <tr id='row1'><td style='border:1px solid #333;height:20pt'>row 1</td></tr>
                    <tr id='row2'><td style='border:1px solid #333;height:20pt'>row 2</td></tr>
                    <tr id='row3'><td style='border:1px solid #333;height:20pt'>row 3</td></tr>
                </table>"));

            var table = FindById(root, "t");
            var row1 = FindById(root, "row1");
            var row2 = FindById(root, "row2");
            var row3 = FindById(root, "row3");

            var rowHeight = row1.ActualBottom - row1.Location.Y;

            Assert.Equal(rowHeight, row2.ActualBottom - row2.Location.Y, 1);
            Assert.Equal(rowHeight, row3.ActualBottom - row3.Location.Y, 1);

            Assert.Equal(row1.ActualBottom, row2.Location.Y, 1);
            Assert.Equal(row2.ActualBottom, row3.Location.Y, 1);

            // The rows meet centre-to-centre and so sum to their own three heights exactly; the table's
            // border box then adds the outer half of the outermost grid line at each end, which is the
            // whole of what separates its own edge from the first row's (issue #1257). A 1px line is
            // 0.75pt, so each end adds 0.375pt.
            var halfOuterLine = row1.Location.Y - table.Location.Y;
            Assert.Equal(0.375, halfOuterLine, 3);

            Assert.Equal(3 * rowHeight + 2 * halfOuterLine, table.ActualBottom - table.Location.Y, 1);
        }

        [Fact]
        public async Task MultiColumnTable_ColumnWidthsSumExactly_NoCompoundingDrift()
        {
            // The column-axis twin of MultiRowTable_RowHeightsSumExactly_NoCompoundingDrift - the same
            // double-count affected HorizontalSpacingAt identically.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table id='t' style='border-collapse:collapse'>
                    <tr>
                        <td id='col1' style='border:1px solid #333;width:20pt'>1</td>
                        <td id='col2' style='border:1px solid #333;width:20pt'>2</td>
                        <td id='col3' style='border:1px solid #333;width:20pt'>3</td>
                    </tr>
                </table>"));

            var table = FindById(root, "t");
            var col1 = FindById(root, "col1");
            var col2 = FindById(root, "col2");
            var col3 = FindById(root, "col3");

            var colWidth = col1.ActualRight - col1.Location.X;

            Assert.Equal(colWidth, col2.ActualRight - col2.Location.X, 1);
            Assert.Equal(colWidth, col3.ActualRight - col3.Location.X, 1);

            Assert.Equal(col1.ActualRight, col2.Location.X, 1);
            Assert.Equal(col2.ActualRight, col3.Location.X, 1);

            // See MultiRowTable_RowHeightsSumExactly_NoCompoundingDrift's own remark for the two outer
            // half-lines - the same term, on the column axis.
            var halfOuterLine = col1.Location.X - table.Location.X;
            Assert.Equal(0.375, halfOuterLine, 3);

            Assert.Equal(3 * colWidth + 2 * halfOuterLine, table.ActualRight - table.Location.X, 1);
        }
    }
}

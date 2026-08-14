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
    /// each grid line.
    /// </summary>
    public class CollapsedBorderLayoutTests
    {
        private static CssBox FindById(CssBox root, string id) => LayoutHarness.FindById(root, id)!;

        [Fact]
        public async Task AdjacentCells_OverlapByTheResolvedWidth_NotAFlatOne()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse'>
                    <tr><td id='a' style='border-right:3pt solid black'>a</td><td id='b'>b</td></tr>
                </table>"));

            var a = FindById(root, "a");
            var b = FindById(root, "b");

            Assert.Equal(-3, b.Location.X - a.ActualRight, 1);
        }

        [Fact]
        public async Task Rows_OverlapVerticallyByTheResolvedWidth()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse'>
                    <tr><td id='a' style='border-bottom:4pt solid black'>a</td></tr>
                    <tr><td id='b'>b</td></tr>
                </table>"));

            var a = FindById(root, "a");
            var b = FindById(root, "b");

            Assert.Equal(-4, b.Location.Y - a.ActualBottom, 1);
        }

        [Fact]
        public async Task RowBorder_WidensEveryColumnBoundaryOnThatLine()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse'>
                    <tr style='border-bottom:6pt solid red'><td id='a'>a</td><td id='b'>b</td></tr>
                    <tr><td id='c'>c</td><td id='d'>d</td></tr>
                </table>"));

            var a = FindById(root, "a");
            var c = FindById(root, "c");

            Assert.Equal(-6, c.Location.Y - a.ActualBottom, 1);
        }

        [Fact]
        public async Task MixedWidthBoundary_ReservesTheMaxAcrossItsSegments()
        {
            // Column 0's boundary resolves to 8pt (a's own border), column 1's to 2pt (c's) - the row
            // below must start clear of the WIDER of the two, even though only one column's border is
            // actually that wide.
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
            Assert.Equal(-8, c.Location.Y - a.ActualBottom, 1);
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
        public async Task TableOuterEdges_CoincideWithTheFirstCellsOwnEdges()
        {
            // Per CollapsedBorderModel's own geometric model, the table's own border box and the first
            // cell's own border box are the SAME edge (X_0 - VW[0]/2 both), not two edges VW[0]/2 apart -
            // the shared outer border is one physical sliver of space, viewed from the table's side and
            // the cell's side at once, not two separate reservations stacked. Verified against
            // GetWidthSum's own independently-derived total, not just asserted (see StartXSpacing's
            // remarks in CssLayoutEngineTable for the exact numeric residual this caught).
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table id='t' style='border-collapse:collapse;border:6pt solid black'>
                    <tr><td id='a' style='border-top:6pt solid black;border-left:6pt solid black'>a</td></tr>
                </table>"));

            var table = FindById(root, "t");
            var a = FindById(root, "a");

            Assert.Equal(0, a.Location.Y - table.Location.Y, 1);
            Assert.Equal(0, a.Location.X - table.Location.X, 1);
        }

        [Fact]
        public async Task TableOuterEdge_WonByACellAlone_StillCoincides()
        {
            // The table itself declares no border at all - only the cell touching the outer edge does.
            // Without DerivedStyle.SetCollapsedUsedBorderWidths, the table's own (still-computed-from-its-
            // own-declared-style) ActualBorderTopWidth/ActualBorderLeftWidth would stay 0, which happens
            // to already coincide by accident here - the real point of this test is
            // TableWidthMatchesGetWidthSum below, which fails without the override regardless of which
            // box's declared border happens to be nonzero.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table id='t' style='border-collapse:collapse'>
                    <tr><td id='a' style='border-top:6pt solid black;border-left:6pt solid black'>a</td></tr>
                </table>"));

            var table = FindById(root, "t");
            var a = FindById(root, "a");

            Assert.Equal(0, a.Location.Y - table.Location.Y, 1);
            Assert.Equal(0, a.Location.X - table.Location.X, 1);
        }

        [Fact]
        public async Task TableWidthMatchesColumnWidthsMinusInteriorGaps_NoOuterEdgeResidual()
        {
            // The regression this whole fix was for: a 3-column, uniformly 1px-bordered table's rendered
            // width must equal its columns' own widths minus the (two) interior overlaps - with no
            // leftover half-border residual from the outer edges, which independently-but-inconsistently
            // computed startX/ActualRight formulas produced (200.375 instead of 200.000, for the fixture
            // this mirrors) before StartXSpacing/StartYSpacing accounted for ClientLeft/ClientTop already
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
    }
}

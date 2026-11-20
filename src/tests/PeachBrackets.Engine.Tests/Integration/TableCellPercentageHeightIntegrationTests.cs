namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// css-tables-3's percentage-height rules for a table cell: an indefinite percentage still leaves
/// <c>min-height</c> able to clamp the used height (wpt css-tables/height-distribution/
/// percentage-sizing-of-table-cell-children-003/006), and a cell's own percentage <c>height</c> is
/// treated as auto when its row sizes itself, rather than resolved against the table (wpt
/// css-tables/table-as-item-cell-percentage-001..004).
/// </summary>
public class TableCellPercentageHeightIntegrationTests
{
    [Fact]
    public async Task AnIndefinitePercentageHeightChild_StillHonorsItsOwnMinHeight()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                "<div style='display:table'><div id='cell' style='display:table-cell'>" +
                "<div id='child' style='height:100%;min-height:40pt'></div></div></div>"),
            pageWidth: 600, pageHeight: 600);
        var child = LayoutHarness.FindById(root, "child")!;

        Assert.Equal(40, child.ActualBottom - child.Location.Y, 1);
    }

    [Fact]
    public async Task AnIndefinitePercentageHeightChild_WithNoMinHeight_StaysAtItsContentHeight()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                "<div style='display:table'><div id='cell' style='display:table-cell'>" +
                "<div id='child' style='height:100%'><div style='height:15pt'></div></div></div></div>"),
            pageWidth: 600, pageHeight: 600);
        var child = LayoutHarness.FindById(root, "child")!;

        Assert.Equal(15, child.ActualBottom - child.Location.Y, 1);
    }

    [Fact]
    public async Task ACellsOwnPercentageHeight_DoesNotInflateTheRowPastItsSiblingsNaturalHeight()
    {
        // Two rows of 50pt each (100pt total); the lone cell's own `height: 100%` must act as auto for
        // row sizing rather than resolve against a larger, already-grown table height, which would
        // otherwise double (or keep compounding) the row's real height every time the table re-grew to
        // fit the last pass's inflated cell.
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                "<table id='t' style='border-spacing:0;width:100pt'>" +
                "<tr style='height:50pt'><td></td></tr>" +
                "<tr style='height:50pt'><td id='cell' style='height:100%'></td></tr></table>"),
            pageWidth: 600, pageHeight: 600);
        var t = LayoutHarness.FindById(root, "t")!;
        var cell = LayoutHarness.FindById(root, "cell")!;

        Assert.Equal(100, t.ActualBottom - t.Location.Y, 1);
        Assert.Equal(50, cell.ActualBottom - cell.Location.Y, 1);
    }

    private static async Task<double[]> RowHeightsAsync(string cell1, string cell2, string cell3, string tableStyle = "height:300pt")
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                $"<style>td{{padding:0}}</style><table id='t' style='border-spacing:0;width:100pt;{tableStyle}'>" +
                $"<tr id='r1'><td {cell1}><div style='height:50pt'></div></td></tr>" +
                $"<tr id='r2'><td {cell2}><div style='height:50pt'></div></td></tr>" +
                $"<tr id='r3'><td {cell3}><div style='height:50pt'></div></td></tr></table>"),
            pageWidth: 600, pageHeight: 600);
        return new[] { "r1", "r2", "r3" }
            .Select(id => LayoutHarness.FindById(root, id)!)
            .Select(r => Math.Round(r.ActualBottom - r.Location.Y))
            .ToArray();
    }

    [Fact]
    public async Task APercentageCell_AtOneHundredPercent_TakesTheHeightTheOtherRowsLeave()
    {
        var h = await RowHeightsAsync("", "style='height:100%'", "");
        Assert.Equal(new[] { 50d, 200d, 50d }, h);
    }

    [Fact]
    public async Task APercentageCell_AtFiftyPercent_TakesItsShareAndTheRestIsSpread()
    {
        var h = await RowHeightsAsync("", "style='height:50%'", "");
        Assert.Equal(new[] { 75d, 150d, 75d }, h);
    }

    [Fact]
    public async Task TwoPercentageCells_AreServedInOrderAndCapped()
    {
        var h = await RowHeightsAsync("style='height:100%'", "style='height:100%'", "");
        Assert.Equal(new[] { 200d, 50d, 50d }, h);
    }

    [Fact]
    public async Task APercentageCell_InAnAutoHeightTable_StaysAtContentHeight()
    {
        var h = await RowHeightsAsync("", "style='height:100%'", "", tableStyle: "");
        Assert.Equal(new[] { 50d, 50d, 50d }, h);
    }

    [Fact]
    public async Task ACellsOwnPercentageHeight_ActsAsAutoEvenAgainstAnExplicitlyTallTable()
    {
        // The table's own height is a genuinely definite, non-circular 100pt, exactly matching its two
        // rows' natural 50pt-each total - so css-tables-3's extra-height distribution has nothing left to
        // give either row. The second row's cell's own `height: 100%` must still act as auto for its own
        // sizing, not resolve directly against the table's 100pt and inflate its row to 100pt on top of
        // the first row's own natural 50pt (which would grow the table to 150pt).
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                "<table id='t' style='border-spacing:0;width:100pt;height:100pt'>" +
                "<tr style='height:50pt'><td></td></tr>" +
                "<tr style='height:50pt'><td id='cell' style='height:100%'></td></tr></table>"),
            pageWidth: 600, pageHeight: 600);
        var t = LayoutHarness.FindById(root, "t")!;
        var cell = LayoutHarness.FindById(root, "cell")!;

        Assert.Equal(100, t.ActualBottom - t.Location.Y, 1);
        Assert.Equal(50, cell.ActualBottom - cell.Location.Y, 1);
    }
}

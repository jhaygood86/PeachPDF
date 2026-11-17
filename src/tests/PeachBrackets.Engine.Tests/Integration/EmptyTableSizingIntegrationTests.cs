namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// CSS 2.1 section 17.4: an empty table (no rows, no columns) still gets its own specified <c>width</c> -
/// it has no column content to size from, but that is no reason to collapse to zero (wpt css2/absolute-tables,
/// css2/CSS2/tables/collapsing-border-model-011/012, css-tables/subpixel-table-width-001).
/// </summary>
public class EmptyTableSizingIntegrationTests
{
    [Fact]
    public async Task AnEmptyTableWithAWidth_KeepsIt()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap("<table id='t' style='width:150pt'></table>"), pageWidth: 600, pageHeight: 600);
        var t = LayoutHarness.FindById(root, "t")!;

        Assert.Equal(150, t.ActualRight - t.Location.X, 1);
    }

    [Fact]
    public async Task AnAbsolutelyPositionedEmptyTable_ResolvesAPercentageWidthAgainstItsPositionedAncestor()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                "<div style='position:relative;width:200pt'>" +
                "<div id='t' style='position:absolute;display:table;width:50%;height:50pt'></div>" +
                "</div>"),
            pageWidth: 600, pageHeight: 600);
        var t = LayoutHarness.FindById(root, "t")!;

        Assert.Equal(100, t.ActualRight - t.Location.X, 1);
    }

    [Fact]
    public async Task ANonEmptyTableWithAWidthNarrowerThanItsPadding_IsUnaffectedByTheEmptyTableFloor()
    {
        // Regression guard: GetWidthSum's empty-table floor must only kick in when the table has
        // literally no columns (_columnWidths.Length == 0) - a table with a real cell must still size
        // from that cell's content, not get bumped up to GetAvailableTableWidth()'s resolved `width`
        // a second time on top of its own column-width-driven total.
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap("<table id='t' style='width:10pt'><tr><td>a longer cell</td></tr></table>"),
            pageWidth: 600, pageHeight: 600);
        var t = LayoutHarness.FindById(root, "t")!;

        // The cell's own min-content width governs, well past the too-small specified width - and well
        // past 2x that width, which is what a double-counted GetAvailableTableWidth() would produce.
        Assert.True(t.ActualRight - t.Location.X > 20);
    }
}

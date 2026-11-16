namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// css-tables-3's own table-height algorithm has no shrinking step (CSS 2.1 section 17.5.3's height is
/// always at least the row grid's natural extent) - <c>max-height</c> cannot cut into that, the same way
/// it has no effect on a table cell (wpt css-tables/max-height-table).
/// </summary>
public class TableMaxHeightIntegrationTests
{
    [Fact]
    public async Task AMaxHeightSmallerThanContent_DoesNotShrinkATable()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                "<table id='t' style='max-height:0;border-spacing:0'><tr><td style='padding:0'>" +
                "<div style='width:5pt;height:5pt'></div></td></tr></table>"),
            pageWidth: 600, pageHeight: 600);
        var t = LayoutHarness.FindById(root, "t")!;

        Assert.True(t.ActualBottom - t.Location.Y >= 5);
    }

    [Fact]
    public async Task AMaxHeightSmallerThanAnExplicitHeight_DoesNotShrinkATable()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                "<table id='t' style='height:80pt;max-height:20pt;border-spacing:0'><tr><td>a</td></tr></table>"),
            pageWidth: 600, pageHeight: 600);
        var t = LayoutHarness.FindById(root, "t")!;

        Assert.Equal(80, t.ActualBottom - t.Location.Y, 1);
    }
}

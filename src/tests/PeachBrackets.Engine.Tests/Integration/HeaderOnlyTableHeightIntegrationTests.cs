namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// CSS 2.1 section 17.5.3: a table's own explicit <c>height</c> exceeding its rows' natural total is
/// redistributed into the rows, raising each one's floor by its own proportional share - but that
/// redistribution is keyed to <c>_bodyRows</c>, so a table with only a <c>thead</c>/<c>tfoot</c> and no
/// <c>tbody</c> (wpt css-tables/tentative/section-no-tbody-*-distribution) had nowhere to put the
/// surplus and stayed at its rows' natural (too-short) height.
/// </summary>
public class HeaderOnlyTableHeightIntegrationTests
{
    [Fact]
    public async Task AHeaderOnlyTable_GrowsItsHeaderToFillAnExplicitHeight()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                "<table id='t' style='width:100pt;height:100pt;border-spacing:0'>" +
                "<thead id='thead' style='height:20pt'><tr><td>a</td></tr></thead>" +
                "</table>"),
            pageWidth: 600, pageHeight: 600);
        var t = LayoutHarness.FindById(root, "t")!;
        var thead = LayoutHarness.FindById(root, "thead")!;

        Assert.Equal(100, t.ActualBottom - t.Location.Y, 1);
        Assert.Equal(100, thead.ActualBottom - thead.Location.Y, 1);
    }

    [Fact]
    public async Task AHeaderAndFooterOnlyTable_SplitsTheSurplusBetweenThem()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                "<table id='t' style='width:100pt;height:100pt;border-spacing:0'>" +
                "<thead id='thead'><tr><td>a</td></tr></thead>" +
                "<tfoot id='tfoot'><tr><td>b</td></tr></tfoot>" +
                "</table>"),
            pageWidth: 600, pageHeight: 600);
        var t = LayoutHarness.FindById(root, "t")!;
        var thead = LayoutHarness.FindById(root, "thead")!;
        var tfoot = LayoutHarness.FindById(root, "tfoot")!;

        Assert.Equal(100, t.ActualBottom - t.Location.Y, 1);
        var combinedHeight = (thead.ActualBottom - thead.Location.Y) + (tfoot.ActualBottom - tfoot.Location.Y);
        Assert.Equal(100, combinedHeight, 1);
    }
}

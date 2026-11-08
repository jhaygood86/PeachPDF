using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// A table whose box starts left of the page (a negative margin) has its cells inside the box (CSS 2.1 section 8.3): the column axis does not
/// start at the page's left edge.
/// </summary>
public class TableNegativeMarginTests
{
    [Fact]
    public async Task CellsOfATableWithANegativeMarginLeft_StartInsideTheTableBox()
    {
        var html = LayoutHarness.Wrap(
            "<table id='t' cellspacing='0' cellpadding='0' style='margin-left:-100pt;width:300pt'><tr>" +
            "<td id='a'>t1</td><td id='b'>t2</td></tr></table>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);
        var table = LayoutHarness.FindById(root, "t")!;
        var a = LayoutHarness.FindById(root, "a")!;
        var b = LayoutHarness.FindById(root, "b")!;

        Assert.True(table.Location.X < 0, $"the table is at {table.Location.X}");
        Assert.Equal(table.Location.X, a.Location.X, 1);
        Assert.Equal(a.ActualRight, b.Location.X, 1);
        Assert.Equal(table.Location.X + 300, b.ActualRight, 1);
    }

    [Fact]
    public async Task CellsOfATableWithNoMargin_StillStartAtThePageEdge()
    {
        var html = LayoutHarness.Wrap(
            "<table id='t' cellspacing='0' cellpadding='0' style='margin:0;width:300pt'><tr><td id='a'>t1</td></tr></table>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        Assert.Equal(LayoutHarness.FindById(root, "t")!.Location.X, LayoutHarness.FindById(root, "a")!.Location.X, 1);
    }
}

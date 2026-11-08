using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// A table in normal flow establishes a block formatting context: its border box does not overlap the margin box of a preceding float (CSS 2.1
/// section 9.5), it is placed beside the float when it fits.
/// </summary>
public class TableBesideFloatTests
{
    [Theory]
    [InlineData("<table id='f' align='left' width='150' cellspacing='0' cellpadding='0'><tr><td>float</td></tr></table>")]
    [InlineData("<table id='f' style='float:left;width:150pt' cellspacing='0' cellpadding='0'><tr><td>float</td></tr></table>")]
    public async Task BlockTableAfterAFloat_IsPlacedBesideIt(string floated)
    {
        var html = LayoutHarness.Wrap(
            floated + "<table id='t' width='200' cellspacing='0' cellpadding='0'><tr><td id='c'>block</td></tr></table>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);
        var f = LayoutHarness.FindById(root, "f")!;
        var t = LayoutHarness.FindById(root, "t")!;
        var c = LayoutHarness.FindById(root, "c")!;

        Assert.True(t.Location.X >= f.ActualRight - 0.5, $"the block table is at {t.Location.X}, the float ends at {f.ActualRight}");
        Assert.Equal(f.Location.Y, t.Location.Y, 1);
        Assert.Equal(t.Location.X, c.Location.X, 1);
    }

    [Fact]
    public async Task BlockTableWithNoFloatBefore_StaysAtTheStartEdge()
    {
        var html = LayoutHarness.Wrap("<table id='t' width='200' cellspacing='0' cellpadding='0'><tr><td>block</td></tr></table>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);
        var t = LayoutHarness.FindById(root, "t")!;

        Assert.Equal(t.ContainingBlock.ClientLeft, t.Location.X, 1);
    }

    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    public async Task BlockTableTooWideToFitBesideAFloat_IsPlacedBelowIt(string side)
    {
        // The page is 595pt wide (the harness default); 400pt beside a 300pt float leaves too little room.
        var html = LayoutHarness.Wrap(
            "<div id='f' style='float:" + side + ";width:300pt;height:50pt'></div>" +
            "<table id='t' width='400' cellspacing='0' cellpadding='0'><tr><td>block</td></tr></table>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageWidth: 595, pageHeight: 600);
        var f = LayoutHarness.FindById(root, "f")!;
        var t = LayoutHarness.FindById(root, "t")!;

        Assert.True(t.Location.Y >= f.ActualBottom - 0.5, $"the table is at {t.Location.Y}, the float ends at {f.ActualBottom}");
        Assert.Equal(t.ContainingBlock.ClientLeft, t.Location.X, 1);
    }

    [Fact]
    public async Task BlockTableThatFitsBesideARightFloat_StaysAtTheStartEdgeOnTheFloatsLine()
    {
        var html = LayoutHarness.Wrap(
            "<div id='f' style='float:right;width:100pt;height:50pt'></div>" +
            "<table id='t' width='200' cellspacing='0' cellpadding='0'><tr><td>block</td></tr></table>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageWidth: 595, pageHeight: 600);
        var f = LayoutHarness.FindById(root, "f")!;
        var t = LayoutHarness.FindById(root, "t")!;

        Assert.Equal(f.Location.Y, t.Location.Y, 1);
        Assert.Equal(t.ContainingBlock.ClientLeft, t.Location.X, 1);
    }
}

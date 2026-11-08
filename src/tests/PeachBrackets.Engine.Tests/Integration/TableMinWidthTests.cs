using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>A table is at least as wide as its <c>min-width</c> (CSS 2.1 section 10.4), and a centred one is centred at that width.</summary>
public class TableMinWidthTests
{
    [Theory]
    [InlineData("")]
    [InlineData("margin:0 auto;")]
    [InlineData("table-layout:fixed;")]
    public async Task ATableNarrowerThanItsMinWidth_IsGrownToIt(string extra)
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap("<table id='t' style='min-width:300pt;" + extra + "'><tr><td>a</td><td>b</td></tr></table>"), pageWidth: 600, pageHeight: 600);
        var t = LayoutHarness.FindById(root, "t")!;

        Assert.Equal(300, t.ActualRight - t.Location.X, 1);
    }

    [Fact]
    public async Task ACentredTableWithAMinWidth_IsCentredAtTheMinimum()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap("<table id='t' style='min-width:300pt;margin:0 auto'><tr><td>a</td></tr></table>"), pageWidth: 600, pageHeight: 600);
        var t = LayoutHarness.FindById(root, "t")!;
        var parent = t.ParentBox!;

        Assert.Equal(t.Location.X - parent.ClientLeft, parent.ClientRight - t.ActualRight, 1);
        Assert.True(t.Location.X > parent.ClientLeft + 1);
    }

    [Fact]
    public async Task AMinWidthBelowTheContentChangesNothing()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap("<table id='t' style='min-width:1pt'><tr><td>a longer cell text</td></tr></table>"), pageWidth: 600, pageHeight: 600);
        var t = LayoutHarness.FindById(root, "t")!;

        Assert.True(t.ActualRight - t.Location.X > 20);
    }
}

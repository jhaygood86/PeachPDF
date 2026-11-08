using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// A block-level table in a <c>&lt;center&gt;</c> or a <c>align="center"</c> parent is centred as a box (HTML Standard rendering section,
/// <c>-webkit-center</c>), as HTML e-mail relies on.
/// </summary>
public class TableInLegacyCenterTests
{
    private const string Table = "<table id='t' width='200' cellspacing='0' cellpadding='0'><tr><td>x</td></tr></table>";

    [Theory]
    [InlineData("<center>" + Table + "</center>")]
    [InlineData("<div align='center'>" + Table + "</div>")]
    [InlineData("<table width='100%'><tr><td align='center'>" + Table + "</td></tr></table>")]
    public async Task Table_IsCentredInItsContainingBlock(string markup)
    {
        var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(markup), pageHeight: 600);
        var t = LayoutHarness.FindById(root, "t")!;
        var parent = t.ParentBox!;
        var left = t.Location.X - parent.ClientLeft;
        var right = parent.ClientRight - t.ActualRight;

        Assert.True(left > 1, $"left gap {left}");
        Assert.Equal(left, right, 1);
    }

    [Fact]
    public async Task TableInADivWithoutAlign_StaysAtTheStartEdge()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap("<div>" + Table + "</div>"), pageHeight: 600);
        var t = LayoutHarness.FindById(root, "t")!;

        Assert.Equal(t.ParentBox!.ClientLeft, t.Location.X, 1);
    }

    [Fact]
    public async Task AuthorMarginBeatsTheLegacyHint()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap("<center><table id='t' width='200' style='margin:0'><tr><td>x</td></tr></table></center>"), pageHeight: 600);
        var t = LayoutHarness.FindById(root, "t")!;

        Assert.Equal(t.ParentBox!.ClientLeft, t.Location.X, 1);
    }
}

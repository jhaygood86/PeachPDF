using PeachBrackets.Html.Dom;
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

/// <summary>A table does not inherit a legacy centre alignment: the text in its cells stays at the start edge, as in a browser.</summary>
public class TableDoesNotInheritLegacyAlignTests
{
    private const string Table = "<table width='300' cellspacing='0' cellpadding='0'><tr><td id='c'>word</td></tr></table>";

    private static async Task<double> FirstWordXInCellAsync(string markup)
    {
        var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(markup), pageHeight: 600);
        var cell = LayoutHarness.FindById(root, "c")!;
        var table = cell.ParentBox!.ParentBox!;
        static IEnumerable<CssRect> WordsOf(CssBox box) => box.Words.Concat(box.Boxes.SelectMany(WordsOf));
        return WordsOf(cell).First().Left - table.Location.X;
    }

    [Theory]
    [InlineData("<td align='center'>")]
    public async Task TableInATdWithAlignCenter_KeepsItsCellTextAtTheStart(string open)
    {
        var x = await FirstWordXInCellAsync("<table width='600'><tr>" + open + Table + "</td></tr></table>");
        Assert.True(x < 5, $"{x}");
    }

    [Fact]
    public async Task TableInACenterElement_KeepsItsCellTextAtTheStart()
    {
        var x = await FirstWordXInCellAsync("<center>" + Table + "</center>");
        Assert.True(x < 5, $"{x}");
    }

    [Fact]
    public async Task TextAlignFromAStyleRuleIsStillInherited()
    {
        var x = await FirstWordXInCellAsync("<div style='text-align:center'>" + Table + "</div>");
        Assert.True(x > 50, $"{x}");
    }
}

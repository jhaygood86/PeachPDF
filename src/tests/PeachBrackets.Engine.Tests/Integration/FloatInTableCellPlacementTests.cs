using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>A float that is all a table cell holds is at the top of the cell's content box, whichever way the cell aligns its content.</summary>
public class FloatInTableCellPlacementTests
{
    [Theory]
    [InlineData("")]
    [InlineData("valign='top'")]
    [InlineData("valign='bottom'")]
    public async Task ALeftFloatedTableAlone_SitsAtTheTopOfTheCell(string valign)
    {
        var html = "<table cellspacing='0' cellpadding='0' border='0'><tr><td id='c' " + valign + ">\n    " +
                   "<table id='f' align='left' cellspacing='0' cellpadding='0'><tr><td>x</td></tr></table>\n  </td></tr></table>";
        var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html), pageHeight: 600);
        var cell = LayoutHarness.FindById(root, "c")!;
        var f = LayoutHarness.FindById(root, "f")!;

        Assert.Equal(cell.ClientTop, f.Location.Y - f.ActualMarginTop, 2);
    }
}

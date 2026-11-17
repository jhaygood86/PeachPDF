namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// HTML Standard, processing model for tables: a cell's <c>rowspan="0"</c> spans every remaining row of
/// its row group, not literally zero rows (wpt css-tables/zero-rowspan-001/002).
/// </summary>
public class ZeroRowspanIntegrationTests
{
    [Fact]
    public async Task ARowspanOfZero_SpansToTheLastRowOfTheTable()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                "<table id='t'>" +
                "<tr><td id='c' rowspan='0'>Foo</td></tr>" +
                "<tr><td>Foo</td></tr>" +
                "<tr><td>Foo</td></tr>" +
                "<tr id='lastRow'><td>Foo</td></tr>" +
                "</table>"),
            pageWidth: 600, pageHeight: 600);
        var lastRow = LayoutHarness.FindById(root, "lastRow")!;
        var c = LayoutHarness.FindById(root, "c")!;

        Assert.Equal(lastRow.ActualBottom, c.ActualBottom, 1);
    }

    [Fact]
    public async Task ARowspanOfZeroInATbody_DoesNotCrossIntoALaterTbody()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                "<table id='t'>" +
                "<tbody id='group1'>" +
                "<tr><td id='c' rowspan='0'>Foo</td></tr>" +
                "<tr><td>Foo</td></tr>" +
                "</tbody>" +
                "<tbody id='group2'>" +
                "<tr><td>Bar</td></tr>" +
                "</tbody>" +
                "</table>"),
            pageWidth: 600, pageHeight: 600);
        var group1 = LayoutHarness.FindById(root, "group1")!;
        var c = LayoutHarness.FindById(root, "c")!;

        Assert.Equal(group1.ActualBottom, c.ActualBottom, 1);
    }
}

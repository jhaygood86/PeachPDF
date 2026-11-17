using PeachBrackets.Html.Dom;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// CSS 2.1 section 17.6: "Rows, columns, row groups, and column groups cannot have borders... user
/// agents must ignore the 'border' properties for those elements" - in EITHER border-collapse model, not
/// only the collapsed one. A row or row group used to share the collapse-mode-gated suppression flag
/// cells and the table box use, so it painted its own border normally whenever the table was
/// <c>border-collapse: separate</c> (wpt css-tables/row-margin-border-padding,
/// row-group-margin-border-padding).
/// </summary>
public class TableStructuralBorderSuppressionTests
{
    [Theory]
    [InlineData("separate")]
    [InlineData("collapse")]
    public async Task ARowsOwnBorder_IsAlwaysSuppressed(string borderCollapse)
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                $"<table style='border-collapse:{borderCollapse}'>" +
                "<tr id='row' style='border:20px solid black'><td>a</td><td>b</td></tr>" +
                "</table>"),
            pageWidth: 600, pageHeight: 600);
        var row = LayoutHarness.FindById(root, "row")!;

        Assert.Equal(BorderEdges.All, row.SuppressedBorderEdges);
    }

    [Theory]
    [InlineData("separate")]
    [InlineData("collapse")]
    public async Task ARowGroupsOwnBorder_IsAlwaysSuppressed(string borderCollapse)
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                $"<table style='border-collapse:{borderCollapse}'>" +
                "<tbody id='group' style='border:20px solid black'><tr><td>a</td></tr></tbody>" +
                "</table>"),
            pageWidth: 600, pageHeight: 600);
        var group = LayoutHarness.FindById(root, "group")!;

        Assert.Equal(BorderEdges.All, group.SuppressedBorderEdges);
    }

    [Fact]
    public async Task ACellsOwnBorder_PaintsNormallyInTheSeparatedModel()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                "<table style='border-collapse:separate'><tr><td id='cell' style='border:1px solid'>a</td></tr></table>"),
            pageWidth: 600, pageHeight: 600);
        var cell = LayoutHarness.FindById(root, "cell")!;

        Assert.Equal(BorderEdges.None, cell.SuppressedBorderEdges);
    }

    [Fact]
    public async Task ACellsOwnBorder_IsSuppressedInTheCollapsedModel()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                "<table style='border-collapse:collapse'><tr><td id='cell' style='border:1px solid'>a</td></tr></table>"),
            pageWidth: 600, pageHeight: 600);
        var cell = LayoutHarness.FindById(root, "cell")!;

        Assert.Equal(BorderEdges.All, cell.SuppressedBorderEdges);
    }
}

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A column with <c>visibility: collapse</c> is removed from the table (CSS 2.1 §17.5.5), so none of its cells paint. A cell does
/// not inherit its column's visibility, and a cell the engine gives no width still drew its text, over the cell that closed the gap.
/// </summary>
public class CollapsedColumnPaintTests
{
    private static string Document(string columns, string rows) =>
        "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 200pt; margin: 20pt } " +
        "body { margin:0; font: 10pt/12pt Arial } table { border-collapse: separate; border-spacing: 4pt 0 } " +
        "td, th { padding: 4pt 8pt } col.collapsed { visibility: collapse }</style></head><body>" +
        $"<table><colgroup>{columns}</colgroup>{rows}</table></body></html>";

    [Fact]
    public async Task CellsOfACollapsedColumn_PaintNothing()
    {
        var html = Document(
            "<col style='width:60pt'><col class='collapsed' style='width:60pt'><col style='width:60pt'>",
            "<tr><th>w1_1</th><th>w1_2</th><th>w1_3</th></tr><tr><td>w1_4</td><td>w1_5</td><td>w1_6</td></tr>");

        var (painted, _) = await PaintedWords.LayOutAndPaintAsync(html);

        Assert.Equal(["w1_1", "w1_3", "w1_4", "w1_6"], painted);
    }

    [Fact]
    public async Task ATableWithoutACollapsedColumn_PaintsEveryCell()
    {
        var html = Document(
            "<col style='width:60pt'><col style='width:60pt'><col style='width:60pt'>",
            "<tr><td>w1_1</td><td>w1_2</td><td>w1_3</td></tr>");

        var (painted, _) = await PaintedWords.LayOutAndPaintAsync(html);

        Assert.Equal(["w1_1", "w1_2", "w1_3"], painted);
    }
}

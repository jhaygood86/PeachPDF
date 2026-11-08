using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// A rowspan that reaches past the last row of its row group is clamped to the rows that exist (HTML Standard, processing model for tables),
/// so the cell is laid out as in a table whose span is 1.
/// </summary>
public class TableRowspanPastLastRowTests
{
    private static IEnumerable<CssRect> WordsOf(CssBox box) => box.Words.Concat(box.Boxes.SelectMany(WordsOf));

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public async Task RowspanPastTheLastRow_LaysTheCellOutLikeASpanOfOne(int span)
    {
        var text = string.Join(' ', Enumerable.Range(1, 20).Select(i => $"w{i}"));
        string Doc(int s) => LayoutHarness.Wrap(
            $"<table id='t' style='width:100pt'><tbody><tr><td id='c' rowspan='{s}'>{text}</td></tr></tbody></table>");

        var (root, _) = await LayoutHarness.LayoutAsync(Doc(span), pageHeight: 600);
        var (reference, _) = await LayoutHarness.LayoutAsync(Doc(1), pageHeight: 600);

        var table = LayoutHarness.FindById(root, "t")!;
        var cell = LayoutHarness.FindById(root, "c")!;
        var expected = LayoutHarness.FindById(reference, "c")!;

        Assert.Equal(expected.Location.Y, cell.Location.Y, 1);
        Assert.Equal(expected.ActualBottom, cell.ActualBottom, 1);
        Assert.All(WordsOf(cell), w => Assert.True(w.Top >= table.Location.Y, $"a word is at {w.Top}, above the table at {table.Location.Y}"));
    }
}

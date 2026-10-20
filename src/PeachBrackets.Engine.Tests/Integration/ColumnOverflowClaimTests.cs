using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A box wider than its column puts its content past the column's edge. The columns that hold the box are the only ones
/// that can draw it, so content that lies in no holder's extent belongs to the first of them.
/// </summary>
public class ColumnOverflowClaimTests
{
    private static string Page(string body) =>
        "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
        "body { margin:0; font: 10pt/12pt Arial } td { vertical-align: top }</style></head><body>" + body + "</body></html>";

    // The left float leaves the container 133pt, so its three columns are 39pt wide and the table is wider than any of them:
    // the second line of its cell lies in the third column's extent, and no third column was filled.
    [Fact]
    public async Task TableWiderThanItsColumns_KeepsTheLinesThatOverflowThePastColumn()
    {
        var body = "<div style='float:left;width:127pt;height:94pt'></div><div style='columns:3;column-gap:8pt'>" +
                   "<table><tr><td><div style='float:right;width:72pt;height:69pt'></div></td><td>w1_1 w1_2 w1_3 w1_4 w1_5 w1_6 w1_7 w1_8</td></tr></table></div>";

        var positions = await PaintedWords.PositionsAsync(Page(body));

        foreach (var word in new[] { "w1_1", "w1_2", "w1_3", "w1_4", "w1_5", "w1_6", "w1_7", "w1_8" })
        {
            Assert.True(positions.ContainsKey(word), $"{word} is in no fragment");
        }
    }

    // A row that only the first column holds, with its cell text starting inside a later column's extent.
    [Fact]
    public async Task RowOnlyTheFirstColumnHolds_KeepsTheCellTextThatStartsInALaterColumnsExtent()
    {
        var body = "<div style='float:left;width:127pt;height:94pt'></div><div style='columns:3;column-gap:8pt'>" +
                   "<div style='display:table-row'><div style='float:right;width:72pt;height:69pt'></div>" +
                   "<div style='display:table-cell'>w2_1 w2_2 w2_3 w2_4 w2_5 w2_6</div></div>" +
                   "w2_7 w2_8 w2_9 w2_10 w2_11 w2_12 w2_13 w2_14 w2_15 w2_16 w2_17 w2_18 w2_19 w2_20 w2_21 w2_22 w2_23 w2_24</div>";

        var positions = await PaintedWords.PositionsAsync(Page(body));

        foreach (var word in Enumerable.Range(1, 24).Select(i => $"w2_{i}"))
        {
            Assert.True(positions.ContainsKey(word), $"{word} is in no fragment");
        }
    }
}
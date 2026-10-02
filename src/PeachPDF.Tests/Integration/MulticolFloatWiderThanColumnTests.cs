using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A float as wide as (or wider than) the column it appears in leaves the text beside it no room, so that text goes below
/// the float (CSS 2.1 §9.5), inside the column. It used to be placed beside the float anyway, past the column's edge,
/// where no column claimed it and the words were lost.
/// </summary>
public class MulticolFloatWiderThanColumnTests
{
    private static string Page(string body) =>
        "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
        "body { margin:0; font: 10pt/12pt Arial } p { margin:0 0 4pt }</style></head><body>" + body + "</body></html>";

    // The reduction of the bug: a 92pt float in a 81pt column.
    [Fact]
    public async Task TextBesideAFloatWiderThanItsColumn_MovesBelowIt()
    {
        var body = "<div style='columns:3;column-gap:8pt'><p></p><div style='float:left;width:92pt;height:32pt'></div>w9_92 </div>";

        var layout = Task.Run(() => PaintedWords.LayOutAndCollectVisibleAsync(Page(body)), TestContext.Current.CancellationToken);
        var finished = await Task.WhenAny(layout, Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)) == layout;

        // A container whose columns could not hold the float used to defer itself from page to page for ever.
        Assert.True(finished, "layout did not finish within 30 seconds");

        var (visible, _) = await layout;
        var (lost, doubled) = PaintedWords.Diff(body, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);

        // Below the float (its bottom is at 20 + 32 = 52 less the empty paragraph above it), not beside it.
        var positions = await PaintedWords.PositionsAsync(Page(body));
        Assert.True(positions["w9_92"].Y >= 20 + 32 - 0.5, $"the text started at y={positions["w9_92"].Y}, beside the float");
    }

    // Two such floats (one per column) with text after them, as a generated document had it.
    [Fact]
    public async Task TextAfterFloatsWiderThanTheirColumns_KeepsEveryWord()
    {
        var body = "<h3>w9_15 </h3>" + string.Join(' ', Enumerable.Range(26, 18).Select(i => $"w9_{i}")) +
                   "<div style='columns:2;column-gap:8pt'><div style='position:relative'><div style='float:left;width:120pt;height:125pt'></div></div>" +
                   "<div style='float:left;width:120pt;height:86pt'>w9_50 w9_51 w9_52 </div></div>w9_53 w9_54 ";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(Page(body));
        var (lost, doubled) = PaintedWords.Diff(body, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);
    }

    // A float narrower than the column, with a word that does not fit beside it, is left as it was.
    [Fact]
    public async Task FloatLeavingSomeRoomBesideIt_IsNotMovedBelow()
    {
        var body = "<div style='columns:2;column-gap:8pt'><td>w9_41 </td><div style='float:left;width:91pt;height:49pt'>w9_47 </div>w9_48 </div>";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(Page(body));
        var (lost, doubled) = PaintedWords.Diff(body, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);
    }

    // A float on the line a column break discards is laid out in that column, and again by the next: the first column's
    // snapshot kept it, and its text was drawn in both.
    [Fact]
    public async Task FloatOnTheLineThatBreaksToTheNextColumn_IsDrawnInOneColumnOnly()
    {
        var long15 = string.Join(' ', Enumerable.Range(1, 15).Select(i => $"w9_{i}"));
        var body = $"<div style='columns:3;column-gap:8pt'><p>{long15}</p>" +
                   "<div style='float:left;width:92pt;height:32pt'>w9_87 </div>w9_92 w9_93 </div>";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(Page(body));
        var (lost, doubled) = PaintedWords.Diff(body, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);
    }
}
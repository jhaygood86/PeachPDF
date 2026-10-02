using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// An absolutely positioned box inside a multi-column container breaks against the page grid, as one outside it does: its
/// containing block is the multi-column container, not a column (css-multicol-1 §2).
/// </summary>
public class AbsoluteBoxInColumnsPageBreakTests
{
    private static string Page(string body) =>
        "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
        "body { margin:0; font: 10pt/12pt Arial }</style></head><body>" + body + "</body></html>";

    private static string Words(string prefix, int from, int to) =>
        string.Join(" ", Enumerable.Range(from, to - from + 1).Select(i => $"{prefix}_{i}"));

    // Thirteen one-word lines (each word is wider than half the box) from y=84 reach 240; the page's content ends at 230, so the last line cannot be placed
    // there and goes to the next page rather than being drawn across the foot and clipped away.
    [Fact]
    public async Task LastLineOfTheBoxAcrossThePageFoot_ContinuesOnTheNextPage()
    {
        var body = "<div style='columns:2;column-gap:8pt'><div style='position:relative'>" +
                   $"<div style='position:absolute;top:64pt;left:36pt;width:74pt'>{Words("w1wordword", 1, 13)} </div></div></div>";

        var positions = await PaintedWords.PositionsAsync(Page(body));

        foreach (var word in Enumerable.Range(1, 13).Select(i => $"w1wordword_{i}"))
        {
            Assert.True(positions.ContainsKey(word), $"{word} is in no fragment");
        }

        Assert.Equal(0, positions["w1wordword_12"].Page);
        Assert.Equal(1, positions["w1wordword_13"].Page);
        Assert.InRange(positions["w1wordword_13"].Y, 19.5, 40);
    }

    // The box is positioned against its containing block, which here is in the second column.
    [Fact]
    public async Task BoxInTheSecondColumn_IsPlacedFromThatColumnsEdge()
    {
        var body = "<div style='columns:2;column-gap:8pt'><p style='margin:0'>w2_1</p>" +
                   "<div style='break-before:column;position:relative'><div style='position:absolute;top:0;left:20pt;width:60pt'>w2_2</div></div></div>";

        var positions = await PaintedWords.PositionsAsync(Page(body));

        // The second column starts at 20 + (260 - 8) / 2 + 8 = 154.
        Assert.InRange(positions["w2_2"].X, 154 + 20 - 1, 154 + 20 + 1);
    }
}
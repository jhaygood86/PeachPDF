using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// CSS 2.1 §9.5: "If a shortened line box is too small to contain any content, then the line box is shifted downward (and its width
/// recomputed) until either some content fits or there are no more floats present." A float that is taller than the page ends on a later
/// page, and that is where the line goes (css-break-3 §4.4: content is not lost off the edge of the fragmentainer).
/// </summary>
public class LineBelowAFloatThatEndsOnALaterPageTests
{
    private static string Page(string body) =>
        "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
        "body { margin:0; font: 10pt/12pt Arial } p { margin:0 0 4pt }</style></head><body>" + body + "</body></html>";

    private static string Words(string prefix, int from, int to) =>
        string.Join(" ", Enumerable.Range(from, to - from + 1).Select(i => $"{prefix}_{i}"));

    // The float leaves no room beside it, and ends 190pt into the second page.
    [Fact]
    public async Task FloatAsWideAsThePage_TheTextStartsBelowItsEnd()
    {
        var positions = await PaintedWords.PositionsAsync(Page(
            $"<div style='float:left;width:250pt;height:400pt'>w1_1</div><p>{Words("w2", 1, 12)}</p>"));

        Assert.Equal(1, positions["w2_1"].Page);
        Assert.InRange(positions["w2_1"].Y, 208, 212);
        Assert.Equal(2, positions["w2_10"].Page);
    }

    // A float wider than the block it is in overflows it, and the block's lines go below it all the same.
    [Fact]
    public async Task FloatWiderThanItsContainingBlock_TheTextStartsBelowItsEnd()
    {
        var positions = await PaintedWords.PositionsAsync(Page(
            $"<div style='width:100pt'><div style='float:left;width:140pt;height:400pt'>w1_1</div><p>{Words("w2", 1, 12)}</p></div>"));

        Assert.Equal(1, positions["w2_1"].Page);
        Assert.InRange(positions["w2_1"].Y, 208, 212);
    }

    // A float shorter than the page: the same rule, on the page the float is on.
    [Fact]
    public async Task FloatShorterThanThePage_TheTextStartsBelowItOnTheSamePage()
    {
        var positions = await PaintedWords.PositionsAsync(Page(
            $"<div style='float:left;width:250pt;height:150pt'>w1_1</div><p>{Words("w2", 1, 12)}</p>"));

        Assert.Equal(0, positions["w2_1"].Page);
        Assert.InRange(positions["w2_1"].Y, 168, 172);
    }
}
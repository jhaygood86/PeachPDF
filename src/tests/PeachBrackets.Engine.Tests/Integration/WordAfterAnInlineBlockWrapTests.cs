using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// A word after an atomic inline that has already filled (or overflowed) its line moves to the next line (CSS Text 3 §5.1: an atomic
/// inline is treated as an object replacement character, which allows a break either side of it). An inline-block is flowed as a block of
/// its own, so the line it sits on holds its rectangle and none of its words, and with no word to look back at the word after it saw no
/// wrap opportunity and stayed on the line, past the box's right edge.
/// </summary>
public class WordAfterAnInlineBlockWrapTests
{
    private static string Page(string body) =>
        "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 160pt; margin: 20pt } " +
        "body { margin:0; font: 12pt Arial }</style></head><body>" + body + "</body></html>";

    // Widths narrower than the inline-block (44, 52: it overflows the line on its own) and, as controls, one it fits in (60) and a
    // width wide enough for both words (120).
    [Theory]
    [InlineData(44, true)]
    [InlineData(52, true)]
    [InlineData(60, true)]
    [InlineData(140, false)]
    public async Task WordAfterAnInlineBlock_WrapsWhenThereIsNoRoomForIt(int width, bool wraps)
    {
        var body = $"<div style='width:{width}pt'><span style='display:inline-block;padding:1pt 3pt;border:1pt solid #888'>w9004_1</span> w9004_2</div>";

        var (painted, _) = await PaintedWords.LayOutAndCollectVisibleAsync(Page(body));
        var (lost, doubled) = PaintedWords.Diff(body, painted);
        Assert.Empty(lost);
        Assert.Empty(doubled);

        var positions = await PaintedWords.PositionsAsync(Page(body));
        var first = positions["w9004_1"];
        var second = positions["w9004_2"];

        if (wraps)
        {
            Assert.Equal(20, second.X, 1);
            Assert.True(second.Y > first.Y + 5, $"the word is at y={second.Y}, on the line of the inline-block (y={first.Y})");
        }
        else
        {
            Assert.Equal(first.Y, second.Y, 3);
            Assert.True(second.X > first.X, $"the word is at x={second.X}, not after the inline-block");
        }
    }

    // No white space between the inline-block and the word: still a wrap opportunity (the object replacement character allows a break
    // either side), but nothing wraps when the word fits.
    [Fact]
    public async Task WordRightAfterAnInlineBlock_WrapsAsWell()
    {
        var body = "<div style='width:52pt'><span style='display:inline-block;padding:1pt 3pt;border:1pt solid #888'>w9004_1</span>w9004_2</div>";

        var (painted, _) = await PaintedWords.LayOutAndCollectVisibleAsync(Page(body));
        var (lost, doubled) = PaintedWords.Diff(body, painted);
        Assert.Empty(lost);
        Assert.Empty(doubled);
    }
}

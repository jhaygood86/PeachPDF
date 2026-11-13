namespace PeachBrackets.Tests;

/// <summary>An inline box split by a bidi run that other text interrupts is drawn as separate pieces, each with its own edges.</summary>
public class BidiSplitInlineTests
{
    private static readonly (byte, byte, byte) Green = (0, 160, 0);

    [Fact]
    public async Task ASpanCutByARightToLeftRun_IsPaintedAsTwoPieces_NotOneBoxAcrossTheOtherText()
    {
        // The override opens inside the span and runs on past its end, so what follows the span reads, in the middle of its two pieces.
        using var canvas = await PageRaster.RenderAsync(
            "<div style='font-size:20px;line-height:24px'><span style='background:rgb(0,160,0);color:rgb(0,160,0)'>aaa &#x202E;bbb</span>ccc&#x202C;ddd</div>",
            width: 300, height: 40);

        var runs = PageRaster.RunsOf(canvas, 12, 300, Green);

        Assert.Equal(2, runs.Count);
    }

    [Fact]
    public async Task ASpanOnOneRun_IsPaintedAsOneBox()
    {
        using var canvas = await PageRaster.RenderAsync(
            "<div style='font-size:20px;line-height:24px'><span style='background:rgb(0,160,0);color:rgb(0,160,0)'>aaa bbb</span>ccc</div>",
            width: 300, height: 40);

        Assert.Single(PageRaster.RunsOf(canvas, 12, 300, Green));
    }
}

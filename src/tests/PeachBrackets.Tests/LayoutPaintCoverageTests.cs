namespace PeachBrackets.Tests;

/// <summary>
/// Rendered checks (through the public API) of layout behavior added alongside the WPT runner: absolutely positioned grid children, the intrinsic width of a
/// wrapping flex column, the border width keywords, and values too large for an integer.
/// </summary>
public class LayoutPaintCoverageTests
{
    private static readonly (byte, byte, byte) Green = (0, 128, 0);
    private static readonly (byte, byte, byte) White = (255, 255, 255);

    private const string Grid =
        "<div style='display:grid;grid-template-columns:50px 50px;grid-template-rows:50px 50px;position:relative;width:100px;height:100px'>{0}</div>";

    [Fact]
    public async Task AnAbsoluteGridChildIsPlacedInTheAreaItsLinesName()
    {
        using var canvas = await PageRaster.RenderAsync(string.Format(Grid,
            "<div style='position:absolute;grid-column:2;grid-row:2;width:20px;height:20px;background:rgb(0,128,0)'></div>"));

        Assert.Equal(Green, PageRaster.Rgb(canvas, 60, 60));
        Assert.Equal(White, PageRaster.Rgb(canvas, 10, 10));
        Assert.Equal(White, PageRaster.Rgb(canvas, 60, 10));
    }

    [Fact]
    public async Task AnAbsoluteGridChildWithARightInsetSitsAtTheRightEdgeOfItsArea()
    {
        using var canvas = await PageRaster.RenderAsync(string.Format(Grid,
            "<div style='position:absolute;grid-column:1;grid-row:1;right:0;top:0;width:20px;height:20px;background:rgb(0,128,0)'></div>"));

        // The area is the first column (x 0..50), so the box is x 30..50, not at the grid's own right edge.
        Assert.Equal(Green, PageRaster.Rgb(canvas, 40, 10));
        Assert.Equal(White, PageRaster.Rgb(canvas, 10, 10));
        Assert.Equal(White, PageRaster.Rgb(canvas, 90, 10));
    }

    [Fact]
    public async Task AnAbsoluteGridChildWithoutLinesFillsFromThePaddingEdge()
    {
        using var canvas = await PageRaster.RenderAsync(string.Format(Grid,
            "<div style='position:absolute;width:20px;height:20px;background:rgb(0,128,0)'></div>"));

        Assert.Equal(Green, PageRaster.Rgb(canvas, 10, 10));
    }

    [Fact]
    public async Task AWrappingFlexColumnWithADefiniteHeightIsAsWideAsItsColumns()
    {
        using var canvas = await PageRaster.RenderAsync(
            "<div style='display:flex;flex-flow:column wrap;height:100px;width:max-content;background:rgb(0,128,0)'>" +
            "<div style='width:50px;flex:0 0 100px'></div><div style='width:50px;flex:0 0 100px'></div></div>");

        // Two columns of 50px; the old measurement gave 50px.
        Assert.Equal(Green, PageRaster.Rgb(canvas, 95, 50));
        Assert.Equal(White, PageRaster.Rgb(canvas, 105, 50));
    }

    [Fact]
    public async Task AWrappingFlexColumnPutsItemsInOneColumnWhenTheyFit()
    {
        using var canvas = await PageRaster.RenderAsync(
            "<div style='display:flex;flex-flow:column wrap;height:100px;width:max-content;row-gap:0;column-gap:10px;background:rgb(0,128,0)'>" +
            "<div style='width:50px;flex:0 0 40px'></div><div style='width:30px;flex:0 0 40px'></div></div>");

        Assert.Equal(Green, PageRaster.Rgb(canvas, 45, 50));
        Assert.Equal(White, PageRaster.Rgb(canvas, 55, 50));
    }

    [Theory]
    [InlineData("thin", 1)]
    [InlineData("medium", 3)]
    [InlineData("thick", 5)]
    public async Task TheBorderWidthKeywordsAreOneThreeAndFivePixels(string keyword, int expected)
    {
        using var canvas = await PageRaster.RenderAsync(
            $"<div style='border:{keyword} solid rgb(0,0,255);width:30px;height:30px;margin:10px'></div>", width: 80, height: 60);

        var runs = PageRaster.RunsOf(canvas, 25, 80, (0, 0, 255));

        Assert.Equal(expected, runs[0].Last - runs[0].First + 1);
    }

    [Fact]
    public async Task AListMarkerShapeIsDrawnWhenTheDecorationsSnap()
    {
        using var canvas = await PageRaster.RenderAsync(
            "<ul style='list-style-type:square;margin:0;padding-left:30px'><li style='font-size:20px'>x</li></ul>", width: 80, height: 40);

        var dark = false;
        for (var x = 5; x < 30 && !dark; x++)
            for (var y = 0; y < 30 && !dark; y++)
                dark = PageRaster.Rgb(canvas, x, y).R < 100;

        Assert.True(dark, "the square marker is drawn in the padding to the left of the text");
    }

    [Theory]
    [InlineData("z-index:99999999999999")]
    [InlineData("z-index:-99999999999999")]
    [InlineData("order:99999999999999")]
    public async Task AnIntegerTooLargeForAnIntIsClamped_NotAnError(string declaration)
    {
        using var canvas = await PageRaster.RenderAsync(
            $"<div style='position:relative;{declaration};width:20px;height:20px;background:rgb(0,128,0)'></div>", width: 40, height: 40);

        Assert.Equal(Green, PageRaster.Rgb(canvas, 10, 10));
    }
}

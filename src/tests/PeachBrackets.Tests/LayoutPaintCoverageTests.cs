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

    [Theory]
    [InlineData("align-self:center;justify-self:center", 35, 35, true)]
    [InlineData("align-self:end;justify-self:end", 85, 85, true)]
    [InlineData("align-self:flex-end", 10, 85, true)]
    [InlineData("align-self:start", 10, 10, true)]
    [InlineData("safe center", 10, 10, false)]
    public async Task AnAbsoluteGridChildIsAlignedInItsStaticPositionRectangle(string alignment, int x, int y, bool declaration)
    {
        var style = declaration ? alignment : "align-self:safe center";
        using var canvas = await PageRaster.RenderAsync(
            "<div style='display:grid;width:100px;height:100px'>" +
            $"<div style='position:absolute;width:30px;height:30px;background:rgb(0,128,0);{style}'></div></div>");

        Assert.Equal(Green, PageRaster.Rgb(canvas, x, y));
    }

    [Fact]
    public async Task TheContainersAlignItemsAppliesToAnAbsoluteChildWithoutItsOwnAlignSelf()
    {
        using var canvas = await PageRaster.RenderAsync(
            "<div style='display:grid;width:100px;height:100px;align-items:end;justify-items:center'>" +
            "<div style='position:absolute;width:30px;height:30px;background:rgb(0,128,0)'></div></div>");

        Assert.Equal(Green, PageRaster.Rgb(canvas, 50, 85));
        Assert.Equal(White, PageRaster.Rgb(canvas, 50, 10));
    }

    [Fact]
    public async Task EmptyAutoFitRowsAndColumnsCollapse_SoTheContentDistributionSharesTheSpaceAmongTheOthers()
    {
        // Five 20px tracks fit the 100px square; only column 3/4 and row 2/3 hold an item, so two tracks are left in each axis and space-evenly puts
        // 20px before, between and after them.
        using var canvas = await PageRaster.RenderAsync(
            "<div style='display:grid;width:100px;height:100px;grid-template-columns:repeat(auto-fit,20px);grid-template-rows:repeat(auto-fit,20px);" +
            "align-content:space-evenly;justify-content:space-evenly'>" +
            "<div style='grid-row:2;grid-column:3;background:rgb(0,128,0)'></div><div style='grid-row:3;grid-column:4;background:rgb(0,128,0)'></div></div>");

        Assert.Equal(Green, PageRaster.Rgb(canvas, 30, 30));
        Assert.Equal(Green, PageRaster.Rgb(canvas, 70, 70));
        Assert.Equal(White, PageRaster.Rgb(canvas, 10, 10));
        Assert.Equal(White, PageRaster.Rgb(canvas, 30, 70));
    }

    private const string SizelessSvg =
        "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg'%3E%3Crect width='100%25' height='50%25' fill='green'/%3E%3C/svg%3E";

    [Fact]
    public async Task ASvgBackgroundWithNoSizeOrViewBoxFillsThePositioningArea()
    {
        // The svg states no size, so its percentages are shares of the 100x60 area it is drawn into: the top half is green.
        using var canvas = await PageRaster.RenderAsync(
            $"<div style=\"width:100px;height:60px;background:url({SizelessSvg}) no-repeat\"></div>", loadResources: true);

        Assert.Equal(Green, PageRaster.Rgb(canvas, 90, 20));
        Assert.Equal(White, PageRaster.Rgb(canvas, 90, 55));
    }

    [Fact]
    public async Task ASvgBackgroundWithNoSizeTakesTheSizeBackgroundSizeGivesIt()
    {
        using var canvas = await PageRaster.RenderAsync(
            $"<div style=\"width:100px;height:60px;background:url({SizelessSvg}) no-repeat;background-size:40px 40px\"></div>", loadResources: true);

        Assert.Equal(Green, PageRaster.Rgb(canvas, 30, 10));
        Assert.Equal(White, PageRaster.Rgb(canvas, 30, 30));
        Assert.Equal(White, PageRaster.Rgb(canvas, 70, 10));
    }

    private static readonly string BluePng = SolidPng(0, 0, 255);

    private static string SolidPng(int r, int g, int b)
    {
        var raster = new PeachDrawing.RasterRenderContext();
        using var canvas = raster.CreateCanvas(8, 8);
        canvas.DrawRectangle(raster.GetSolidBrush(PeachDrawing.Core.PaintColor.FromArgb(255, r, g, b)), 0, 0, 8, 8);
        using var stream = new MemoryStream();
        canvas.Save(stream, "png", new PeachImage.Formats.Png.PngEncoderOptions());
        return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }

    [Fact]
    public async Task ABackgroundClippedToTheBorderAreaPaintsOnlyUnderTheBorder()
    {
        using var canvas = await PageRaster.RenderAsync(
            "<div style=\"width:60px;height:40px;border:20px solid transparent;background:rgb(0,128,0);background-clip:border-area\"></div>");

        Assert.Equal(Green, PageRaster.Rgb(canvas, 10, 40));
        Assert.Equal(Green, PageRaster.Rgb(canvas, 90, 40));
        Assert.Equal(White, PageRaster.Rgb(canvas, 50, 40));
    }

    [Fact]
    public async Task AnImageBackgroundClippedToTheBorderAreaLeavesThePaddingBoxEmpty()
    {
        using var canvas = await PageRaster.RenderAsync(
            $"<div style=\"width:60px;height:40px;border:20px solid transparent;background:url({BluePng});background-size:100% 100%;background-clip:border-area\"></div>",
            loadResources: true);

        Assert.NotEqual(White, PageRaster.Rgb(canvas, 10, 40));
        Assert.Equal(White, PageRaster.Rgb(canvas, 50, 40));
    }

    [Fact]
    public async Task ARepeatedBackgroundFillsTheAreaUnderATransparentBorder()
    {
        // The default origin is the padding box and the default clip the border box: the tiles go on outward under the border.
        using var canvas = await PageRaster.RenderAsync(
            $"<div style=\"width:60px;height:40px;border:20px solid transparent;background:url({BluePng});background-size:20px 20px\"></div>",
            loadResources: true);

        Assert.NotEqual(White, PageRaster.Rgb(canvas, 5, 5));
        Assert.NotEqual(White, PageRaster.Rgb(canvas, 95, 75));
    }

    [Fact]
    public async Task ADeclaredWidthInlineBlockThatDoesNotFitTheLineStartsTheNextOne()
    {
        // Two 70px boxes in a 100px container: the second cannot share the first's line, so it sits beneath it rather than beside it.
        using var canvas = await PageRaster.RenderAsync(
            "<div style='width:100px;font-size:0'><div style='display:inline-block;width:70px;height:20px;background:rgb(0,128,0)'>x</div>" +
            "<div style='display:inline-block;width:70px;height:20px;background:rgb(0,128,0)'>y</div></div>");

        Assert.Equal(Green, PageRaster.Rgb(canvas, 20, 18));
        Assert.Equal(Green, PageRaster.Rgb(canvas, 20, 38));
        Assert.Equal(White, PageRaster.Rgb(canvas, 90, 18));
    }

    [Fact]
    public async Task ADeclaredWidthInlineBlockThatFitsStaysOnTheLine()
    {
        using var canvas = await PageRaster.RenderAsync(
            "<div style='width:200px;font-size:0'><div style='display:inline-block;width:70px;height:20px;background:rgb(0,128,0)'>x</div>" +
            "<div style='display:inline-block;width:70px;height:20px;background:rgb(0,128,0)'>y</div></div>");

        Assert.Equal(Green, PageRaster.Rgb(canvas, 20, 18));
        Assert.Equal(Green, PageRaster.Rgb(canvas, 90, 18));
    }

    [Fact]
    public async Task AutoFillRowsRepeatAsOftenAsTheyFit_AndKeepTheirEmptyTracks()
    {
        using var canvas = await PageRaster.RenderAsync(
            "<div style='display:grid;width:100px;height:100px;grid-template-rows:repeat(auto-fill,20px);grid-template-columns:100px'>" +
            "<div style='grid-row:4;background:rgb(0,128,0)'></div></div>");

        // Row 4 is 60..80: three empty rows above it are kept.
        Assert.Equal(Green, PageRaster.Rgb(canvas, 50, 70));
        Assert.Equal(White, PageRaster.Rgb(canvas, 50, 30));
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

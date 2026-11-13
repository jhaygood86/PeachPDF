namespace PeachBrackets.Tests;

/// <summary><c>background-repeat: space</c> and <c>round</c> (CSS Backgrounds 3 section 3.4).</summary>
public class BackgroundRepeatTests
{
    private static readonly (byte, byte, byte) Green = (0, 128, 0);
    private static readonly (byte, byte, byte) White = (255, 255, 255);

    private static string Box(string repeat, string size = "30px 30px") =>
        $"<div style='width:100px;height:30px;background:linear-gradient(rgb(0,128,0),rgb(0,128,0)) 0 0/{size};background-repeat:{repeat}'></div>";

    [Fact]
    public async Task Space_SharesTheLeftoverBetweenWholeTiles()
    {
        // 100px holds three 30px tiles; the 10px left over is two 5px gaps: tiles at 0, 35 and 70.
        using var canvas = await PageRaster.RenderAsync(Box("space no-repeat"), 120, 40);

        Assert.Equal(Green, PageRaster.Rgb(canvas, 10, 10));
        Assert.Equal(White, PageRaster.Rgb(canvas, 32, 10));
        Assert.Equal(Green, PageRaster.Rgb(canvas, 40, 10));
        Assert.Equal(Green, PageRaster.Rgb(canvas, 99, 10));
        Assert.Equal(White, PageRaster.Rgb(canvas, 67, 10));
    }

    [Fact]
    public async Task Space_WithRoomForOneTile_PlacesItWhereBackgroundPositionPutsIt()
    {
        using var canvas = await PageRaster.RenderAsync(Box("space no-repeat", "70px 30px"), 120, 40);

        Assert.Equal(Green, PageRaster.Rgb(canvas, 10, 10));
        Assert.Equal(White, PageRaster.Rgb(canvas, 85, 10));
    }

    [Fact]
    public async Task Round_ResizesTheTileToFillAWholeNumberOfThem()
    {
        // Three tiles across 100px are 33.3px each, with no gap anywhere.
        using var canvas = await PageRaster.RenderAsync(Box("round no-repeat"), 120, 40);

        Assert.Equal(Green, PageRaster.Rgb(canvas, 20, 10));
        Assert.Equal(Green, PageRaster.Rgb(canvas, 50, 10));
        Assert.Equal(Green, PageRaster.Rgb(canvas, 90, 10));
    }

    [Fact]
    public async Task Round_ScalesTheOtherAutoAxisToKeepProportions_AndSpaceWorksVertically()
    {
        using var canvas = await PageRaster.RenderAsync(
            "<div style='width:30px;height:100px;background:linear-gradient(rgb(0,128,0),rgb(0,128,0)) 0 0/30px 30px;background-repeat:no-repeat space'></div>", 60, 120);

        Assert.Equal(Green, PageRaster.Rgb(canvas, 10, 10));
        Assert.Equal(White, PageRaster.Rgb(canvas, 10, 32));
        Assert.Equal(Green, PageRaster.Rgb(canvas, 10, 40));
    }

    [Fact]
    public async Task Round_OnBothAxesRescalesEach()
    {
        using var canvas = await PageRaster.RenderAsync(
            "<div style='width:100px;height:50px;background:linear-gradient(rgb(0,128,0),rgb(0,128,0)) 0 0/30px 20px;background-repeat:round'></div>", 120, 60);

        Assert.Equal(Green, PageRaster.Rgb(canvas, 99, 49));
    }
}

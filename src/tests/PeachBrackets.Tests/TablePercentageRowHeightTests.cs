namespace PeachBrackets.Tests;

/// <summary>
/// A table cell's percentage <c>height</c> is given its share of a definite-height table before the
/// other rows share what is left (css-tables-3 "distribute extra height to the rows"). Rows are painted
/// red, green and blue so the used row heights read straight off the pixels.
/// </summary>
public class TablePercentageRowHeightTests
{
    private static readonly (byte R, byte G, byte B) Red = (255, 0, 0);
    private static readonly (byte R, byte G, byte B) Green = (0, 255, 0);
    private static readonly (byte R, byte G, byte B) Blue = (0, 0, 255);

    private static async Task<PeachDrawing.RasterCanvas> RenderAsync(string cell1, string cell2, string cell3, string tableStyle = "height:300px")
    {
        const string cellStyle = "padding:0;";
        return await PageRaster.RenderAsync(
            $"<table style='border-spacing:0;width:100px;{tableStyle}'>" +
            $"<tr style='background:#f00'><td style='{cellStyle}{cell1}'><div style='height:50px'></div></td></tr>" +
            $"<tr style='background:#0f0'><td style='{cellStyle}{cell2}'><div style='height:50px'></div></td></tr>" +
            $"<tr style='background:#00f'><td style='{cellStyle}{cell3}'><div style='height:50px'></div></td></tr></table>",
            width: 100, height: 400);
    }

    private static void AssertColor(PeachDrawing.RasterCanvas canvas, int y, (byte R, byte G, byte B) expected) =>
        Assert.Equal(expected, PageRaster.Rgb(canvas, 10, y));

    [Fact]
    public async Task AFullHeightCell_TakesWhatTheOtherRowsLeave()
    {
        var canvas = await RenderAsync("", "height:100%", "");

        AssertColor(canvas, 45, Red);
        AssertColor(canvas, 55, Green);
        AssertColor(canvas, 245, Green);
        AssertColor(canvas, 255, Blue);
        AssertColor(canvas, 295, Blue);
    }

    [Fact]
    public async Task AHalfHeightCell_TakesItsShareAndTheRestIsSpreadOverTheOthers()
    {
        var canvas = await RenderAsync("", "height:50%", "");

        AssertColor(canvas, 70, Red);
        AssertColor(canvas, 80, Green);
        AssertColor(canvas, 220, Green);
        AssertColor(canvas, 230, Blue);
    }

    [Fact]
    public async Task TwoFullHeightCells_AreServedInOrderAndCapped()
    {
        var canvas = await RenderAsync("height:100%", "height:100%", "");

        AssertColor(canvas, 195, Red);
        AssertColor(canvas, 205, Green);
        AssertColor(canvas, 245, Green);
        AssertColor(canvas, 255, Blue);
    }

    [Fact]
    public async Task APercentageCell_InAnAutoHeightTable_StaysAtItsContentHeight()
    {
        var canvas = await RenderAsync("", "height:100%", "", tableStyle: "");

        AssertColor(canvas, 45, Red);
        AssertColor(canvas, 55, Green);
        AssertColor(canvas, 95, Green);
        AssertColor(canvas, 105, Blue);
        AssertColor(canvas, 145, Blue);
    }

    [Fact]
    public async Task EveryRowAsksForAPercentage_TheLeftoverIsSharedBetweenThem()
    {
        var canvas = await RenderAsync("height:10%", "height:10%", "height:10%");

        // 30% asked for of a 300px table: each row keeps its 50px content, and the 150px left is shared equally.
        AssertColor(canvas, 95, Red);
        AssertColor(canvas, 105, Green);
        AssertColor(canvas, 195, Green);
        AssertColor(canvas, 205, Blue);
        AssertColor(canvas, 295, Blue);
    }
}

using PeachBrackets;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>
/// The rule between the columns of a multi-column container is painted the way the same border on a box is: its width in layout units
/// whatever the canvas's pixel density, its ends on whole pixels, and every style (not only solid, dashed and dotted) honored.
/// </summary>
public class ColumnRulePaintTests
{
    private static readonly (byte, byte, byte) White = (255, 255, 255);
    private static readonly (byte, byte, byte) Green = (0, 128, 0);

    // Two 50px columns and a 10px gap, from x = 50 to 60, each column filled by one 40px tall block.
    private static async Task<RasterCanvas> RenderAsync(string rule)
    {
        var context = await TestDocument.NewContextAsync();
        context.PixelsPerPoint = 96.0 / 72.0;

        var options = new HtmlDocumentOptions
        {
            MediaType = MediaType.Screen,
            PageWidthPoints = 110 * 0.75,
            ViewportHeightPoints = 40 * 0.75,
            SnapBoxDecorationsToCssPixels = true,
            SnapBorderWidthsToCssPixels = true,
        };
        options.SetMargins(0);

        var body = $"<div style='width:110px;height:40px;columns:2;column-gap:10px;column-fill:auto;column-rule:{rule}'>" +
                   "<div style='height:40px'></div><div style='height:40px'></div></div>";
        using var document = await HtmlDocument.LoadAsync(TestDocument.Wrap(body), context, options);
        using (var measuring = context.CreateCanvas(1, 1))
        {
            await document.LayoutAsync(measuring);
        }

        var canvas = context.CreateCanvas(110, 40);
        canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 255, 255)), 0, 0, 110, 40);
        await document.PaintPageAsync(0, canvas);
        return canvas;
    }

    private static (byte R, byte G, byte B) Rgb(RasterCanvas canvas, int x, int y)
    {
        var (r, g, b, _) = TestDocument.Pixel(canvas, x, y);
        return (r, g, b);
    }

    [Fact]
    public async Task ASolidRule_IsAsWideAsDeclared_WhenALayoutUnitIsACssPixel()
    {
        using var canvas = await RenderAsync("10px solid rgb(0,128,0)");

        Assert.Equal(White, Rgb(canvas, 49, 20));
        for (var x = 50; x < 60; x++)
            Assert.Equal(Green, Rgb(canvas, x, 20));
        Assert.Equal(White, Rgb(canvas, 60, 20));
    }

    [Fact]
    public async Task ASolidRule_EndsOnWholeRows()
    {
        using var canvas = await RenderAsync("10px solid rgb(0,128,0)");

        Assert.Equal(Green, Rgb(canvas, 55, 0));
        Assert.Equal(Green, Rgb(canvas, 55, 39));
    }

    [Fact]
    public async Task ADoubleRule_LeavesAGapBetweenItsTwoBands()
    {
        using var canvas = await RenderAsync("9px double rgb(0,128,0)");

        // 9px: a 3px band, a 3px gap, a 3px band, centered on the 10px gap (x 50.5 .. 59.5).
        var painted = Enumerable.Range(50, 10).Select(x => Rgb(canvas, x, 20)).ToList();
        Assert.Contains(Green, painted);
        Assert.Contains(White, painted);
        Assert.Equal(Green, painted[1]);
        Assert.Equal(Green, painted[8]);
    }

    [Fact]
    public async Task ADashedRule_AlternatesPaintedAndEmptyRuns()
    {
        using var canvas = await RenderAsync("4px dashed rgb(0,128,0)");

        var column = Enumerable.Range(0, 40).Select(y => Rgb(canvas, 55, y)).ToList();
        Assert.Contains(Green, column);
        Assert.Contains(White, column);
    }

    [Fact]
    public async Task ARuleOfStyleNone_PaintsNothing()
    {
        using var canvas = await RenderAsync("10px none rgb(0,128,0)");

        for (var x = 48; x < 62; x++)
            Assert.Equal(White, Rgb(canvas, x, 20));
    }
}

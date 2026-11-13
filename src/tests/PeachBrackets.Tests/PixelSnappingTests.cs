using PeachBrackets;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>
/// A raster host that lays the document out in CSS pixels (<see cref="RasterRenderContext.PixelsPerPoint"/> of 96/72) and asks for the pixel snapping
/// a browser does: boxes, borders and the baseline of text land on whole device pixels, with no fringe where a browser has none.
/// </summary>
public class PixelSnappingTests
{
    private static async Task<RasterCanvas> RenderAsync(string body, int width = 120, int height = 80)
    {
        var context = await TestDocument.NewContextAsync();
        context.PixelsPerPoint = 96.0 / 72.0;

        var options = new HtmlDocumentOptions
        {
            MediaType = MediaType.Screen,
            PageWidthPoints = width * 0.75,
            ViewportHeightPoints = height * 0.75,
            SnapBoxDecorationsToCssPixels = true,
            SnapBorderWidthsToCssPixels = true,
        };
        options.SetMargins(0);

        using var document = await HtmlDocument.LoadAsync(TestDocument.Wrap(body), context, options);
        using (var measuring = context.CreateCanvas(1, 1))
        {
            await document.LayoutAsync(measuring);
        }

        var canvas = context.CreateCanvas(width, height);
        canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 255, 255)), 0, 0, width, height);
        await document.PaintPageAsync(0, canvas);
        return canvas;
    }

    [Fact]
    public async Task ABorderOfWholePixels_CoversExactlyThatManyDevicePixels()
    {
        using var canvas = await RenderAsync("<div style='border:5px solid rgb(0,0,255);width:50px;height:20px'></div>");

        for (var x = 0; x < 5; x++)
            Assert.Equal((0, 0, 255), Rgb(canvas, x, 10));
        Assert.Equal((255, 255, 255), Rgb(canvas, 5, 10)); // the box's own interior, white
        Assert.Equal((0, 0, 255), Rgb(canvas, 59, 10)); // 5 + 50 + 5 = 60 wide
        Assert.Equal((255, 255, 255), Rgb(canvas, 60, 10));
    }

    [Fact]
    public async Task ABoxAtAHalfPixel_IsSnappedToWholePixels_WithNoFringeRow()
    {
        using var canvas = await RenderAsync("<div style='height:10.5px'></div><div style='height:20px;background:rgb(0,128,0)'></div>");

        // Every pixel of every row is either the background or the box's colour: no row of partial coverage.
        for (var y = 0; y < 40; y++)
        {
            var pixel = Rgb(canvas, 30, y);
            Assert.True(pixel == (255, 255, 255) || pixel == (0, 128, 0), $"row {y} is a fringe: {pixel}");
        }
    }

    [Fact]
    public async Task TheSameTextAtDifferentFractionalOffsets_PaintsIdenticallyWhenTheyShareAPixel()
    {
        using var a = await RenderAsync("<div style='margin-top:10.2px;font-size:16px'>Hg</div>");
        using var b = await RenderAsync("<div style='margin-top:10.4px;font-size:16px'>Hg</div>");

        Assert.True(a.ToPixelBuffer().PremultipliedRgba.Span.SequenceEqual(b.ToPixelBuffer().PremultipliedRgba.Span),
            "the baseline is snapped to a whole pixel, so both offsets paint the same rows");
    }

    [Fact]
    public async Task TheDefaultContext_StillLaysOutInPoints()
    {
        var context = await TestDocument.NewContextAsync();

        Assert.Equal(1.0, context.PixelsPerPoint);
        Assert.Equal(1.0, context.LayoutUnitsPerPoint);
    }

    private static (byte R, byte G, byte B) Rgb(RasterCanvas canvas, int x, int y)
    {
        var (r, g, b, _) = TestDocument.Pixel(canvas, x, y);
        return (r, g, b);
    }
}

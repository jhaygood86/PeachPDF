using PeachDrawing.Core;

namespace PeachDrawing.Tests;

public class ScaledCanvasBrushTests
{
    private static (byte R, byte G, byte B, byte A) At(RasterCanvas canvas, int x, int y)
    {
        var buffer = canvas.ToPixelBuffer();
        var span = buffer.PremultipliedRgba.Span;
        var i = (y * buffer.Width + x) * 4;
        return (span[i], span[i + 1], span[i + 2], span[i + 3]);
    }

    /// <summary>A 40 layout-unit square on a canvas whose layout unit is half a point, so it is 20 pixels at 72 dpi.</summary>
    private static RasterCanvas HalfPointCanvas(RenderContext context)
    {
        var region = RasterSurfaceFactory.Create(context, pixelsPerPoint: 2, new Rect(0, 0, 40, 40), dpi: 72, maxPixels: 1_000_000)!;
        Assert.Equal(20, region.Surface.Width);
        return (RasterCanvas)region.Graphics;
    }

    [Fact]
    public void LinearGradient_IsPositionedInTheCanvasOwnUnits()
    {
        var context = new RasterRenderContext();
        using var canvas = HalfPointCanvas(context);
        var brush = canvas.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(40, 0),
            [(PaintColor.FromArgb(255, 255, 0, 0), 0.0), (PaintColor.FromArgb(255, 0, 0, 255), 1.0)]);

        canvas.DrawRectangle(brush, 0, 0, 40, 40);

        // The gradient spans the whole square, so its midpoint is the middle pixel. Read as points it would span twice
        // the surface and the middle pixel would still be three quarters red.
        var middle = At(canvas, 10, 5);
        Assert.InRange(middle.R, 112, 144);
        Assert.InRange(middle.B, 112, 144);
        Assert.True(At(canvas, 18, 5).B > 220);
    }

    [Fact]
    public void RadialGradient_IsPositionedInTheCanvasOwnUnits()
    {
        var context = new RasterRenderContext();
        using var canvas = HalfPointCanvas(context);
        var brush = canvas.GetRadialGradientBrush(new PaintPoint(20, 20), 20, 20,
            [(PaintColor.FromArgb(255, 255, 0, 0), 0.0), (PaintColor.FromArgb(255, 0, 0, 255), 1.0)]);

        canvas.DrawRectangle(brush, 0, 0, 40, 40);

        Assert.True(At(canvas, 10, 10).R > 220);
        Assert.True(At(canvas, 10, 1).B > 200);
    }
}

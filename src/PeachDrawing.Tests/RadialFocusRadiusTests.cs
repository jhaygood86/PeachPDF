using PeachDrawing.Core;

namespace PeachDrawing.Tests;

public class RadialFocusRadiusTests
{
    private static readonly PaintColor Red = PaintColor.FromArgb(255, 255, 0, 0);
    private static readonly PaintColor Blue = PaintColor.FromArgb(255, 0, 0, 255);

    private static (byte R, byte G, byte B, byte A) At(RasterCanvas canvas, int x, int y)
    {
        var p = canvas.Surface.Row(y).Slice(x * 4, 4);
        return (p[0], p[1], p[2], p[3]);
    }

    [Fact]
    public void FocusRadius_FillsTheFirstColorInsideTheFocalCircle_AndLeavesOutsideTheConeUnpainted()
    {
        using var canvas = new RasterRenderContext().CreateCanvas(100, 100);

        // From a circle of radius 10 around (30, 50) to one of radius 30 around (60, 50).
        var brush = canvas.GetRadialGradientBrush(new PaintPoint(60, 50), 30, 30,
            [(Red, 0.0), (Blue, 1.0)], false, new PaintPoint(30, 50), 10);
        canvas.DrawRectangle(brush, 0, 0, 100, 100);

        // Left of the outer circle, in the part of the focal circle it does not cover: mostly the first color.
        Assert.True(At(canvas, 22, 50).R > At(canvas, 22, 50).B);
        Assert.Equal(255, At(canvas, 85, 50).B);             // beyond the outer circle's far side: last color (padded)
        Assert.Equal(0, At(canvas, 5, 5).A);                 // no circle of the family reaches the corner
    }

    [Fact]
    public void ZeroFocusRadius_IsTheClassicFocalGradient()
    {
        using var canvas = new RasterRenderContext().CreateCanvas(100, 100);
        var brush = canvas.GetRadialGradientBrush(new PaintPoint(50, 50), 40, 40,
            [(Red, 0.0), (Blue, 1.0)], false, new PaintPoint(50, 50), 0);
        canvas.DrawRectangle(brush, 0, 0, 100, 100);

        Assert.True(At(canvas, 50, 50).R > 240);
        Assert.True(At(canvas, 95, 50).B > 240);
    }
}

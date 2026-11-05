using PeachDrawing.Core;
using System.Numerics;

namespace PeachDrawing.CoreGraphics.Tests;

public class BrushTests
{
    private static (PaintColor, double)[] RedToBlue => [(PaintColor.FromArgb(255, 255, 0, 0), 0), (PaintColor.FromArgb(255, 0, 0, 255), 1)];

    [Fact]
    public void A_linear_gradient_runs_from_the_first_stop_to_the_last()
    {
        using var scene = Scene.Create(100, 10);
        using var brush = scene.Context.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(100, 0), RedToBlue);
        scene.Canvas.DrawRectangle(brush, 0, 0, 100, 10);

        Assert.True(scene.Px(2, 5).R > 230);
        Assert.True(scene.Px(97, 5).B > 230);
        var middle = scene.Px(50, 5);
        Assert.InRange(middle.R, 100, 160);
        Assert.InRange(middle.B, 100, 160);
    }

    [Fact]
    public void A_pad_gradient_extends_its_end_colours()
    {
        using var scene = Scene.Create(100, 10);
        using var brush = scene.Context.GetLinearGradientBrush(new PaintPoint(30, 0), new PaintPoint(70, 0), RedToBlue);
        scene.Canvas.DrawRectangle(brush, 0, 0, 100, 10);

        Assert.True(scene.IsRed(5, 5));
        Assert.True(scene.IsBlue(95, 5));
    }

    [Fact]
    public void A_repeating_gradient_starts_over()
    {
        using var scene = Scene.Create(100, 10);
        using var brush = scene.Context.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(20, 0), RedToBlue, true);
        scene.Canvas.DrawRectangle(brush, 0, 0, 100, 10);

        // Each 20-unit period starts red again and ends blue.
        Assert.True(scene.Px(41, 5).R > 200);
        Assert.True(scene.Px(39, 5).B > 200);
        Assert.True(scene.Px(81, 5).R > 200);
    }

    [Fact]
    public void A_radial_gradient_goes_from_the_centre_outwards()
    {
        using var scene = Scene.Create(40, 40);
        using var brush = scene.Context.GetRadialGradientBrush(new PaintPoint(20, 20), 20, 20, RedToBlue);
        scene.Canvas.DrawRectangle(brush, 0, 0, 40, 40);

        Assert.True(scene.Px(20, 20).R > 230);
        Assert.True(scene.Px(1, 20).B > 200);
        Assert.True(scene.Px(39, 20).B > 200);
    }

    [Fact]
    public void A_radial_gradients_focus_is_not_moved_by_the_ellipse()
    {
        using var scene = Scene.Create(100, 100);
        using var brush = scene.Context.GetRadialGradientBrush(new PaintPoint(50, 50), 40, 20, RedToBlue, false, new PaintPoint(50, 60));
        scene.Canvas.DrawRectangle(brush, 0, 0, 100, 100);

        // The brightest red is at the focus, ten units below the centre.
        Assert.True(scene.Px(50, 60).R > scene.Px(50, 50).R);
    }

    [Fact]
    public void A_conic_gradient_sweeps_clockwise_from_straight_up()
    {
        using var scene = Scene.Create(40, 40);
        PaintColor[] colors =
        [
            PaintColor.FromArgb(255, 255, 0, 0), PaintColor.FromArgb(255, 255, 0, 0),
            PaintColor.FromArgb(255, 0, 0, 255), PaintColor.FromArgb(255, 0, 0, 255),
        ];
        double[] angles = [0, Math.PI, Math.PI + 1e-6, 2 * Math.PI];
        using var brush = scene.Context.GetConicGradientBrush(new PaintPoint(20, 20), 20, colors, angles);
        scene.Canvas.DrawRectangle(brush, 0, 0, 40, 40);

        // Clockwise from up: the right-hand half is the first half turn (red), the left-hand half the second (blue).
        Assert.True(scene.IsRed(30, 20));
        Assert.True(scene.IsBlue(10, 20));
    }

    [Fact]
    public void A_gradient_pen_strokes_with_the_gradient()
    {
        using var scene = Scene.Create(100, 20);
        using var brush = scene.Context.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(100, 0), RedToBlue);
        var pen = scene.Context.GetPen(brush);
        pen.Width = 6;
        scene.Canvas.DrawLine(pen, 0, 10, 100, 10);

        Assert.True(scene.Px(3, 10).R > 200);
        Assert.True(scene.Px(97, 10).B > 200);
        Assert.True(scene.IsClear(50, 2));
    }

    [Fact]
    public void A_tile_brush_repeats_the_tile_upright()
    {
        using var scene = Scene.Create(40, 40);
        var tile = scene.Canvas.CreateTile(10, 10)!.Value;
        tile.Graphics.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 10, 5);   // top half red
        tile.Graphics.DrawRectangle(scene.Solid(0, 0, 255), 0, 5, 10, 5);   // bottom half blue
        using var brush = new TileBrush(tile.Image, 10, 10, Matrix3x2.Identity, ImageSampling.Nearest);
        scene.Canvas.DrawRectangle(brush, 0, 0, 40, 40);

        Assert.True(scene.IsRed(2, 2));
        Assert.True(scene.IsBlue(2, 7));
        Assert.True(scene.IsRed(32, 22));
        Assert.True(scene.IsBlue(32, 27));
        tile.Image.Dispose();
    }

    [Fact]
    public void A_hatch_brush_paints_its_foreground()
    {
        using var scene = Scene.Create(40, 40);
        var hatch = new HatchBrush(HatchStyle.Horizontal, PaintColor.FromArgb(255, 255, 0, 0), PaintColor.FromArgb(255, 255, 255, 255), 4, 1);
        scene.Canvas.DrawRectangle(hatch, 0, 0, 40, 40);

        Assert.True(scene.OpaquePixels() > 40 * 40 / 2);
        Assert.Equal(0, scene.Context.Counts.Total - scene.Context.Counts.Surfaces + 0);
    }

    [Fact]
    public void Transparent_brushes_draw_nothing()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0, 0), 0, 0, 40, 40);

        Assert.Equal(0, scene.OpaquePixels());
    }
}

using PeachDrawing.Core;
using PeachDrawing.Skia;
using System.Numerics;

namespace PeachDrawing.Skia.Tests;

public class BrushTests
{
    private static readonly PaintColor Red = PaintColor.FromArgb(255, 255, 0, 0);
    private static readonly PaintColor Blue = PaintColor.FromArgb(255, 0, 0, 255);

    [Fact]
    public void A_linear_gradient_runs_from_one_stop_to_the_other()
    {
        using var scene = Scene.Create(100, 10);
        using var brush = scene.Canvas.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(100, 0), [(Red, 0), (Blue, 1)]);
        scene.Canvas.DrawRectangle(brush, 0, 0, 100, 10);

        Assert.True(scene.Px(1, 5).R > 240);
        Assert.InRange(scene.Px(50, 5).R, 100, 156);
        Assert.InRange(scene.Px(50, 5).B, 100, 156);
        Assert.True(scene.Px(98, 5).B > 240);
    }

    [Fact]
    public void A_repeating_linear_gradient_repeats_past_its_extent_and_a_padded_one_holds_its_end()
    {
        using var repeating = Scene.Create(100, 10);
        using var repeatBrush = repeating.Canvas.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(25, 0), [(Red, 0), (Blue, 1)], isRepeating: true);
        repeating.Canvas.DrawRectangle(repeatBrush, 0, 0, 100, 10);

        using var padded = Scene.Create(100, 10);
        using var padBrush = padded.Canvas.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(25, 0), [(Red, 0), (Blue, 1)]);
        padded.Canvas.DrawRectangle(padBrush, 0, 0, 100, 10);

        Assert.True(repeating.Px(26, 5).R > 200, "a new period starts red");
        Assert.True(padded.IsBlue(60, 5));
    }

    [Fact]
    public void A_radial_gradient_is_the_first_colour_at_its_centre_and_the_last_at_its_rim()
    {
        using var scene = Scene.Create(40, 40);
        using var brush = scene.Canvas.GetRadialGradientBrush(new PaintPoint(20, 20), 20, 20, [(Red, 0), (Blue, 1)]);
        scene.Canvas.DrawRectangle(brush, 0, 0, 40, 40);

        Assert.True(scene.Px(20, 20).R > 230);
        Assert.True(scene.Px(2, 20).B > 200);
    }

    [Fact]
    public void An_elliptical_radial_gradient_is_squeezed_along_the_short_axis()
    {
        using var scene = Scene.Create(80, 40);
        using var brush = scene.Canvas.GetRadialGradientBrush(new PaintPoint(40, 20), 40, 10, [(Red, 0), (Blue, 1)]);
        scene.Canvas.DrawRectangle(brush, 0, 0, 80, 40);

        // 10 units above the centre is the rim of the short axis; 10 units to the right is a quarter of the way along the long one.
        Assert.True(scene.Px(40, 10).B > scene.Px(50, 20).B);
    }

    [Fact]
    public void A_conic_gradient_changes_with_the_angle_around_its_centre()
    {
        using var scene = Scene.Create(40, 40);
        using var brush = scene.Canvas.GetConicGradientBrush(new PaintPoint(20, 20), 20, [Red, Blue, Red], [0, Math.PI, 2 * Math.PI]);
        scene.Canvas.DrawRectangle(brush, 0, 0, 40, 40);

        // Angle 0 is straight up and runs clockwise: the top is red, the bottom (half a turn) is blue.
        Assert.True(scene.Px(20, 4).R > 200, "top");
        Assert.True(scene.Px(20, 36).B > 200, "bottom");
    }

    [Fact]
    public void A_tile_brush_repeats_its_picture_across_the_shape()
    {
        using var scene = Scene.Create(40, 40);
        var tile = scene.Canvas.CreateTile(10, 10);
        Assert.NotNull(tile);
        using (tile!.Value.Image)
        {
            tile.Value.Graphics.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 5, 10);
            tile.Value.Graphics.Dispose();
            using var brush = new TileBrush(tile.Value.Image, 10, 10);
            scene.Canvas.DrawRectangle(brush, 0, 0, 40, 40);
        }

        foreach (var cell in new[] { 0, 10, 20, 30 })
        {
            Assert.True(scene.IsRed(cell + 2, 5), $"left half of cell {cell}");
            Assert.True(scene.IsClear(cell + 7, 5), $"right half of cell {cell}");
        }
    }

    [Fact]
    public void A_tile_brush_of_pixels_repeats_the_image_and_the_transform_moves_the_grid()
    {
        using var scene = Scene.Create(40, 40);
        using var image = scene.Context.ImageFromStream(new MemoryStream(Scene.TwoColourPng(8, 4)));
        using var brush = new TileBrush(image, 20, 20, Matrix3x2.CreateTranslation(0, 0), ImageSampling.Nearest);
        scene.Canvas.DrawRectangle(brush, 0, 0, 40, 40);

        Assert.True(scene.IsRed(5, 5));
        Assert.True(scene.IsBlue(15, 5));
        Assert.True(scene.IsRed(25, 25));
    }

    [Fact]
    public void A_hatch_paints_lines_over_its_background()
    {
        using var scene = Scene.Create(40, 40);
        using var hatch = new HatchBrush(HatchStyle.Cross, Red, Blue, 10, 2);
        scene.Canvas.DrawRectangle(hatch, 0, 0, 40, 40);

        Assert.True(scene.IsRed(5, 5) || scene.IsRed(5, 4) || scene.IsRed(4, 5), "a line of the cross");
        Assert.True(scene.IsBlue(2, 2), "the background between the lines");
    }

    [Fact]
    public void A_pen_can_stroke_with_a_gradient()
    {
        using var scene = Scene.Create(100, 10);
        using var brush = scene.Canvas.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(100, 0), [(Red, 0), (Blue, 1)]);
        var pen = scene.Canvas.GetPen(brush);
        pen.Width = 6;
        scene.Canvas.DrawLine(pen, 0, 5, 100, 5);

        Assert.True(scene.Px(2, 5).R > 230);
        Assert.True(scene.Px(97, 5).B > 230);
    }

    [Fact]
    public void A_fully_transparent_colour_paints_nothing()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0, 0), 0, 0, 40, 40);

        Assert.Equal(0, scene.OpaquePixels());
    }
}

using PeachDrawing.Core;
using PeachDrawing.Win2D;
using System.Numerics;

namespace PeachDrawing.Win2D.Tests;

public class DrawingTests
{
    [Fact]
    public void A_filled_rectangle_paints_exactly_its_area()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 10, 10, 10, 10);

        Assert.True(scene.IsRed(15, 15));
        Assert.True(scene.IsRed(10, 10));
        Assert.True(scene.IsClear(9, 15));
        Assert.True(scene.IsClear(20, 15));
        Assert.True(scene.IsClear(15, 9));
        Assert.True(scene.IsClear(15, 20));
    }

    [Fact]
    public void A_stroked_line_has_the_pens_width_and_colour()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawLine(scene.Pen(255, 0, 0, 4), 5, 20, 35, 20);

        Assert.True(scene.IsRed(20, 19));
        Assert.True(scene.IsRed(20, 20));
        Assert.True(scene.IsClear(20, 16));
        Assert.True(scene.IsClear(20, 24));
    }

    [Fact]
    public void A_dashed_pen_leaves_gaps_along_the_line()
    {
        using var scene = Scene.Create(60, 20);
        var pen = scene.Pen(255, 0, 0, 2);
        pen.DashStyle = DashStyle.Dash;
        scene.Canvas.DrawLine(pen, 0, 10, 60, 10);

        // A 3:1 dash of a 2-wide pen is 6 on and 2 off, so the line is both drawn and broken.
        var drawn = Enumerable.Range(0, 60).Count(x => scene.Px(x, 10).A > 128);
        Assert.InRange(drawn, 35, 52);
    }

    [Fact]
    public void A_clip_rectangle_limits_what_is_drawn_until_it_is_popped()
    {
        using var scene = Scene.Create();
        scene.Canvas.PushClip(new Rect(0, 0, 20, 40));
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 10);
        scene.Canvas.PopClip();
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 20, 40, 10);

        Assert.True(scene.IsRed(10, 5));
        Assert.True(scene.IsClear(30, 5));
        Assert.True(scene.IsRed(30, 25));
    }

    [Fact]
    public void A_path_clip_and_an_excluding_clip_shape_the_drawing()
    {
        using var scene = Scene.Create();
        using (var path = scene.Canvas.GetGraphicsPath())
        {
            path.Start(0, 0);
            path.LineTo(20, 0);
            path.LineTo(20, 20);
            path.LineTo(0, 20);
            path.CloseFigure();
            scene.Canvas.PushClip(path);
        }

        scene.Canvas.PushClipExclude(new Rect(5, 5, 5, 5));
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);
        scene.Canvas.PopClip();
        scene.Canvas.PopClip();

        Assert.True(scene.IsRed(2, 2));
        Assert.True(scene.IsClear(7, 7));
        Assert.True(scene.IsClear(30, 30));
    }

    [Fact]
    public void A_pushed_transform_moves_what_follows_and_is_undone_by_its_pop()
    {
        using var scene = Scene.Create();
        scene.Canvas.PushTransform(Matrix3x2.CreateTranslation(20, 0));
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 10, 10);
        scene.Canvas.PopTransform();
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 20, 10, 10);

        Assert.True(scene.IsRed(25, 5));
        Assert.True(scene.IsClear(5, 5));
        Assert.True(scene.IsRed(5, 25));
        Assert.Equal(Matrix3x2.Identity, scene.Canvas.CurrentTransform);
    }

    [Fact]
    public void An_even_odd_path_leaves_its_inner_contour_empty()
    {
        using var scene = Scene.Create();
        using var path = scene.Canvas.GetGraphicsPath();
        path.FillMode = FillMode.EvenOdd;
        foreach (var (inset, size) in new[] { (0.0, 30.0), (10.0, 10.0) })
        {
            path.Start(inset, inset);
            path.LineTo(inset + size, inset);
            path.LineTo(inset + size, inset + size);
            path.LineTo(inset, inset + size);
            path.CloseFigure();
        }

        scene.Canvas.DrawPath(scene.Solid(255, 0, 0), path);

        Assert.True(scene.IsRed(3, 3));
        Assert.True(scene.IsClear(15, 15));
    }

    [Fact]
    public void A_linear_gradient_runs_from_one_colour_to_the_other()
    {
        using var scene = Scene.Create();
        var brush = scene.Context.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(40, 0),
            [(PaintColor.FromArgb(255, 255, 0, 0), 0), (PaintColor.FromArgb(255, 0, 0, 255), 1)]);
        scene.Canvas.DrawRectangle(brush, 0, 0, 40, 40);

        var left = scene.Px(1, 20);
        var right = scene.Px(38, 20);
        var middle = scene.Px(20, 20);
        Assert.True(left.R > 220 && left.B < 40);
        Assert.True(right.B > 220 && right.R < 40);
        Assert.InRange(middle.R, 90, 170);
        Assert.InRange(middle.B, 90, 170);
    }

    [Fact]
    public void A_radial_gradient_and_a_conic_gradient_both_paint()
    {
        using var scene = Scene.Create();
        var radial = scene.Context.GetRadialGradientBrush(new PaintPoint(10, 10), 10, 10,
            [(PaintColor.FromArgb(255, 255, 0, 0), 0), (PaintColor.FromArgb(255, 0, 0, 255), 1)]);
        scene.Canvas.DrawRectangle(radial, 0, 0, 20, 20);

        var conic = scene.Context.GetConicGradientBrush(new PaintPoint(30, 30), 8,
            [PaintColor.FromArgb(255, 255, 0, 0), PaintColor.FromArgb(255, 0, 0, 255)], [0, 2 * Math.PI]);
        scene.Canvas.DrawRectangle(conic, 22, 22, 16, 16);

        Assert.True(scene.Px(10, 10).R > 200);
        Assert.True(scene.Px(30, 30).A > 200);
        Assert.True(scene.IsClear(25, 5));
    }

    [Fact]
    public void A_polygon_fills_its_interior()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawPolygon(scene.Solid(255, 0, 0), [new PaintPoint(5, 5), new PaintPoint(35, 5), new PaintPoint(5, 35)]);

        Assert.True(scene.IsRed(10, 10));
        Assert.True(scene.IsClear(30, 30));
    }

    [Fact]
    public void Path_coordinates_are_points_and_rectangles_are_layout_units()
    {
        // 2 layout units per point: the page is 20 points, 40 units.
        using var scene = Scene.Create(20, 20, pixelsPerPoint: 2);
        using var path = scene.Canvas.GetGraphicsPath();
        path.Start(0, 0);
        path.LineTo(5, 0);
        path.LineTo(5, 5);
        path.LineTo(0, 5);
        path.CloseFigure();
        scene.Canvas.DrawPath(scene.Solid(255, 0, 0), path);
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 255), 0, 20, 5, 5);

        Assert.True(scene.IsRed(7, 2));
        Assert.True(scene.IsClear(12, 2));
        Assert.Equal(40, scene.Composer.PixelWidth);
        Assert.True(scene.Px(2, 22).B > 200);
        Assert.True(scene.IsClear(7, 22));
    }

    [Fact]
    public void A_pen_width_is_in_points_so_it_scales_with_the_layout_units()
    {
        using var scene = Scene.Create(20, 20, pixelsPerPoint: 2);
        scene.Canvas.DrawLine(scene.Pen(255, 0, 0, 3), 0, 20, 40, 20);

        // 3 points are 6 layout units wide.
        Assert.True(scene.IsRed(20, 17));
        Assert.True(scene.IsRed(20, 22));
        Assert.True(scene.IsClear(20, 14));
    }

    [Fact]
    public void A_background_option_clears_the_page_to_a_colour()
    {
        using var scene = Scene.Create(options: new Win2dComposerOptions { Background = PaintColor.FromArgb(255, 255, 0, 0) });
        Assert.True(scene.IsRed(0, 0));
        Assert.True(scene.IsRed(39, 39));
    }

    [Fact]
    public void A_higher_dpi_makes_a_larger_bitmap_of_the_same_page()
    {
        using var scene = Scene.Create(40, 40, options: new Win2dComposerOptions { Dpi = 192 });
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 10, 10, 10, 10);

        Assert.Equal(80, scene.Composer.PixelWidth);
        Assert.True(scene.IsRed(30, 30));
        Assert.True(scene.IsClear(15, 15));
    }

    [Fact]
    public async Task The_page_saves_as_a_png_file()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 10, 10);
        using var stream = new MemoryStream();
        await scene.Composer.SaveAsync(stream);

        var bytes = stream.ToArray();
        Assert.True(bytes.Length > 50);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
    }
}

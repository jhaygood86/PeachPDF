using PeachDrawing.Core;
using PeachDrawing.Skia;
using System.Numerics;

namespace PeachDrawing.Skia.Tests;

public class DrawingTests
{
    [Fact]
    public void A_filled_rectangle_paints_exactly_its_area()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 10, 10, 10, 10);

        Assert.True(scene.IsRed(15, 15));
        Assert.True(scene.IsClear(5, 5));
        Assert.True(scene.IsClear(25, 25));
        Assert.Equal(100, scene.OpaquePixels());
    }

    [Fact]
    public void A_stroked_line_paints_along_the_line_with_the_pens_width()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawLine(scene.Pen(0, 0, 255, 4), 5, 20, 35, 20);

        Assert.True(scene.IsBlue(20, 19));
        Assert.True(scene.IsBlue(20, 20));
        Assert.True(scene.IsClear(20, 15));
    }

    [Fact]
    public void A_dashed_pen_leaves_gaps_and_a_solid_one_does_not()
    {
        using var dashed = Scene.Create(60, 10);
        var pen = dashed.Pen(255, 0, 0, 4);
        pen.DashStyle = DashStyle.Dash;
        dashed.Canvas.DrawLine(pen, 0, 5, 60, 5);

        using var solid = Scene.Create(60, 10);
        solid.Canvas.DrawLine(solid.Pen(255, 0, 0, 4), 0, 5, 60, 5);

        Assert.Contains(Enumerable.Range(0, 60), x => dashed.IsClear(x, 5));
        Assert.True(Enumerable.Range(0, 60).All(x => solid.IsRed(x, 5)));
    }

    [Fact]
    public void A_path_fills_with_its_fill_rule()
    {
        using var scene = Scene.Create();
        using var path = scene.Canvas.GetGraphicsPath();
        path.FillMode = FillMode.EvenOdd;
        Scene.AddRectangle(path, 2, 2, 36, 36);
        Scene.AddRectangle(path, 12, 12, 16, 16);
        scene.Canvas.DrawPath(scene.Solid(255, 0, 0), path);

        Assert.True(scene.IsRed(5, 5));
        Assert.True(scene.IsClear(20, 20), "the inner square is a hole under the even-odd rule");
    }

    [Fact]
    public void A_polygon_is_filled_and_one_with_too_few_points_is_ignored()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawPolygon(scene.Solid(255, 0, 0), [new PaintPoint(5, 5), new PaintPoint(35, 5), new PaintPoint(5, 35)]);
        scene.Canvas.DrawPolygon(scene.Solid(0, 0, 255), [new PaintPoint(30, 30), new PaintPoint(39, 39)]);

        Assert.True(scene.IsRed(10, 10));
        Assert.True(scene.IsClear(33, 33));
    }

    [Fact]
    public void A_clip_confines_painting_and_popping_it_lifts_the_confinement()
    {
        using var scene = Scene.Create();
        scene.Canvas.PushClip(new Rect(0, 0, 20, 40));
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 10);
        scene.Canvas.PopClip();
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 255), 0, 20, 40, 10);

        Assert.True(scene.IsRed(10, 5));
        Assert.True(scene.IsClear(30, 5));
        Assert.True(scene.IsBlue(30, 25));
    }

    [Fact]
    public void An_excluded_clip_leaves_its_rectangle_unpainted()
    {
        using var scene = Scene.Create();
        scene.Canvas.PushClipExclude(new Rect(10, 10, 20, 20));
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);
        scene.Canvas.PopClip();

        Assert.True(scene.IsRed(5, 5));
        Assert.True(scene.IsClear(20, 20));
    }

    [Fact]
    public void A_path_clip_confines_painting_to_the_path()
    {
        using var scene = Scene.Create();
        using var shape = scene.Canvas.GetGraphicsPath();
        Scene.AddRectangle(shape, 10, 10, 10, 10);
        scene.Canvas.PushClip(shape);
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);
        scene.Canvas.PopClip();

        Assert.True(scene.IsRed(15, 15));
        Assert.True(scene.IsClear(5, 5));
    }

    [Fact]
    public void A_transform_moves_painting_and_is_tracked_for_readers()
    {
        using var scene = Scene.Create();
        scene.Canvas.PushTransform(Matrix3x2.CreateTranslation(20, 20));
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 10, 10);
        Assert.Equal(new Vector2(20, 20), scene.Canvas.CurrentTransform.Translation);
        scene.Canvas.PopTransform();

        Assert.Equal(Matrix3x2.Identity, scene.Canvas.CurrentTransform);
        Assert.True(scene.IsRed(25, 25));
        Assert.True(scene.IsClear(5, 5));
    }

    [Fact]
    public void The_transform_scale_follows_the_pushed_matrices()
    {
        using var scene = Scene.Create();
        scene.Canvas.PushTransform(Matrix3x2.CreateScale(2, 3));

        Assert.Equal((2.0, 3.0), scene.Canvas.TransformScale);
    }

    [Fact]
    public void A_pushed_blend_mode_composites_each_draw_against_the_page()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawRectangle(scene.Solid(255, 255, 0), 0, 0, 40, 40);
        scene.Canvas.PushBlendMode(PaintBlendMode.Multiply);
        scene.Canvas.DrawRectangle(scene.Solid(0, 255, 255), 0, 0, 40, 40);
        scene.Canvas.PopBlendMode();

        // Yellow x cyan is green.
        Assert.Equal((0, 255, 0, 255), scene.Px(20, 20));
    }

    [Fact]
    public void Anti_aliasing_can_be_switched_off_for_a_shape()
    {
        using var smooth = Scene.Create();
        smooth.Canvas.DrawLine(smooth.Pen(255, 0, 0, 1), 0, 0, 40, 17);

        using var aliased = Scene.Create();
        aliased.Canvas.PushAntiAlias(false);
        aliased.Canvas.DrawLine(aliased.Pen(255, 0, 0, 1), 0, 0, 40, 17);
        aliased.Canvas.PopAntiAlias();

        static bool HasPartialCoverage(Scene s) => Enumerable.Range(0, 40).Any(x => Enumerable.Range(0, 40).Any(y => s.Px(x, y).A is > 0 and < 255));
        Assert.True(HasPartialCoverage(smooth));
        Assert.False(HasPartialCoverage(aliased));
    }

    [Fact]
    public void A_page_in_points_is_scaled_by_the_contexts_layout_units()
    {
        using var scene = Scene.Create(20, 20, pixelsPerPoint: 2);
        using var path = scene.Canvas.GetGraphicsPath();

        // A path is in points, so 5 points are 10 layout units - and the composer maps them onto the 40-pixel surface.
        Scene.AddRectangle(path, 5, 5, 5, 5);
        scene.Canvas.DrawPath(scene.Solid(255, 0, 0), path);

        Assert.Equal(40, scene.Composer.PixelWidth);
        Assert.True(scene.IsRed(15, 15));
        Assert.True(scene.IsClear(5, 5));
    }

    [Fact]
    public void A_disposed_canvas_throws_from_every_drawing_call()
    {
        using var scene = Scene.Create();
        var canvas = scene.Canvas;
        scene.Composer.Finish();

        Assert.Throws<ObjectDisposedException>(() => canvas.DrawRectangle(scene.Solid(1, 2, 3), 0, 0, 1, 1));
        Assert.Throws<ObjectDisposedException>(() => canvas.PushClip(new Rect(0, 0, 1, 1)));
    }

    [Fact]
    public void Popping_with_nothing_pushed_is_an_error()
    {
        using var scene = Scene.Create();

        Assert.Throws<InvalidOperationException>(() => scene.Canvas.PopTransform());
    }
}

using PeachDrawing.Core;
using System.Numerics;

namespace PeachDrawing.CoreGraphics.Tests;

public class DrawingTests
{
    [Fact]
    public void A_filled_rectangle_lands_where_it_was_asked_with_y_growing_downwards()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 4, 2, 10, 6);

        Assert.True(scene.IsRed(8, 4));
        Assert.True(scene.IsClear(8, 20));   // below the rectangle, not mirrored into it
        Assert.True(scene.IsClear(8, 1));
        Assert.True(scene.IsClear(2, 4));
        Assert.True(scene.IsClear(20, 4));
    }

    [Fact]
    public void A_stroked_line_covers_its_width()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawLine(scene.Pen(0, 0, 255, 4), 5, 20, 35, 20);

        Assert.True(scene.IsBlue(20, 20));
        Assert.True(scene.IsBlue(20, 21));
        Assert.True(scene.IsClear(20, 25));
    }

    [Fact]
    public void A_dashed_pen_leaves_gaps()
    {
        using var scene = Scene.Create(60, 20);
        var pen = scene.Pen(255, 0, 0, 4);
        pen.DashStyle = DashStyle.Dash;
        scene.Canvas.DrawLine(pen, 0, 10, 60, 10);

        var covered = Enumerable.Range(0, 60).Count(x => scene.IsRed(x, 10));
        Assert.InRange(covered, 15, 50);
    }

    [Fact]
    public void A_clip_confines_drawing_and_pops_back()
    {
        using var scene = Scene.Create();
        scene.Canvas.PushClip(new Rect(10, 10, 10, 10));
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);
        scene.Canvas.PopClip();
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 255), 30, 30, 5, 5);

        Assert.True(scene.IsRed(15, 15));
        Assert.True(scene.IsClear(5, 5));
        Assert.True(scene.IsClear(25, 15));
        Assert.True(scene.IsBlue(32, 32));
    }

    [Fact]
    public void An_exclusion_clip_leaves_a_hole()
    {
        using var scene = Scene.Create();
        scene.Canvas.PushClipExclude(new Rect(10, 10, 10, 10));
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);
        scene.Canvas.PopClip();

        Assert.True(scene.IsClear(15, 15));
        Assert.True(scene.IsRed(5, 5));
        Assert.True(scene.IsRed(30, 30));
    }

    [Fact]
    public void A_path_clip_follows_the_path()
    {
        using var scene = Scene.Create();
        using var path = scene.Canvas.GetGraphicsPath();
        Scene.AddRectangle(path, 10, 10, 10, 10);
        scene.Canvas.PushClip(path);
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);
        scene.Canvas.PopClip();

        Assert.True(scene.IsRed(15, 15));
        Assert.True(scene.IsClear(5, 5));
    }

    [Fact]
    public void A_pushed_transform_moves_drawing_and_is_tracked()
    {
        using var scene = Scene.Create();
        scene.Canvas.PushTransform(Matrix3x2.CreateTranslation(20, 10));
        Assert.Equal(20, scene.Canvas.CurrentTransform.M31);
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 5, 5);
        scene.Canvas.PopTransform();

        Assert.True(scene.IsRed(22, 12));
        Assert.True(scene.IsClear(2, 2));
        Assert.Equal(Matrix3x2.Identity, scene.Canvas.CurrentTransform);
    }

    [Fact]
    public void A_scale_transform_is_reported_by_TransformScale()
    {
        using var scene = Scene.Create();
        scene.Canvas.PushTransform(Matrix3x2.CreateScale(2, 3));
        Assert.Equal((2.0, 3.0), (Math.Round(scene.Canvas.TransformScale.X, 6), Math.Round(scene.Canvas.TransformScale.Y, 6)));
        scene.Canvas.PopTransform();
    }

    [Fact]
    public void Polygon_and_even_odd_path_fill()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawPolygon(scene.Solid(255, 0, 0), [new PaintPoint(2, 2), new PaintPoint(12, 2), new PaintPoint(12, 12), new PaintPoint(2, 12)]);

        using var path = scene.Canvas.GetGraphicsPath();
        path.FillMode = FillMode.EvenOdd;
        Scene.AddRectangle(path, 20, 20, 16, 16);
        Scene.AddRectangle(path, 24, 24, 8, 8);
        scene.Canvas.DrawPath(scene.Solid(0, 0, 255), path);

        Assert.True(scene.IsRed(6, 6));
        Assert.True(scene.IsBlue(21, 21));
        Assert.True(scene.IsClear(28, 28));
    }

    [Fact]
    public void A_multiply_blend_darkens()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawRectangle(scene.Solid(255, 255, 0), 0, 0, 40, 40);
        scene.Canvas.PushBlendMode(PaintBlendMode.Multiply);
        scene.Canvas.DrawRectangle(scene.Solid(0, 255, 255), 0, 0, 40, 40);
        scene.Canvas.PopBlendMode();

        // yellow x cyan = green
        Assert.Equal((0, 255, 0), (scene.Px(20, 20).R, scene.Px(20, 20).G, scene.Px(20, 20).B));
    }

    [Fact]
    public void Disposed_canvas_throws()
    {
        using var scene = Scene.Create();
        scene.Composer.Finish();
        Assert.Throws<ObjectDisposedException>(() => scene.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 1, 1));
    }

    [Fact]
    public void Popping_with_nothing_pushed_throws()
    {
        using var scene = Scene.Create();
        Assert.Throws<InvalidOperationException>(() => scene.Canvas.PopClip());
    }
}

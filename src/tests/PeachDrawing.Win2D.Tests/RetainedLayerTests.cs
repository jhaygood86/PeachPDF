using PeachDrawing.Core;
using PeachDrawing.Win2D;

namespace PeachDrawing.Win2D.Tests;

public class RetainedLayerTests
{
    private static RetainedLayer Record(Scene scene, Rect bounds)
    {
        var layer = scene.Canvas.CreateRetainedLayer(bounds)!;
        layer.Recorder.DrawRectangle(scene.Solid(255, 0, 0), bounds.X, bounds.Y, bounds.Width, bounds.Height);
        layer.Seal();
        return layer;
    }

    [Fact]
    public void A_retained_layer_is_recorded_once_and_drawn_where_the_target_transform_puts_it()
    {
        using var scene = Scene.Create();
        using var layer = Record(scene, new Rect(0, 0, 10, 10));

        layer.Draw(scene.Canvas);
        scene.Canvas.PushTransform(System.Numerics.Matrix3x2.CreateTranslation(20, 20));
        layer.Draw(scene.Canvas);
        scene.Canvas.PopTransform();

        Assert.True(scene.IsRed(5, 5));
        Assert.True(scene.IsRed(25, 25));
        Assert.True(scene.IsClear(15, 15));
    }

    [Fact]
    public void A_layer_whose_bounds_are_not_at_the_origin_draws_where_its_content_was_recorded()
    {
        using var scene = Scene.Create();
        using var layer = Record(scene, new Rect(12, 8, 10, 10));

        layer.Draw(scene.Canvas);

        Assert.True(scene.IsRed(15, 12));
        Assert.True(scene.IsClear(5, 5));
    }

    [Fact]
    public void Each_draw_has_its_own_opacity()
    {
        using var scene = Scene.Create();
        using var layer = Record(scene, new Rect(0, 0, 10, 10));

        layer.Draw(scene.Canvas, 0.5);
        scene.Canvas.PushTransform(System.Numerics.Matrix3x2.CreateTranslation(20, 0));
        layer.Draw(scene.Canvas, 1.0);
        scene.Canvas.PopTransform();

        Assert.InRange(scene.Px(5, 5).A, 118, 138);
        Assert.True(scene.IsRed(25, 5));
    }

    [Fact]
    public void A_layer_cannot_be_drawn_before_it_is_sealed_and_releases_everything_it_holds()
    {
        using var scene = Scene.Create();
        var baseline = scene.Context.Counts.Total;
        var layer = scene.Canvas.CreateRetainedLayer(new Rect(0, 0, 10, 10))!;
        Assert.Equal(1, scene.Context.Counts.RetainedLayers);
        Assert.Throws<InvalidOperationException>(() => layer.Draw(scene.Canvas));

        layer.Seal();
        Assert.True(layer.CanDraw(scene.Canvas));
        layer.Dispose();

        Assert.False(layer.CanDraw(scene.Canvas));
        Assert.Equal(baseline, scene.Context.Counts.Total);
    }

    [Fact]
    public void A_layer_is_not_made_for_an_empty_region()
    {
        using var scene = Scene.Create();
        Assert.Null(scene.Canvas.CreateRetainedLayer(new Rect(0, 0, 0, 10)));
    }
}

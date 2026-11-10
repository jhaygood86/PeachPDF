using PeachDrawing.Core;
using PeachDrawing.Skia;
using SkiaSharp;
using System.Numerics;

namespace PeachDrawing.Skia.Tests;

/// <summary>The Skia retained layer: a group recorded once and drawn again under a new matrix and opacity, with nothing replayed.</summary>
public class RetainedLayerTests
{
    private static RetainedLayer RedSquare(Scene scene, Rect bounds)
    {
        var layer = scene.Canvas.CreateRetainedLayer(bounds)!;
        layer.Recorder.DrawRectangle(scene.Solid(255, 0, 0), bounds.X, bounds.Y, bounds.Width, bounds.Height);
        layer.Seal();
        return layer;
    }

    [Fact]
    public void A_sealed_layer_draws_its_content_where_the_target_matrix_puts_it()
    {
        using var scene = Scene.Create(40, 40);
        using var layer = RedSquare(scene, new Rect(0, 0, 10, 10));

        Assert.True(layer.IsSealed);
        Assert.True(layer.CanDraw(scene.Canvas));

        scene.Canvas.PushTransform(Matrix3x2.CreateTranslation(20, 20));
        layer.Draw(scene.Canvas);
        scene.Canvas.PopTransform();

        Assert.True(scene.IsRed(25, 25));
        Assert.True(scene.IsClear(5, 5));
        Assert.True(scene.IsClear(35, 35));
    }

    [Fact]
    public void A_layer_is_drawn_again_with_a_different_matrix_and_opacity_without_being_recorded_again()
    {
        using var scene = Scene.Create(40, 40);
        using var layer = RedSquare(scene, new Rect(0, 0, 10, 10));

        layer.Draw(scene.Canvas, 1.0);
        scene.Canvas.PushTransform(Matrix3x2.CreateTranslation(20, 20));
        layer.Draw(scene.Canvas, 0.5);
        scene.Canvas.PopTransform();

        Assert.True(scene.IsRed(5, 5));
        var faded = scene.Px(25, 25);
        Assert.InRange(faded.A, 100, 156);
        Assert.True(faded.R > 100);
    }

    [Fact]
    public void A_layer_not_at_the_origin_is_recorded_in_the_creating_canvas_coordinates()
    {
        using var scene = Scene.Create(40, 40);
        using var layer = RedSquare(scene, new Rect(20, 10, 10, 10));

        layer.Draw(scene.Canvas);

        Assert.True(scene.IsRed(25, 15));
        Assert.True(scene.IsClear(5, 5));
        Assert.True(scene.IsClear(15, 15));
    }

    [Fact]
    public void A_layer_is_kept_at_the_density_of_the_host_matrix()
    {
        using var surface = SKSurface.Create(new SKImageInfo(80, 80, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var context = new SkiaRenderContext();
        surface.Canvas.Scale(2, 2);
        using var canvas = SkiaCanvas.FromCanvas(context, surface.Canvas, new Rect(0, 0, 40, 40));

        using var layer = canvas.CreateRetainedLayer(new Rect(0, 0, 10, 10))!;
        layer.Recorder.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, 10, 10);
        layer.Seal();
        layer.Draw(canvas);

        using var pixels = surface.PeekPixels();
        Assert.Equal(SKColors.Red, pixels.GetPixelColor(19, 19));
        Assert.Equal(0, pixels.GetPixelColor(21, 21).Alpha);
    }

    [Fact]
    public void A_layer_cannot_be_drawn_before_it_is_sealed_or_after_it_is_disposed()
    {
        using var scene = Scene.Create(20, 20);
        var layer = scene.Canvas.CreateRetainedLayer(new Rect(0, 0, 10, 10))!;

        Assert.False(layer.IsSealed);
        Assert.False(layer.CanDraw(scene.Canvas));
        Assert.Throws<InvalidOperationException>(() => layer.Draw(scene.Canvas));

        layer.Seal();
        layer.Seal();
        Assert.Throws<InvalidOperationException>(() => _ = layer.Recorder);

        layer.Dispose();
        layer.Dispose();
        Assert.False(layer.CanDraw(scene.Canvas));
        Assert.Throws<ObjectDisposedException>(() => layer.Draw(scene.Canvas));
    }

    [Fact]
    public void A_layer_belongs_to_the_gpu_context_it_was_made_for()
    {
        using var scene = Scene.Create(20, 20);
        using var layer = RedSquare(scene, new Rect(0, 0, 10, 10));
        using var other = Scene.Create(20, 20);
        var raster = new PeachDrawing.RasterRenderContext();

        // Both draw with no GPU context, so they share the (absent) owner; a layer made for one cannot be drawn on a non-Skia canvas.
        Assert.True(layer.CanDraw(other.Canvas));
        using var foreign = raster.CreateCanvas(8, 8);
        Assert.False(layer.CanDraw(foreign));
        Assert.Throws<ArgumentException>(() => layer.Draw(foreign));
    }

    [Fact]
    public void A_layer_with_no_area_or_too_large_is_not_made()
    {
        using var scene = Scene.Create(20, 20);

        Assert.Null(scene.Canvas.CreateRetainedLayer(new Rect(0, 0, 0, 10)));
        Assert.Null(scene.Canvas.CreateRetainedLayer(new Rect(0, 0, double.PositiveInfinity, 10)));
        Assert.Null(scene.Canvas.CreateRetainedLayer(new Rect(0, 0, 20000, 10)));
    }

    [Fact]
    public void Every_layer_is_released_with_its_image_and_recorder()
    {
        var scene = Scene.Create(20, 20);
        var counts = scene.Context.Counts;

        var sealedLayer = RedSquare(scene, new Rect(0, 0, 10, 10));
        var openLayer = scene.Canvas.CreateRetainedLayer(new Rect(0, 0, 10, 10))!;
        Assert.Equal(2, counts.RetainedLayers);

        sealedLayer.Dispose();
        openLayer.Dispose();
        scene.Dispose();

        Assert.Equal(0, counts.RetainedLayers);
        Assert.Equal(0, counts.Total);
    }

    [Fact]
    public void A_retained_layer_drawn_with_a_blur_spreads_beyond_its_bounds_without_being_recorded_again()
    {
        using var scene = Scene.Create(100, 100);
        using var layer = RedSquare(scene, new Rect(40, 40, 20, 20));

        layer.Draw(scene.Canvas, 1.0, new LayerEffect[] { new BlurEffect(4) });

        Assert.True(scene.Px(36, 50).A > 20, $"left of the bounds: {scene.Px(36, 50)}");
        Assert.InRange(scene.Px(40, 50).A, 60, 200);
        Assert.True(scene.Px(50, 50).A > 235, "the middle stays nearly opaque");
        Assert.True(scene.IsClear(20, 50));
    }

    [Fact]
    public void The_same_layer_drawn_with_a_wider_blur_reaches_further()
    {
        // Two draws of one layer in turn (a canvas is finished once its pixels are read, so each is read from its own scene): the content is recorded once per
        // scene and only the effect differs.
        int Reach(double sigma)
        {
            using var scene = Scene.Create(100, 100);
            using var layer = RedSquare(scene, new Rect(40, 40, 20, 20));
            layer.Draw(scene.Canvas, 1.0, new LayerEffect[] { new BlurEffect(sigma) });
            return scene.Px(30, 50).A;
        }

        var narrow = Reach(3);
        var wide = Reach(8);

        Assert.True(wide > narrow + 10, $"narrow {narrow}, wide {wide}");
    }

    [Fact]
    public void An_effect_draw_with_opacity_fades_the_blurred_layer_once()
    {
        using var scene = Scene.Create(100, 100);
        using var layer = RedSquare(scene, new Rect(40, 40, 20, 20));

        layer.Draw(scene.Canvas, 0.5, new LayerEffect[] { new BlurEffect(3) });

        Assert.InRange(scene.Px(50, 50).A, 115, 140);
    }

    [Fact]
    public void An_effect_draw_follows_the_target_transform()
    {
        using var scene = Scene.Create(100, 100);
        using var layer = RedSquare(scene, new Rect(40, 40, 20, 20));

        scene.Canvas.PushTransform(System.Numerics.Matrix3x2.CreateTranslation(-30, -30));
        layer.Draw(scene.Canvas, 1.0, new LayerEffect[] { new BlurEffect(2) });
        scene.Canvas.PopTransform();

        Assert.True(scene.Px(20, 20).A > 235);
        Assert.True(scene.IsClear(55, 55));
    }
}

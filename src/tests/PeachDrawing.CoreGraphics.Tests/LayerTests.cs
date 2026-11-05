using PeachDrawing.Core;
using System.Numerics;

namespace PeachDrawing.CoreGraphics.Tests;

public class LayerTests
{
    [Fact]
    public void A_layer_composites_at_its_opacity()
    {
        using var scene = Scene.Create();
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 0.5)))
        {
            Assert.NotNull(layer);
            layer!.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);
        }

        var px = scene.Px(20, 20);
        Assert.InRange(px.A, 110, 145);
        Assert.InRange(px.R, 110, 145);   // premultiplied
    }

    [Fact]
    public void A_layer_is_isolated_so_overlapping_content_does_not_show_through()
    {
        using var scene = Scene.Create();
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 0.5)))
        {
            layer!.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 30, 30);
            layer.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 10, 10, 30, 30);
        }

        // Inside the overlap the opacity is still one half, not three quarters.
        Assert.InRange(scene.Px(20, 20).A, 110, 145);
    }

    [Fact]
    public void A_layer_is_drawn_where_it_was_begun_under_a_transform()
    {
        using var scene = Scene.Create();
        scene.Canvas.PushTransform(Matrix3x2.CreateTranslation(20, 20));
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 1, Bounds: new Rect(0, 0, 10, 10))))
            layer!.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 10, 10);

        scene.Canvas.PopTransform();

        Assert.True(scene.IsRed(25, 25));
        Assert.True(scene.IsClear(5, 5));
    }

    [Fact]
    public void A_blur_spreads_an_edge()
    {
        using var scene = Scene.Create(40, 40);
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 1, Effects: [new BlurEffect(3, 3)])))
            layer!.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 10, 10, 20, 20);

        var inside = scene.Px(20, 20).A;
        var onEdge = scene.Px(10, 20).A;
        var outside = scene.Px(5, 20).A;
        Assert.True(inside > 230);
        Assert.InRange(onEdge, 60, 200);   // a sharp edge would be 0 or 255
        Assert.True(outside < onEdge);
    }

    [Fact]
    public void A_drop_shadow_appears_offset_beneath_the_content()
    {
        using var scene = Scene.Create(60, 60);
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 1, Effects: [new DropShadowEffect(10, 10, 0.5, 0.5, PaintColor.FromArgb(255, 0, 0, 255))])))
            layer!.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 5, 5, 20, 20);

        Assert.True(scene.IsRed(15, 15));
        Assert.True(scene.IsBlue(32, 32));   // down and to the right, past the content
        Assert.True(scene.IsClear(50, 5));
    }

    [Fact]
    public void A_colour_matrix_layer_recolours_its_content()
    {
        using var scene = Scene.Create();
        var swap = new ColorMatrix(new Matrix4x4(0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1), Vector4.Zero);
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 1, ColorMatrix: swap)))
            layer!.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);

        Assert.True(scene.IsBlue(20, 20));
    }

    [Fact]
    public void A_layer_blend_mode_blends_with_the_page_under_it()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawRectangle(scene.Solid(255, 255, 0), 0, 0, 40, 40);
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 1, BlendMode: PaintBlendMode.Multiply)))
            layer!.Canvas.DrawRectangle(scene.Solid(0, 255, 255), 0, 0, 40, 40);

        var px = scene.Px(20, 20);
        Assert.Equal((0, 255, 0), (px.R, px.G, px.B));
    }

    [Fact]
    public void Layers_and_tiles_are_all_released()
    {
        using var scene = Scene.Create();
        var tile = scene.Canvas.CreateTile(8, 8)!.Value;
        tile.Graphics.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 8, 8);
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 0.5, Effects: [new BlurEffect(1, 1)])))
            layer!.Canvas.DrawImage(tile.Image, new Rect(0, 0, 8, 8));

        tile.Image.Dispose();
        scene.Composer.Finish();

        Assert.Equal(0, scene.Context.Counts.Layers);
        Assert.Equal(0, scene.Context.Counts.Tiles);
    }

    [Fact]
    public void A_layer_left_open_is_finished_with_the_canvas()
    {
        using var scene = Scene.Create();
        var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 1));
        layer!.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);
        scene.Composer.Finish();

        Assert.True(scene.IsRed(20, 20));
        Assert.Equal(0, scene.Context.Counts.Layers);
    }

    [Fact]
    public void Effects_run_on_the_cpu_when_the_gpu_is_off()
    {
        using var scene = Scene.Create(40, 40, useGpu: false);
        Assert.False(scene.Context.EffectsUseGpu);
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 1, Effects: [new BlurEffect(3, 3)])))
            layer!.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 10, 10, 20, 20);

        Assert.InRange(scene.Px(10, 20).A, 60, 200);
    }
}

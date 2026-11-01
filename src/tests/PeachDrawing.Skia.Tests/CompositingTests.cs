using PeachDrawing.Core;
using PeachDrawing.Skia;

namespace PeachDrawing.Skia.Tests;

public class CompositingTests
{
    [Fact]
    public void A_layer_is_faded_as_one_group_so_overlapping_shapes_do_not_show_through_each_other()
    {
        using var scene = Scene.Create();
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 0.5)))
        {
            Assert.NotNull(layer);
            layer!.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 30, 40);
            layer.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 10, 0, 30, 40);
        }

        var alone = scene.Px(5, 20);
        var overlap = scene.Px(20, 20);
        Assert.InRange(alone.A, 120, 135);
        Assert.Equal(alone, overlap);
    }

    [Fact]
    public void A_layers_blend_mode_composites_the_group_against_the_page()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawRectangle(scene.Solid(255, 255, 0), 0, 0, 40, 40);
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(BlendMode: PaintBlendMode.Multiply)))
            layer!.Canvas.DrawRectangle(scene.Solid(0, 255, 255), 0, 0, 40, 40);

        Assert.Equal((0, 255, 0, 255), scene.Px(20, 20));
    }

    [Fact]
    public void A_layer_confined_to_bounds_keeps_nothing_outside_them()
    {
        using var scene = Scene.Create();
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Bounds: new Rect(10, 10, 10, 10))))
            layer!.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);

        Assert.True(scene.IsRed(15, 15));
        Assert.True(scene.IsClear(5, 5));
        Assert.True(scene.IsClear(25, 25));
    }

    [Fact]
    public void A_blur_effect_spreads_a_layers_ink_beyond_its_shape()
    {
        using var scene = Scene.Create(60, 60);
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Bounds: new Rect(0, 0, 60, 60), Effects: [new BlurEffect(3)])))
            layer!.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 20, 20, 20, 20);

        Assert.True(scene.Px(30, 30).A > 200, "the middle stays solid");
        Assert.True(scene.Px(17, 30).A > 0, "ink has spread past the left edge");
        Assert.True(scene.Px(20, 30).A < 255, "the edge is soft");
    }

    [Fact]
    public void A_drop_shadow_effect_paints_an_offset_copy_under_the_shape()
    {
        using var scene = Scene.Create(60, 60);
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Bounds: new Rect(0, 0, 60, 60), Effects: [new DropShadowEffect(10, 10, 0.5, 0.5, PaintColor.FromArgb(255, 0, 0, 255))])))
            layer!.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 10, 10, 20, 20);

        Assert.True(scene.IsRed(15, 15));
        Assert.True(scene.IsBlue(35, 35), "the shadow shows past the shape's corner");
    }

    [Fact]
    public void A_colour_matrix_recolours_a_layer()
    {
        using var scene = Scene.Create();

        // Channels swapped: red becomes blue.
        var swap = new ColorMatrix(new System.Numerics.Matrix4x4(0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1), System.Numerics.Vector4.Zero);
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(ColorMatrix: swap)))
            layer!.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);

        Assert.True(scene.IsBlue(20, 20));
    }

    [Fact]
    public void A_colour_matrix_offset_is_added_to_the_colour()
    {
        using var scene = Scene.Create();
        var lift = new ColorMatrix(System.Numerics.Matrix4x4.Identity, new System.Numerics.Vector4(0, 0.5f, 0, 0));
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(ColorMatrix: lift)))
            layer!.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 40, 40);

        Assert.InRange(scene.Px(20, 20).G, 120, 136);
    }

    [Fact]
    public void A_tile_is_drawn_with_opacity_as_one_flattened_image()
    {
        using var scene = Scene.Create();
        var tile = scene.Canvas.CreateTile(40, 40)!.Value;
        tile.Graphics.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 30, 40);
        tile.Graphics.DrawRectangle(scene.Solid(255, 0, 0), 10, 0, 30, 40);
        tile.Graphics.Dispose();
        using (tile.Image)
            scene.Canvas.DrawImageWithOpacity(tile.Image, new Rect(0, 0, 40, 40), 0.5);

        Assert.Equal(scene.Px(5, 20), scene.Px(20, 20));
        Assert.InRange(scene.Px(5, 20).A, 120, 135);
    }

    [Fact]
    public void A_tile_can_be_recoloured_when_drawn()
    {
        using var scene = Scene.Create();
        var tile = scene.Canvas.CreateTile(40, 40)!.Value;
        tile.Graphics.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);
        tile.Graphics.Dispose();
        var swap = new ColorMatrix(new System.Numerics.Matrix4x4(0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1), System.Numerics.Vector4.Zero);
        using (tile.Image)
            scene.Canvas.DrawImageWithColorMatrix(tile.Image, new Rect(0, 0, 40, 40), swap);

        Assert.True(scene.IsBlue(20, 20));
    }

    [Fact]
    public void A_luminosity_mask_shows_the_content_where_the_mask_is_bright()
    {
        using var scene = Scene.Create();
        var content = scene.Canvas.CreateTile(40, 40)!.Value;
        content.Graphics.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);
        content.Graphics.Dispose();
        var mask = scene.Canvas.CreateTile(40, 40)!.Value;
        mask.Graphics.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 40, 40);
        mask.Graphics.DrawRectangle(scene.Solid(255, 255, 255), 0, 0, 20, 40);
        mask.Graphics.Dispose();

        using (content.Image)
        using (mask.Image)
            scene.Canvas.DrawImageMasked(content.Image, mask.Image, new Rect(0, 0, 40, 40));

        Assert.True(scene.IsRed(10, 20));
        Assert.True(scene.Px(30, 20).A < 10);
    }

    [Fact]
    public void An_alpha_mask_shows_the_content_where_the_mask_is_opaque_and_an_inverted_one_where_it_is_not()
    {
        static void Run(Scene scene, bool invert)
        {
            var content = scene.Canvas.CreateTile(40, 40)!.Value;
            content.Graphics.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);
            content.Graphics.Dispose();
            var mask = scene.Canvas.CreateTile(40, 40)!.Value;
            mask.Graphics.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 20, 40);
            mask.Graphics.Dispose();
            using (content.Image)
            using (mask.Image)
                scene.Canvas.DrawImageAlphaMasked(content.Image, mask.Image, new Rect(0, 0, 40, 40), invert);
        }

        using var plain = Scene.Create();
        Run(plain, false);
        using var inverted = Scene.Create();
        Run(inverted, true);

        Assert.True(plain.IsRed(10, 20));
        Assert.True(plain.Px(30, 20).A < 10);
        Assert.True(inverted.Px(10, 20).A < 10);
        Assert.True(inverted.IsRed(30, 20));
    }

    [Fact]
    public void Two_tiles_blend_with_each_other_and_not_with_the_page()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 255), 0, 0, 40, 40);
        var bottom = scene.Canvas.CreateTile(40, 40)!.Value;
        bottom.Graphics.DrawRectangle(scene.Solid(255, 255, 0), 0, 0, 40, 40);
        bottom.Graphics.Dispose();
        var top = scene.Canvas.CreateTile(40, 40)!.Value;
        top.Graphics.DrawRectangle(scene.Solid(0, 255, 255), 0, 0, 40, 40);
        top.Graphics.Dispose();

        using (bottom.Image)
        using (top.Image)
            scene.Canvas.DrawImageBlendedOver(top.Image, bottom.Image, new Rect(0, 0, 40, 40), PaintBlendMode.Multiply);

        // Yellow x cyan is green; the blue page underneath is covered by the opaque result, not multiplied into it.
        Assert.Equal((0, 255, 0, 255), scene.Px(20, 20));
    }

    [Fact]
    public void A_tile_starts_with_the_transform_of_the_canvas_that_made_it()
    {
        using var scene = Scene.Create();
        scene.Canvas.PushTransform(System.Numerics.Matrix3x2.CreateTranslation(7, 9));
        var tile = scene.Canvas.CreateTile(10, 10)!.Value;

        Assert.Equal(scene.Canvas.CurrentTransform, tile.Graphics.CurrentTransform);
        Assert.True(tile.Graphics.IsOffscreenTile);
        tile.Image.Dispose();
    }

    [Fact]
    public void A_tile_of_no_size_is_refused()
    {
        using var scene = Scene.Create();

        Assert.Null(scene.Canvas.CreateTile(0, 10));
        Assert.Null(scene.Canvas.CreateTile(10, double.PositiveInfinity));
    }

    [Fact]
    public void The_raster_seam_draws_pixels_a_raster_region_painted()
    {
        using var scene = Scene.Create(20, 20);
        using var region = scene.Canvas.BeginRasterSurface(new Rect(0, 0, 20, 20), 96);
        Assert.NotNull(region);
        region!.Graphics.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 10, 20);
        scene.Canvas.DrawRaster(region.Surface);

        Assert.True(scene.IsRed(5, 10));
        Assert.True(scene.IsClear(15, 10));
    }
}

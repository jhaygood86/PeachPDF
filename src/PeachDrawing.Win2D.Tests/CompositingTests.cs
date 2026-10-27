using PeachDrawing.Core;
using PeachDrawing.Win2D;
using PeachImage.Formats.Png;

namespace PeachDrawing.Win2D.Tests;

public class CompositingTests
{
    [Fact]
    public void A_layer_is_composited_as_one_piece_so_overlap_does_not_darken_twice()
    {
        using var scene = Scene.Create();
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 0.5))!)
        {
            layer.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 20, 20);
            layer.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 10, 10, 20, 20);
        }

        var alone = scene.Px(5, 5);
        var overlap = scene.Px(15, 15);
        Assert.InRange(alone.A, 118, 138);
        Assert.InRange(overlap.A, alone.A - 3, alone.A + 3);
        Assert.Equal(0, scene.Context.Counts.Total);
    }

    [Fact]
    public void A_layer_is_drawn_in_the_canvas_coordinates_it_was_begun_in()
    {
        using var scene = Scene.Create();
        scene.Canvas.PushTransform(System.Numerics.Matrix3x2.CreateTranslation(20, 0));
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 1))!)
            layer.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 10, 10);

        scene.Canvas.PopTransform();
        Assert.True(scene.IsRed(25, 5));
        Assert.True(scene.IsClear(5, 5));
    }

    [Fact]
    public void A_layer_colour_matrix_recolours_the_group()
    {
        using var scene = Scene.Create();

        // Swaps red into blue.
        var matrix = new ColorMatrix(new System.Numerics.Matrix4x4(0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1), System.Numerics.Vector4.Zero);
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(ColorMatrix: matrix))!)
            layer.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 20, 20);

        var pixel = scene.Px(10, 10);
        Assert.True(pixel.B > 200 && pixel.R < 40, pixel.ToString());
    }

    [Fact]
    public void A_blur_effect_spreads_the_group_beyond_its_edge()
    {
        using var scene = Scene.Create();
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Effects: new LayerEffect[] { new BlurEffect(3) }))!)
            layer.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 15, 15, 10, 10);

        Assert.True(scene.Px(14, 20).A > 0);
        Assert.True(scene.Px(20, 20).A > 150);
        Assert.True(scene.Px(20, 20).A < 256);
    }

    [Fact]
    public void A_drop_shadow_effect_adds_an_offset_copy_under_the_group()
    {
        using var scene = Scene.Create();
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Effects: new LayerEffect[] { new DropShadowEffect(8, 0, 0.5, 0.5, PaintColor.FromArgb(255, 0, 0, 255)) }))!)
            layer.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 5, 15, 10, 10);

        Assert.True(scene.IsRed(8, 20));
        Assert.True(scene.Px(20, 20).B > 150);
    }

    [Fact]
    public void A_tile_is_an_image_of_what_was_drawn_on_it()
    {
        using var scene = Scene.Create();
        var (tile, image) = scene.Canvas.CreateTile(10, 10)!.Value;
        tile.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 5, 10);
        tile.Dispose();

        scene.Canvas.DrawImage(image, new Rect(20, 20, 20, 20));
        scene.Canvas.DrawImage(image, new Rect(0, 0, 14, 10), new Rect(3, 0, 7, 10));
        image.Dispose();

        Assert.True(scene.IsRed(22, 30));
        Assert.True(scene.IsClear(35, 30));
        Assert.True(scene.IsRed(2, 5));
        Assert.True(scene.IsClear(8, 5));
        Assert.Equal(0, scene.Context.Counts.Total);
    }

    [Fact]
    public void A_tile_brush_repeats_the_tile_across_the_shape()
    {
        using var scene = Scene.Create();
        var (tile, image) = scene.Canvas.CreateTile(10, 10)!.Value;
        tile.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 5, 5);
        tile.Dispose();

        scene.Canvas.DrawRectangle(new TileBrush(image, 10, 10), 0, 0, 40, 40);
        image.Dispose();

        Assert.True(scene.IsRed(2, 2));
        Assert.True(scene.IsRed(32, 22));
        Assert.True(scene.IsClear(7, 7));
    }

    [Fact]
    public void Opacity_blend_and_colour_matrix_draw_a_tile_through_its_own_image_calls()
    {
        using var scene = Scene.Create();
        var (tile, image) = scene.Canvas.CreateTile(10, 10)!.Value;
        tile.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 10, 10);
        tile.Dispose();

        scene.Canvas.DrawImageWithOpacity(image, new Rect(0, 0, 10, 10), 0.5);
        scene.Canvas.DrawImageWithColorMatrix(image, new Rect(20, 0, 10, 10),
            new ColorMatrix(new System.Numerics.Matrix4x4(0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1), System.Numerics.Vector4.Zero));
        image.Dispose();

        Assert.InRange(scene.Px(5, 5).A, 118, 138);
        Assert.True(scene.Px(25, 5).B > 200);
    }

    [Fact]
    public void A_luminosity_mask_shows_the_image_only_where_the_mask_is_light()
    {
        using var scene = Scene.Create();
        var (content, contentImage) = scene.Canvas.CreateTile(40, 40)!.Value;
        content.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);
        content.Dispose();

        var (mask, maskImage) = scene.Canvas.CreateTile(40, 40)!.Value;
        mask.DrawRectangle(scene.Solid(255, 255, 255), 0, 0, 20, 40);
        mask.DrawRectangle(scene.Solid(0, 0, 0), 20, 0, 20, 40);
        mask.Dispose();

        scene.Canvas.DrawImageMasked(contentImage, maskImage, new Rect(0, 0, 40, 40));
        contentImage.Dispose();
        maskImage.Dispose();

        Assert.True(scene.IsRed(10, 20));
        Assert.True(scene.IsClear(30, 20));
    }

    [Fact]
    public void An_alpha_mask_follows_the_masks_opacity_and_can_be_inverted()
    {
        using var scene = Scene.Create(40, 40);
        var (content, contentImage) = scene.Canvas.CreateTile(40, 40)!.Value;
        content.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);
        content.Dispose();

        var (mask, maskImage) = scene.Canvas.CreateTile(40, 40)!.Value;
        mask.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 20, 40);
        mask.Dispose();

        scene.Canvas.DrawImageAlphaMasked(contentImage, maskImage, new Rect(0, 0, 40, 40));
        scene.Canvas.PushTransform(System.Numerics.Matrix3x2.CreateTranslation(0, 0));
        scene.Canvas.PopTransform();
        contentImage.Dispose();
        maskImage.Dispose();

        Assert.True(scene.IsRed(10, 20));
        Assert.True(scene.IsClear(30, 20));
    }

    [Fact]
    public void Two_tiles_blend_with_the_requested_mode()
    {
        using var scene = Scene.Create();
        var (a, bottom) = scene.Canvas.CreateTile(40, 40)!.Value;
        a.DrawRectangle(scene.Solid(255, 255, 0), 0, 0, 40, 40);
        a.Dispose();
        var (b, top) = scene.Canvas.CreateTile(40, 40)!.Value;
        b.DrawRectangle(scene.Solid(0, 255, 255), 0, 0, 40, 40);
        b.Dispose();

        scene.Canvas.DrawImageBlendedOver(top, bottom, new Rect(0, 0, 40, 40), PaintBlendMode.Multiply);
        top.Dispose();
        bottom.Dispose();

        // Yellow multiplied by cyan is green.
        var pixel = scene.Px(20, 20);
        Assert.True(pixel.G > 200 && pixel.R < 40 && pixel.B < 40, pixel.ToString());
    }

    [Fact]
    public void A_decoded_image_is_drawn_into_the_destination_rectangle()
    {
        using var scene = Scene.Create();
        using var source = scene.Context.ImageFromStream(RedPng(4));

        scene.Canvas.DrawImage(source, new Rect(10, 10, 20, 20));
        scene.Canvas.DrawImage(source, new Rect(0, 0, 8, 8), new Rect(0, 0, 2, 2), ImageSampling.Nearest);

        Assert.True(scene.IsRed(20, 20));
        Assert.True(scene.IsClear(5, 30));
        Assert.True(scene.IsRed(3, 3));
    }

    [Fact]
    public void A_raster_surface_is_drawn_back_at_the_rectangle_it_records()
    {
        using var scene = Scene.Create();
        using (var region = scene.Canvas.BeginRasterSurface(new Rect(10, 10, 20, 20))!)
        {
            region.Graphics.DrawRectangle(scene.Context.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 10, 10, 20, 20);
            scene.Canvas.DrawRaster(region.Surface);
        }

        Assert.True(scene.IsRed(20, 20));
        Assert.True(scene.IsClear(5, 5));
    }

    [Fact]
    public void A_blend_mode_with_a_session_rule_is_applied_and_removed_by_its_pop()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawRectangle(scene.Solid(100, 0, 0), 0, 0, 40, 40);
        scene.Canvas.PushBlendMode(PaintBlendMode.Plus);
        scene.Canvas.DrawRectangle(scene.Solid(100, 0, 0), 0, 0, 20, 40);
        scene.Canvas.PopBlendMode();
        scene.Canvas.DrawRectangle(scene.Solid(100, 0, 0), 0, 20, 40, 20);

        Assert.InRange(scene.Px(10, 5).R, 190, 210);
        Assert.InRange(scene.Px(30, 5).R, 95, 105);
    }

    private static MemoryStream RedPng(int size)
    {
        var raster = new RasterRenderContext();
        using var canvas = raster.CreateCanvas(size, size);
        canvas.DrawRectangle(raster.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, size, size);
        var stream = new MemoryStream();
        canvas.Save(stream, "png", new PngEncoderOptions());
        stream.Position = 0;
        return stream;
    }
}

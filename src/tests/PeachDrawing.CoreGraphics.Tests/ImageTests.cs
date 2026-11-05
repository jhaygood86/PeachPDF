using PeachDrawing.Core;

namespace PeachDrawing.CoreGraphics.Tests;

public class ImageTests
{
    private static PeachDrawing.Core.Image Load(Scene scene, byte[] bytes) => scene.Context.ImageFromStream(new MemoryStream(bytes));

    [Fact]
    public void An_image_is_drawn_upright_and_unmirrored()
    {
        using var scene = Scene.Create(20, 20);
        using var image = Load(scene, Scene.TopBottomPng(4, 8));
        scene.Canvas.DrawImage(image, new Rect(0, 0, 20, 20));

        Assert.True(scene.IsRed(10, 3));
        Assert.True(scene.IsBlue(10, 17));
    }

    [Fact]
    public void An_image_keeps_left_and_right()
    {
        using var scene = Scene.Create(20, 8);
        using var image = Load(scene, Scene.TwoColourPng(8, 4));
        scene.Canvas.DrawImage(image, new Rect(0, 0, 20, 8));

        Assert.True(scene.IsRed(3, 4));
        Assert.True(scene.IsBlue(17, 4));
    }

    [Fact]
    public void A_source_rectangle_crops()
    {
        using var scene = Scene.Create(20, 8);
        using var image = Load(scene, Scene.TwoColourPng(8, 4));
        scene.Canvas.DrawImage(image, new Rect(0, 0, 20, 8), new Rect(4, 0, 4, 4));   // the blue half only

        Assert.True(scene.IsBlue(3, 4));
        Assert.True(scene.IsBlue(17, 4));
    }

    [Fact]
    public void Opacity_scales_the_image()
    {
        using var scene = Scene.Create(10, 10);
        using var image = Load(scene, Scene.TwoColourPng(8, 4));
        scene.Canvas.DrawImageWithOpacity(image, new Rect(0, 0, 10, 10), 0.5);

        var px = scene.Px(2, 5);
        Assert.InRange(px.A, 110, 145);
    }

    [Fact]
    public void A_colour_matrix_recolours()
    {
        using var scene = Scene.Create(20, 8);
        using var image = Load(scene, Scene.TwoColourPng(8, 4));

        // swap red and blue
        var matrix = new ColorMatrix(new System.Numerics.Matrix4x4(0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1), System.Numerics.Vector4.Zero);
        scene.Canvas.DrawImageWithColorMatrix(image, new Rect(0, 0, 20, 8), matrix);

        Assert.True(scene.IsBlue(3, 4));
        Assert.True(scene.IsRed(17, 4));
    }

    [Fact]
    public void A_luminosity_mask_keeps_what_is_bright()
    {
        using var scene = Scene.Create(20, 8);
        using var content = Load(scene, Scene.TwoColourPng(8, 4));
        var mask = scene.Canvas.CreateTile(8, 4)!.Value;
        mask.Graphics.DrawRectangle(scene.Solid(255, 255, 255), 0, 0, 4, 4);   // left half white
        mask.Graphics.DrawRectangle(scene.Solid(0, 0, 0), 4, 0, 4, 4);         // right half black
        scene.Canvas.DrawImageMasked(content, mask.Image, new Rect(0, 0, 20, 8));

        Assert.True(scene.IsRed(3, 4));
        Assert.True(scene.IsClear(17, 4));
        mask.Image.Dispose();
    }

    [Fact]
    public void A_luminosity_mask_counts_a_half_transparent_white_as_half_visible()
    {
        using var scene = Scene.Create(20, 8);
        using var content = Load(scene, Scene.TwoColourPng(8, 4));
        var mask = scene.Canvas.CreateTile(8, 4)!.Value;
        mask.Graphics.DrawRectangle(scene.Solid(255, 255, 255, 128), 0, 0, 8, 4);
        scene.Canvas.DrawImageMasked(content, mask.Image, new Rect(0, 0, 20, 8));

        Assert.InRange(scene.Px(3, 4).A, 100, 160);
        mask.Image.Dispose();
    }

    [Fact]
    public void An_alpha_mask_keeps_what_is_opaque_and_can_be_inverted()
    {
        using var scene = Scene.Create(40, 8);
        using var content = Load(scene, Scene.TwoColourPng(8, 4));
        var mask = scene.Canvas.CreateTile(8, 4)!.Value;
        mask.Graphics.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 4, 4);   // left half opaque
        scene.Canvas.DrawImageAlphaMasked(content, mask.Image, new Rect(0, 0, 20, 8));
        scene.Canvas.DrawImageAlphaMasked(content, mask.Image, new Rect(20, 0, 20, 8), invert: true);

        Assert.True(scene.IsRed(3, 4));
        Assert.True(scene.IsClear(17, 4));
        Assert.True(scene.IsClear(23, 4));
        Assert.True(scene.IsBlue(37, 4));
        mask.Image.Dispose();
    }

    [Fact]
    public void Blended_over_blends_two_images_with_each_other()
    {
        using var scene = Scene.Create(10, 10);
        var bottom = scene.Canvas.CreateTile(10, 10)!.Value;
        var top = scene.Canvas.CreateTile(10, 10)!.Value;
        bottom.Graphics.DrawRectangle(scene.Solid(255, 255, 0), 0, 0, 10, 10);
        top.Graphics.DrawRectangle(scene.Solid(0, 255, 255), 0, 0, 10, 10);
        scene.Canvas.DrawImageBlendedOver(top.Image, bottom.Image, new Rect(0, 0, 10, 10), PaintBlendMode.Multiply);

        var px = scene.Px(5, 5);
        Assert.Equal((0, 255, 0), (px.R, px.G, px.B));
        top.Image.Dispose();
        bottom.Image.Dispose();
    }

    [Fact]
    public void A_foreign_image_is_converted_from_its_pixels()
    {
        using var scene = Scene.Create(10, 10);
        using var other = Scene.Create(4, 4);
        other.Canvas.DrawRectangle(other.Solid(255, 0, 0), 0, 0, 4, 4);
        var pixels = other.Composer.ToPixelBuffer();
        using var foreign = new PixelsImage(pixels);
        scene.Canvas.DrawImage(foreign, new Rect(0, 0, 10, 10));

        Assert.True(scene.IsRed(5, 5));
    }

    [Fact]
    public void The_raster_seam_round_trips_pixels()
    {
        using var scene = Scene.Create(20, 20);
        var region = scene.Canvas.BeginRasterSurface(new Rect(0, 0, 20, 20), 72);
        Assert.NotNull(region);
        using (region)
        {
            region!.Graphics.DrawRectangle(region.Graphics.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, 20, 20);
            scene.Canvas.DrawRaster(region.Surface);
        }

        Assert.True(scene.IsRed(10, 10));
    }

    private sealed class PixelsImage(PixelBuffer pixels) : PeachDrawing.Core.Image
    {
        public override double Width => pixels.Width;

        public override double Height => pixels.Height;

        public override bool Interpolate { get; set; } = true;

        public override PixelBuffer? GetPixels() => pixels;

        public override void Dispose()
        {
        }
    }
}

public class CodecTests
{
    [Theory]
    [InlineData(CoreGraphicsImageCodec.PeachImage)]
    [InlineData(CoreGraphicsImageCodec.Native)]
    [InlineData(CoreGraphicsImageCodec.Auto)]
    public void Every_codec_draws_a_png_the_same(CoreGraphicsImageCodec codec)
    {
        using var scene = Scene.Create(20, 8, codec: codec);
        using var image = scene.Context.ImageFromStream(new MemoryStream(Scene.TwoColourPng(8, 4)));
        scene.Canvas.DrawImage(image, new Rect(0, 0, 20, 8));

        Assert.True(scene.IsRed(3, 4));
        Assert.True(scene.IsBlue(17, 4));
        Assert.Equal(8, (int)image.Width);
        Assert.Equal(4, (int)image.Height);
    }

    [Fact]
    public void Auto_with_a_gpu_keeps_the_encoded_bytes_until_the_first_draw()
    {
        if (MetalDevice() is null)
            return;

        using var scene = Scene.Create(20, 8, codec: CoreGraphicsImageCodec.Auto);
        using var image = scene.Context.ImageFromStream(new MemoryStream(Scene.TwoColourPng(8, 4)));
        var own = Assert.IsType<CoreGraphicsImage>(image);
        Assert.True(own.IsEncodedOnly);
        scene.Canvas.DrawImage(image, new Rect(0, 0, 20, 8));
        Assert.False(own.IsEncodedOnly);
    }

    [Fact]
    public void Auto_without_a_gpu_decodes_with_PeachImage()
    {
        using var scene = Scene.Create(20, 8, codec: CoreGraphicsImageCodec.Auto, useGpu: false);
        using var image = scene.Context.ImageFromStream(new MemoryStream(Scene.TwoColourPng(8, 4)));
        var own = Assert.IsType<CoreGraphicsImage>(image);
        Assert.True(own.HasManagedPixels);
    }

    [Fact]
    public void Native_decodes_with_ImageIO_into_pixels()
    {
        using var scene = Scene.Create(20, 8, codec: CoreGraphicsImageCodec.Native);
        using var image = scene.Context.ImageFromStream(new MemoryStream(Scene.TwoColourPng(8, 4)));
        Assert.NotNull(image.GetPixels());
    }

    [Fact]
    public void A_format_ImageIO_cannot_read_falls_back_to_PeachImage_unless_that_is_off()
    {
        var notAnImage = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        using var scene = Scene.Create(codec: CoreGraphicsImageCodec.Native);
        Assert.Throws<InvalidDataException>(() => scene.Context.ImageFromStream(new MemoryStream(notAnImage)));

        scene.Context.FallbackToPeachImage = false;
        Assert.True(scene.Context.SupportsImageType("image/png"));
        Assert.False(scene.Context.SupportsImageType("image/jxl"));
    }

    [Fact]
    public void Supported_types_are_PeachImages_unless_native_only()
    {
        using var scene = Scene.Create(codec: CoreGraphicsImageCodec.PeachImage);
        Assert.True(scene.Context.SupportsImageType("image/jxl"));
    }

    [Theory]
    [InlineData(CoreGraphicsImageCodec.PeachImage, true, "PeachImage")]
    [InlineData(CoreGraphicsImageCodec.Native, false, "Native")]
    [InlineData(CoreGraphicsImageCodec.Auto, true, "LazyNative")]
    [InlineData(CoreGraphicsImageCodec.Auto, false, "PeachImage")]
    public void The_decoder_choice_follows_the_codec_and_the_gpu(CoreGraphicsImageCodec codec, bool gpu, string expected) =>
        Assert.Equal(expected, CoreGraphicsRenderContext.ChooseDecoder(codec, gpu).ToString());

    private static object? MetalDevice() => Metal.MTLDevice.SystemDefault;
}

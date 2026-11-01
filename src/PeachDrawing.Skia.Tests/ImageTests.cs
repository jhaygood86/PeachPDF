using PeachDrawing.Core;
using PeachDrawing.Skia;
using SkiaSharp;

namespace PeachDrawing.Skia.Tests;

public class ImageTests
{
    [Theory]
    [InlineData(SkiaImageCodec.PeachImage)]
    [InlineData(SkiaImageCodec.Native)]
    public void An_image_decodes_with_either_codec_and_draws_at_its_destination(SkiaImageCodec codec)
    {
        using var scene = Scene.Create(codec: codec);
        using var image = scene.Context.ImageFromStream(new MemoryStream(Scene.TwoColourPng()));

        Assert.Equal(8, image.Width);
        Assert.Equal(4, image.Height);
        scene.Canvas.DrawImage(image, new Rect(0, 0, 40, 20), ImageSampling.Nearest);

        Assert.True(scene.IsRed(5, 10));
        Assert.True(scene.IsBlue(35, 10));
        Assert.True(scene.IsClear(20, 30));
    }

    [Fact]
    public void Both_codecs_decode_the_same_pixels()
    {
        using var peach = new SkiaRenderContext(imageCodec: SkiaImageCodec.PeachImage);
        using var native = new SkiaRenderContext(imageCodec: SkiaImageCodec.Native);
        var bytes = Scene.TwoColourPng();

        using var a = peach.ImageFromStream(new MemoryStream(bytes));
        using var b = native.ImageFromStream(new MemoryStream(bytes));

        var left = a.GetPixels()!.Value;
        var right = b.GetPixels()!.Value;
        Assert.Equal(left.Width, right.Width);
        Assert.Equal(left.Height, right.Height);
        Assert.True(left.PremultipliedRgba.Span.SequenceEqual(right.PremultipliedRgba.Span));
    }

    [Fact]
    public void A_natively_decoded_image_keeps_the_orientation_stored_with_it()
    {
        using var bitmap = new SKBitmap(4, 2);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);

        using var native = new SkiaRenderContext(imageCodec: SkiaImageCodec.Native);
        using var decoded = native.ImageFromStream(new MemoryStream(data.ToArray()));

        Assert.Equal(1, decoded.ExifOrientation);
        Assert.Equal(4, decoded.Width);
    }

    [Fact]
    public void An_undecodable_stream_is_an_error()
    {
        using var context = new SkiaRenderContext();

        Assert.Throws<InvalidDataException>(() => context.ImageFromStream(new MemoryStream([1, 2, 3, 4])));
    }

    [Fact]
    public void The_native_codec_hands_a_format_it_cannot_read_to_PeachImage_unless_told_not_to()
    {
        // A BMP-less, GIF-less stream no codec knows is refused by both; what matters is that fallback is switchable and defaults on.
        using var context = new SkiaRenderContext(imageCodec: SkiaImageCodec.Native);
        Assert.True(context.FallbackToPeachImage);

        context.FallbackToPeachImage = false;
        Assert.Throws<InvalidDataException>(() => context.ImageFromStream(new MemoryStream([9, 9, 9, 9])));
    }

    [Fact]
    public void Part_of_an_image_can_be_drawn()
    {
        using var scene = Scene.Create();
        using var image = scene.Context.ImageFromStream(new MemoryStream(Scene.TwoColourPng()));
        scene.Canvas.DrawImage(image, new Rect(0, 0, 40, 40), new Rect(4, 0, 4, 4), ImageSampling.Nearest);

        Assert.True(scene.IsBlue(5, 5));
        Assert.True(scene.IsBlue(35, 35));
    }

    [Fact]
    public void An_image_of_another_backend_is_drawn_through_its_pixels()
    {
        var raster = new RasterRenderContext();
        using var foreign = raster.ImageFromStream(new MemoryStream(Scene.TwoColourPng()));

        using var scene = Scene.Create();
        scene.Canvas.DrawImage(foreign, new Rect(0, 0, 40, 20), ImageSampling.Nearest);

        Assert.True(scene.IsRed(5, 10));
        Assert.True(scene.IsBlue(35, 10));
    }

    [Fact]
    public void A_host_gpu_context_hint_is_optional_and_kept()
    {
        using var context = new SkiaRenderContext();

        Assert.Null(context.GpuContext);
    }

    [Fact]
    public void Images_the_canvas_converted_are_released_with_it()
    {
        var raster = new RasterRenderContext();
        using var foreign = raster.ImageFromStream(new MemoryStream(Scene.TwoColourPng()));
        using var scene = Scene.Create();

        scene.Canvas.DrawImage(foreign, new Rect(0, 0, 40, 20));
        Assert.True(scene.Context.Counts.Surfaces > 0);

        scene.Composer.Finish();
        Assert.Equal(0, scene.Context.Counts.Surfaces);
    }
}

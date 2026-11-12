using PeachDrawing.Core;
using PeachDrawing.Skia;
using SkiaSharp;

namespace PeachDrawing.Skia.Tests;

/// <summary>
/// The default codec keeps an image Skia can decode as its encoded bytes and lets Skia decode it on first use (into the texture, on a GPU
/// surface). These check that it does, that what it draws is what PeachImage would have drawn, and that what Skia cannot decode still works.
/// </summary>
public class AutoCodecTests
{
    // 16 x 8: a horizontal red-to-blue gradient with a translucent green band across the bottom half.
    private static byte[] Encode(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(16, 8, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(16, 0), [SKColors.Red, SKColors.Blue], SKShaderTileMode.Clamp);
            using var gradient = new SKPaint { Shader = shader };
            canvas.DrawRect(0, 0, 16, 8, gradient);
            using var band = new SKPaint { Color = new SKColor(0, 200, 0, 120) };
            canvas.DrawRect(0, 4, 16, 4, band);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 95);
        return data.ToArray();
    }

    private static (int R, int G, int B, int A)[] Drawn(SkiaImageCodec codec, byte[] bytes)
    {
        using var scene = Scene.Create(16, 8, codec: codec);
        using var image = scene.Context.ImageFromStream(new MemoryStream(bytes));
        scene.Canvas.DrawImage(image, new Rect(0, 0, 16, 8), ImageSampling.Nearest);

        var pixels = new List<(int, int, int, int)>();
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 16; x++)
                pixels.Add(scene.Px(x, y));

        return [.. pixels];
    }

    private static int Worst((int R, int G, int B, int A)[] left, (int R, int G, int B, int A)[] right)
    {
        var worst = 0;
        for (var i = 0; i < left.Length; i++)
        {
            worst = Math.Max(worst, Math.Max(Math.Max(Math.Abs(left[i].R - right[i].R), Math.Abs(left[i].G - right[i].G)),
                Math.Max(Math.Abs(left[i].B - right[i].B), Math.Abs(left[i].A - right[i].A))));
        }

        return worst;
    }

    [Fact]
    public void Auto_is_the_default_codec()
    {
        using var context = new SkiaRenderContext();

        Assert.Equal(SkiaImageCodec.Auto, context.ImageCodec);
        Assert.True(context.FallbackToPeachImage);
    }

    [Fact]
    public void A_supported_image_is_decoded_by_Skia_and_not_kept_as_managed_pixels()
    {
        using var scene = Scene.Create(16, 8, codec: SkiaImageCodec.Auto);
        using var image = Assert.IsType<SkiaImage>(scene.Context.ImageFromStream(new MemoryStream(Encode(SKEncodedImageFormat.Png))));

        Assert.Equal(16, image.Width);
        Assert.Equal(8, image.Height);
        Assert.False(image.HasManagedPixels);

        scene.Canvas.DrawImage(image, new Rect(0, 0, 16, 8), ImageSampling.Nearest);
        Assert.True(scene.OpaquePixels() > 100);
        Assert.False(image.HasManagedPixels);

        // Reading the pixels back works for such an image, and they are what PeachImage decodes (to a rounding step).
        var pixels = image.GetPixels()!.Value.PremultipliedRgba.Span;
        using var peach = new SkiaRenderContext(imageCodec: SkiaImageCodec.PeachImage);
        using var expected = peach.ImageFromStream(new MemoryStream(Encode(SKEncodedImageFormat.Png)));
        var other = expected.GetPixels()!.Value.PremultipliedRgba.Span;
        Assert.Equal(other.Length, pixels.Length);
        for (var i = 0; i < pixels.Length; i++)
            Assert.True(Math.Abs(pixels[i] - other[i]) <= 2, $"byte {i}: {pixels[i]} vs {other[i]}");
    }

    [Fact]
    public void A_png_with_alpha_draws_the_same_as_PeachImage_gives()
    {
        var bytes = Encode(SKEncodedImageFormat.Png);

        Assert.True(Worst(Drawn(SkiaImageCodec.Auto, bytes), Drawn(SkiaImageCodec.PeachImage, bytes)) <= 2);
    }

    [Fact]
    public void A_jpeg_draws_close_to_what_PeachImage_gives()
    {
        // The two decoders round their inverse transforms differently, so the pixels agree to a few levels, not exactly.
        var bytes = Encode(SKEncodedImageFormat.Jpeg);

        Assert.True(Worst(Drawn(SkiaImageCodec.Auto, bytes), Drawn(SkiaImageCodec.PeachImage, bytes)) <= 12);
    }

    [Fact]
    public void An_image_with_a_stored_orientation_is_left_to_PeachImage()
    {
        // A JPEG whose EXIF block says "rotate 90 degrees clockwise" (orientation 6): the stored pixels stay 16 x 8 and the orientation is
        // reported for the canvas to apply, exactly as for every other image.
        byte[] exif =
        [
            0xFF, 0xE1, 0x00, 0x22, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'I', (byte)'I', 0x2A, 0, 8, 0, 0, 0,
            1, 0, 0x12, 0x01, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0,
            0, 0, 0, 0,
        ];
        var plain = Encode(SKEncodedImageFormat.Jpeg);
        var bytes = plain[..2].Concat(exif).Concat(plain[2..]).ToArray();

        using var context = new SkiaRenderContext();
        using var image = Assert.IsType<SkiaImage>(context.ImageFromStream(new MemoryStream(bytes)));

        Assert.Equal(6, image.ExifOrientation);
        Assert.True(image.HasManagedPixels);
        Assert.Equal(16, image.Width);
        Assert.Equal(8, image.Height);
    }

    [Fact]
    public void Data_Skia_cannot_decode_is_refused_like_before()
    {
        using var context = new SkiaRenderContext();

        Assert.Throws<InvalidDataException>(() => context.ImageFromStream(new MemoryStream([9, 9, 9, 9])));
    }

    [Fact]
    public void The_images_native_resources_are_released_with_it()
    {
        using var context = new SkiaRenderContext();
        using (var image = context.ImageFromStream(new MemoryStream(Encode(SKEncodedImageFormat.Png))))
            Assert.Equal(1, context.Counts.Surfaces);

        Assert.Equal(0, context.Counts.Surfaces);
    }
}

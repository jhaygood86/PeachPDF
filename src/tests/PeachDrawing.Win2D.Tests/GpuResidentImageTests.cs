using PeachDrawing.Core;
using PeachImage.Formats.Jpeg;
using PeachImage.Formats.Png;

namespace PeachDrawing.Win2D.Tests;

/// <summary>
/// The default codec keeps an image Windows can decode as its encoded bytes and has Windows decode it into the device's bitmap. These
/// check that it does, that the result is what PeachImage would have drawn, and that what Windows cannot decode still works.
/// </summary>
public class GpuResidentImageTests
{
    // A 16 x 8 image: a horizontal gradient from red to blue over a vertical fade in alpha (the alpha is dropped for JPEG).
    private static byte[] Encode(string format, PeachImage.EncoderOptions options)
    {
        var raster = new RasterRenderContext();
        using var canvas = raster.CreateCanvas(16, 8);
        canvas.DrawRectangle(raster.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(16, 0),
            [(PaintColor.FromArgb(255, 255, 0, 0), 0), (PaintColor.FromArgb(255, 0, 0, 255), 1)]), 0, 0, 16, 8);
        canvas.DrawRectangle(raster.GetSolidBrush(PaintColor.FromArgb(120, 0, 200, 0)), 0, 4, 16, 4);
        using var stream = new MemoryStream();
        canvas.Save(stream, format, options);
        return stream.ToArray();
    }

    private static byte[] Png() => Encode("png", new PngEncoderOptions());

    private static byte[] Jpeg() => Encode("jpeg", new JpegEncoderOptions());

    private static (int R, int G, int B, int A)[] Drawn(Win2dImageCodec codec, byte[] bytes)
    {
        using var context = Win2dRenderContext.CreateSoftware(imageCodec: codec);
        context.PixelsPerPoint = 1;
        using var scene = new Scene(context, 16, 8);
        using var image = context.ImageFromStream(new MemoryStream(bytes));
        scene.Canvas.DrawImage(image, new Rect(0, 0, 16, 8), ImageSampling.Nearest);

        var pixels = new List<(int, int, int, int)>();
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 16; x++)
                pixels.Add(scene.Px(x, y));

        return [.. pixels];
    }

    [Fact]
    public void A_supported_image_stays_on_the_device_until_its_pixels_are_asked_for()
    {
        using var context = Win2dRenderContext.CreateSoftware();
        using var scene = new Scene(context, 16, 8);
        using var image = Assert.IsType<Win2dImage>(context.ImageFromStream(new MemoryStream(Png())));

        Assert.Equal(16, image.Width);
        Assert.Equal(8, image.Height);
        Assert.False(image.HasManagedPixels);

        scene.Canvas.DrawImage(image, new Rect(0, 0, 16, 8), ImageSampling.Nearest);
        Assert.True(scene.OpaquePixels() > 100);
        Assert.False(image.HasManagedPixels);

        // Asking for the pixels decodes them once, and they are what PeachImage decodes.
        var pixels = image.GetPixels()!.Value;
        Assert.True(image.HasManagedPixels);
        using var peach = Win2dRenderContext.CreateSoftware(imageCodec: Win2dImageCodec.PeachImage);
        using var expected = peach.ImageFromStream(new MemoryStream(Png()));
        Assert.True(pixels.PremultipliedRgba.Span.SequenceEqual(expected.GetPixels()!.Value.PremultipliedRgba.Span));
    }

    [Fact]
    public void A_png_with_alpha_draws_the_same_pixels_as_PeachImage_gives()
    {
        var bytes = Png();
        var auto = Drawn(Win2dImageCodec.Auto, bytes);
        var peach = Drawn(Win2dImageCodec.PeachImage, bytes);

        for (var i = 0; i < auto.Length; i++)
        {
            Assert.True(Math.Max(Math.Max(Math.Abs(auto[i].R - peach[i].R), Math.Abs(auto[i].G - peach[i].G)),
                Math.Max(Math.Abs(auto[i].B - peach[i].B), Math.Abs(auto[i].A - peach[i].A))) <= 2, $"pixel {i}: {auto[i]} vs {peach[i]}");
        }
    }

    [Fact]
    public void A_jpeg_draws_close_to_what_PeachImage_gives()
    {
        // The two decoders round their inverse transforms differently, so the pixels agree to a few levels, not exactly.
        var bytes = Jpeg();
        var auto = Drawn(Win2dImageCodec.Auto, bytes);
        var peach = Drawn(Win2dImageCodec.PeachImage, bytes);

        var worst = 0;
        for (var i = 0; i < auto.Length; i++)
            worst = Math.Max(worst, Math.Max(Math.Max(Math.Abs(auto[i].R - peach[i].R), Math.Abs(auto[i].G - peach[i].G)), Math.Abs(auto[i].B - peach[i].B)));

        Assert.True(worst <= 12, $"largest channel difference {worst}");
    }

    [Fact]
    public void An_image_with_a_stored_orientation_is_not_kept_on_the_device_so_it_is_not_rotated_twice()
    {
        // Windows rotates such an image when it loads it into a bitmap, and PeachDrawing rotates it from ExifOrientation: only one may.
        // A JPEG whose EXIF block says "rotate 90 degrees clockwise" (orientation 6): the stored pixels are 16 x 8.
        byte[] exif =
        [
            0xFF, 0xE1, 0x00, 0x22, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'I', (byte)'I', 0x2A, 0, 8, 0, 0, 0,
            1, 0, 0x12, 0x01, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0,
            0, 0, 0, 0,
        ];
        var plain = Jpeg();
        var bytes = plain[..2].Concat(exif).Concat(plain[2..]).ToArray();

        using var context = Win2dRenderContext.CreateSoftware();
        using var image = Assert.IsType<Win2dImage>(context.ImageFromStream(new MemoryStream(bytes)));

        Assert.Equal(6, image.ExifOrientation);
        Assert.True(image.HasManagedPixels, "decoded by PeachImage, which leaves the orientation to the canvas");

        // The bitmap on the device is the stored 16 x 8, not rotated by Windows as well as by the canvas.
        var bitmap = image.GetBitmap(context.Device);
        Assert.Equal(16u, bitmap.SizeInPixels.Width);
        Assert.Equal(8u, bitmap.SizeInPixels.Height);
    }

    [Fact]
    public void Data_Windows_cannot_decode_is_refused_like_before()
    {
        using var context = Win2dRenderContext.CreateSoftware();
        Assert.Throws<InvalidDataException>(() => context.ImageFromStream(new MemoryStream([9, 9, 9, 9])));
    }

    [Fact]
    public void An_image_can_be_drawn_on_two_devices()
    {
        using var first = Win2dRenderContext.CreateSoftware();
        using var image = Assert.IsType<Win2dImage>(first.ImageFromStream(new MemoryStream(Png())));
        using var second = Win2dRenderContext.CreateSoftware();

        var a = image.GetBitmap(first.Device);
        var b = image.GetBitmap(second.Device);

        Assert.NotSame(a, b);
        Assert.Equal(16u, b.SizeInPixels.Width);
        Assert.False(image.HasManagedPixels);
    }
}

using System.Numerics;
using System.Security.Cryptography;
using PeachDrawing.Core;

namespace PeachDrawing.Tests;

/// <summary>
/// Pins the exact pixels image draws produce: one to one at whole and fractional offsets, enlarged, shrunk by factors that take the
/// pre-averaging path (whole and partial blocks), rotated, cropped, and drawn with each sampling mode. The hashes were recorded from
/// the per-pixel sampling and block averaging that preceded the vector rewrite. A hash cannot say what changed: compare pixel by pixel.
/// </summary>
public class ImageGoldenTests
{
    private const int CanvasWidth = 150;
    private const int CanvasHeight = 120;

    /// <summary>A picture of fixed content with transparency, hard edges and smooth ramps (premultiplied, so its color never exceeds its alpha).</summary>
    private static Image Picture(RasterCanvas canvas, int width, int height)
    {
        var (graphics, image) = canvas.CreateTile(width, height)!.Value;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var alpha = (x / 9 + y / 7) % 5 == 0 ? 0 : 120 + (x * 3 + y * 5) % 136;
                graphics.DrawRectangle(graphics.GetSolidBrush(PaintColor.FromArgb(alpha, (x * 37) % 256, (y * 53) % 256, (x * y) % 256)), x, y, 1, 1);
            }
        }

        graphics.Dispose();
        return image;
    }

    private static string Render(Action<RasterCanvas, Image> draw, int imageWidth = 101, int imageHeight = 83)
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(CanvasWidth, CanvasHeight);
        canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 235, 240, 250)), 0, 0, CanvasWidth, CanvasHeight);
        draw(canvas, Picture(canvas, imageWidth, imageHeight));
        return Convert.ToHexString(SHA256.HashData(canvas.ToPixelBuffer().PremultipliedRgba.Span));
    }

    [Theory]
    [InlineData(ImageSampling.Bilinear, "IMAGE_1TO1_BILINEAR")]
    [InlineData(ImageSampling.Nearest, "IMAGE_1TO1_NEAREST")]
    [InlineData(ImageSampling.Bicubic, "IMAGE_1TO1_BICUBIC")]
    public void OneToOneAtAWholePixelOffset_IsByteIdentical(ImageSampling sampling, string name) =>
        ImageGolden.Check(name, Render((c, image) => c.DrawImage(image, new Rect(12, 9, 101, 83), sampling)));

    [Fact]
    public void OneToOneAtAFractionalOffset_IsByteIdentical() =>
        ImageGolden.Check("IMAGE_1TO1_FRACTIONAL", Render((c, image) => c.DrawImage(image, new Rect(12.4, 9.25, 101, 83), ImageSampling.Bilinear)));

    [Fact]
    public void OneToOneCropped_IsByteIdentical() =>
        ImageGolden.Check("IMAGE_1TO1_CROPPED", Render((c, image) => c.DrawImage(image, new Rect(5, 5, 60, 50), new Rect(20, 15, 60, 50), ImageSampling.Bilinear)));

    [Theory]
    [InlineData(ImageSampling.Bilinear, "IMAGE_ENLARGED_BILINEAR")]
    [InlineData(ImageSampling.Nearest, "IMAGE_ENLARGED_NEAREST")]
    [InlineData(ImageSampling.Bicubic, "IMAGE_ENLARGED_BICUBIC")]
    public void Enlarged_IsByteIdentical(ImageSampling sampling, string name) =>
        ImageGolden.Check(name, Render((c, image) => c.DrawImage(image, new Rect(3, 4, 137.5, 112.3), sampling)));

    [Theory]
    [InlineData(40, 33, "IMAGE_SHRUNK_FACTOR_2")]
    [InlineData(31, 26, "IMAGE_SHRUNK_FACTOR_3")]
    [InlineData(19, 15, "IMAGE_SHRUNK_FACTOR_5_PARTIAL_BLOCKS")]
    [InlineData(70, 58, "IMAGE_SHRUNK_JUST_UNDER_FACTOR_2")]
    public void Shrunk_IsByteIdentical(double width, double height, string name) =>
        ImageGolden.Check(name, Render((c, image) => c.DrawImage(image, new Rect(7.3, 6.1, width, height), ImageSampling.Bilinear)));

    [Fact]
    public void ShrunkOddSizedImage_IsByteIdentical() =>
        ImageGolden.Check("IMAGE_SHRUNK_ODD_SOURCE", Render((c, image) => c.DrawImage(image, new Rect(10, 10, 29, 25), ImageSampling.Bilinear), 203, 157));

    [Fact]
    public void Rotated_IsByteIdentical() => ImageGolden.Check("IMAGE_ROTATED", Render((c, image) =>
    {
        c.PushTransform(Matrix3x2.CreateRotation(0.4f) * Matrix3x2.CreateTranslation(60, 5));
        c.DrawImage(image, new Rect(0, 0, 90, 70), ImageSampling.Bilinear);
        c.PopTransform();
    }));
}

/// <summary>The recorded hashes of <see cref="ImageGoldenTests"/>, by case name.</summary>
internal static class ImageGolden
{
    /// <summary>Fails unless <paramref name="actual"/> is the hash recorded for <paramref name="name"/>; the message carries the whole hash, to record a new case.</summary>
    public static void Check(string name, string actual) =>
        Assert.True(Hashes.TryGetValue(name, out var hash) && hash == actual, $"{name}: recorded {(Hashes.TryGetValue(name, out var h) ? h : "(none)")}, actual {actual}");

    private static readonly Dictionary<string, string> Hashes = new()
    {
        ["IMAGE_1TO1_BICUBIC"] = "E9DB3C447E6307A334D19E56006D13EE00B32E761FD2DB1A3648CE244AD8FC98",
        ["IMAGE_1TO1_BILINEAR"] = "E9DB3C447E6307A334D19E56006D13EE00B32E761FD2DB1A3648CE244AD8FC98",
        ["IMAGE_1TO1_CROPPED"] = "1FFDC9983F7628154D445C78900BD816BA1E1F3EF8262FC423A98CAF7A7CC419",
        ["IMAGE_1TO1_FRACTIONAL"] = "7A805E36777E0FB6B34EE6E0B923E5E5122A00BB4DDD1EB26623C2DD3DB13B19",
        ["IMAGE_1TO1_NEAREST"] = "E9DB3C447E6307A334D19E56006D13EE00B32E761FD2DB1A3648CE244AD8FC98",
        ["IMAGE_ENLARGED_BICUBIC"] = "AF6768A5BC7565AC1A319445AF511F258CC14DAC188D54B5895F234A752C20AF",
        ["IMAGE_ENLARGED_BILINEAR"] = "552555C90A2F79D610F2A1D492B77FF0887FC85AD0FD4C0E268C1C28EDD39513",
        ["IMAGE_ENLARGED_NEAREST"] = "F9AC637DD7FB58DB97B9EE1B8262B8BC8D4BAB3570FE9772F9C0A3F0E80284B9",
        ["IMAGE_ROTATED"] = "7ED76ADDFC69BBA4B638B993FB29BF38E92DD7DEAEAE79BBFF179465AF559FC5",
        ["IMAGE_SHRUNK_FACTOR_2"] = "6F77F3CDF19DC7F942C49EF398079E04593BBB6CBCB7DC37AA16F381A1C72D6C",
        ["IMAGE_SHRUNK_FACTOR_3"] = "6257DE1DE09890883479F6D2D8521E7C7582AE436422176A393CF4E9BBEDB8BA",
        ["IMAGE_SHRUNK_FACTOR_5_PARTIAL_BLOCKS"] = "1C6E9AAEBFBB192858DC8C316EC54D09CAABB5D1C69677997A39F824C46F8805",
        ["IMAGE_SHRUNK_JUST_UNDER_FACTOR_2"] = "521E4228762205E155998131E915CEA26D3F6AC91128DA4EC5C4DCD5312F6C5A",
        ["IMAGE_SHRUNK_ODD_SOURCE"] = "D4A7587566CFAFBAB06270DA64285F6C98E57782ED66697192DE5525477F97ED",
    };
}

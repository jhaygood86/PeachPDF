using PeachDrawing.Core;

namespace PeachDrawing.Tests;

public class ImageSamplingTests
{
    /// <summary>A 2x2 image: black and white pixels in a checkerboard, drawn into a tile of the canvas.</summary>
    private static Image Checkerboard(RasterCanvas canvas)
    {
        var (graphics, image) = canvas.CreateTile(2, 2)!.Value;
        var black = graphics.GetSolidBrush(PaintColor.Black);
        var white = graphics.GetSolidBrush(PaintColor.White);
        graphics.DrawRectangle(black, 0, 0, 1, 1);
        graphics.DrawRectangle(white, 1, 0, 1, 1);
        graphics.DrawRectangle(white, 0, 1, 1, 1);
        graphics.DrawRectangle(black, 1, 1, 1, 1);
        graphics.Dispose();
        return image;
    }

    private static byte[] Draw(ImageSampling sampling, int size = 16)
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(size, size);
        canvas.DrawImage(Checkerboard(canvas), new Rect(0, 0, size, size), sampling);
        return canvas.ToPixelBuffer().PremultipliedRgba.ToArray();
    }

    private static int Distinct(byte[] pixels)
    {
        var values = new HashSet<byte>();
        for (var i = 0; i < pixels.Length; i += 4)
            values.Add(pixels[i]);
        return values.Count;
    }

    [Fact]
    public void Nearest_WhenEnlarging_KeepsOnlyTheSourceColours() => Assert.Equal(2, Distinct(Draw(ImageSampling.Nearest)));

    [Fact]
    public void Pixelated_WhenEnlarging_IsTheSameAsNearest() =>
        Assert.Equal(Draw(ImageSampling.Nearest), Draw(ImageSampling.Pixelated));

    [Fact]
    public void Bilinear_WhenEnlarging_BlendsBetweenPixels() => Assert.True(Distinct(Draw(ImageSampling.Bilinear)) > 2);

    [Fact]
    public void Bicubic_WhenEnlarging_BlendsAndDiffersFromBilinear()
    {
        var bicubic = Draw(ImageSampling.Bicubic);

        Assert.True(Distinct(bicubic) > 2);
        Assert.NotEqual(Draw(ImageSampling.Bilinear), bicubic);
    }

    [Fact]
    public void Bicubic_NeverProducesAColourBrighterThanItsAlpha()
    {
        var pixels = Draw(ImageSampling.Bicubic, 33);

        for (var i = 0; i < pixels.Length; i += 4)
        {
            Assert.True(pixels[i] <= pixels[i + 3]);
            Assert.True(pixels[i + 1] <= pixels[i + 3]);
            Assert.True(pixels[i + 2] <= pixels[i + 3]);
        }
    }

    [Fact]
    public void Automatic_IsWhatTheImageAskedFor()
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(16, 16);
        using var reference = context.CreateCanvas(16, 16);
        var image = Checkerboard(canvas);

        canvas.DrawImage(image, new Rect(0, 0, 16, 16), ImageSampling.Automatic);
        reference.DrawImage(image, new Rect(0, 0, 16, 16));

        Assert.Equal(reference.ToPixelBuffer().PremultipliedRgba.ToArray(), canvas.ToPixelBuffer().PremultipliedRgba.ToArray());
    }

    [Fact]
    public void Shrinking_WithPixelated_FiltersWhereNearestOnlySkips()
    {
        var context = new RasterRenderContext();
        using var source = context.CreateCanvas(4, 4);

        // A 64x64 image, black on the left half and white on the right, drawn at 3x3: nearest can only give black or white,
        // while a shrink that filters produces a grey in the middle column.
        var (graphics, image) = source.CreateTile(64, 64)!.Value;
        graphics.DrawRectangle(graphics.GetSolidBrush(PaintColor.Black), 0, 0, 32, 64);
        graphics.DrawRectangle(graphics.GetSolidBrush(PaintColor.White), 32, 0, 32, 64);
        graphics.Dispose();

        byte MiddleOf(ImageSampling sampling)
        {
            using var canvas = context.CreateCanvas(3, 3);
            canvas.DrawImage(image, new Rect(0, 0, 3, 3), sampling);
            return canvas.ToPixelBuffer().PremultipliedRgba.Span[(1 * 3 + 1) * 4];
        }

        Assert.Contains(MiddleOf(ImageSampling.Nearest), new byte[] { 0, 255 });
        Assert.InRange(MiddleOf(ImageSampling.Pixelated), 1, 254);
    }

    [Fact]
    public void SrcRectOverload_HonoursSampling()
    {
        var context = new RasterRenderContext();
        using var nearest = context.CreateCanvas(8, 8);
        using var smooth = context.CreateCanvas(8, 8);
        var image = Checkerboard(nearest);

        nearest.DrawImage(image, new Rect(0, 0, 8, 8), new Rect(0, 0, 2, 2), ImageSampling.Nearest);
        smooth.DrawImage(image, new Rect(0, 0, 8, 8), new Rect(0, 0, 2, 2), ImageSampling.Bilinear);

        Assert.NotEqual(nearest.ToPixelBuffer().PremultipliedRgba.ToArray(), smooth.ToPixelBuffer().PremultipliedRgba.ToArray());
    }
}

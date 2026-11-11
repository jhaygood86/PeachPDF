using PeachDrawing.Core;

namespace PeachDrawing.Tests;

/// <summary>
/// A strongly shrunk image is pre-averaged in blocks, and the vector form divides in single precision, which is only exact while a block's sum
/// stays below 2^24 and the quotient's distance to the next whole number stays above the rounding error. A picture of one color shrunk by a large
/// factor has to come out as that color exactly, whatever the factor.
/// </summary>
public class ImageDownsampleLargeFactorTests
{
    [Theory]
    [InlineData(2, 200)]
    [InlineData(64, 200)]
    [InlineData(150, 200)]
    [InlineData(255, 200)]
    [InlineData(300, 200)]
    [InlineData(400, 255)]
    [InlineData(400, 199)]
    public void AConstantColorShrunkByAnyFactor_KeepsItsColor(int factor, int channel)
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(8, 8);
        var size = factor * 2;
        var (graphics, image) = canvas.CreateTile(size, size)!.Value;
        graphics.DrawRectangle(graphics.GetSolidBrush(PaintColor.FromArgb(255, channel, channel, channel)), 0, 0, size, size);
        graphics.Dispose();

        // Two pixels across for 2 * factor source pixels: each destination pixel averages a factor x factor block of the one color.
        canvas.DrawImage(image, new Rect(1, 1, 2, 2), ImageSampling.Bilinear);

        var pixels = canvas.ToPixelBuffer().PremultipliedRgba.Span;
        foreach (var (x, y) in new[] { (1, 1), (2, 1), (1, 2), (2, 2) })
        {
            var i = (y * 8 + x) * 4;
            Assert.True(pixels[i] == channel && pixels[i + 1] == channel && pixels[i + 2] == channel && pixels[i + 3] == 255,
                $"factor {factor}: ({x}, {y}) is {pixels[i]}, {pixels[i + 1]}, {pixels[i + 2]}, {pixels[i + 3]}, expected {channel}");
        }
    }

    /// <summary>
    /// A 300 x 300 block (90000 pixels) whose average is 199.99998: 45001 pixels of 199 and the rest 200, so (sum + n / 2) / n is just under 200 and the
    /// integer division gives 199. A single-precision sum of that size (about 18 million) is past 2^24 and rounds, which can carry the quotient over to 200.
    /// </summary>
    [Fact]
    public void ABlockAverageJustUnderAWholeNumber_RoundsDownAtAHugeFactor()
    {
        const int factor = 300;
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(8, 8);
        var (graphics, image) = canvas.CreateTile(factor * 2, factor * 2)!.Value;
        graphics.DrawRectangle(graphics.GetSolidBrush(PaintColor.FromArgb(255, 200, 200, 200)), 0, 0, factor * 2, factor * 2);
        var low = graphics.GetSolidBrush(PaintColor.FromArgb(255, 199, 199, 199));
        graphics.DrawRectangle(low, 0, 0, factor, factor / 2); // 300 x 150 = 45000 pixels
        graphics.DrawRectangle(low, 0, factor / 2, 1, 1);      // one more
        graphics.Dispose();

        canvas.DrawImage(image, new Rect(1, 1, 2, 2), ImageSampling.Bilinear);

        const long n = (long)factor * factor;
        var sum = 199L * 45001 + 200L * (n - 45001);
        var expected = (int)((sum + n / 2) / n);
        Assert.Equal(199, expected);

        var i = (1 * 8 + 1) * 4;
        Assert.Equal(expected, canvas.ToPixelBuffer().PremultipliedRgba.Span[i]);
    }
}

namespace PeachBrackets.WptRunner.Tests;

public class ComparisonTests
{
    private static RgbaImage Solid(int w, int h, byte r, byte g, byte b)
    {
        var pixels = new byte[w * h * 4];
        for (var i = 0; i < pixels.Length; i += 4)
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (r, g, b, 255);
        return new RgbaImage(w, h, pixels);
    }

    [Fact]
    public void Compare_ReportsTheLargestChannelDifference_AndTheNumberOfDifferingPixels()
    {
        var a = Solid(2, 2, 10, 20, 30);
        var b = Solid(2, 2, 10, 20, 30);
        b.Pixels[0] = 15; // one pixel, red +5
        b.Pixels[5] = 50; // another, green +30

        Assert.Equal((30, 2), a.Compare(b));
    }

    [Fact]
    public void Compare_IgnoresTheAlphaChannel()
    {
        var a = Solid(1, 1, 1, 2, 3);
        var b = Solid(1, 1, 1, 2, 3);
        b.Pixels[3] = 0;

        Assert.Equal((0, 0), a.Compare(b));
    }

    [Fact]
    public void ImagesOfDifferentSizes_NeverCompare()
    {
        Assert.Null(Solid(2, 2, 0, 0, 0).Compare(Solid(2, 3, 0, 0, 0)));
    }

    [Fact]
    public void IsBlank_IsTrueOnlyForFlatWhiteOrFullyTransparent()
    {
        Assert.True(Solid(2, 2, 255, 255, 255).IsBlank());
        Assert.True(new RgbaImage(1, 1, new byte[4]).IsBlank());
        Assert.False(Solid(2, 2, 254, 255, 255).IsBlank());

        var mostlyWhite = Solid(3, 3, 255, 255, 255);
        mostlyWhite.Pixels[16] = 0; // one non-white pixel
        Assert.False(mostlyWhite.IsBlank());
    }

    [Fact]
    public void ThePngWriter_ProducesAValidSignatureAndEndChunk()
    {
        var png = PngWriter.Encode(Solid(3, 2, 1, 2, 3));

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        Assert.Equal("IEND"u8.ToArray(), png[^8..^4]);
    }
}

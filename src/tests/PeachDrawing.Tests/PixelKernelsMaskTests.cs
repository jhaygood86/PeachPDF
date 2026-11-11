namespace PeachDrawing.Tests;

/// <summary>The vector mask kernels write the scalar reference's bytes for every length and any pixel content, in place or not.</summary>
public class PixelKernelsMaskTests
{
    private static byte[] RandomPixels(Random random, int pixels)
    {
        var bytes = new byte[pixels * 4];
        for (var i = 0; i < pixels; i++)
        {
            var a = random.Next(256);
            // Mostly valid premultiplied color, sometimes color above alpha.
            var limit = random.Next(5) == 0 ? 255 : a;
            bytes[i * 4] = (byte)random.Next(limit + 1);
            bytes[i * 4 + 1] = (byte)random.Next(limit + 1);
            bytes[i * 4 + 2] = (byte)random.Next(limit + 1);
            bytes[i * 4 + 3] = (byte)a;
        }

        return bytes;
    }

    [Fact]
    public void MaskLuminance_MatchesTheScalarReference_ForEveryLength()
    {
        var random = new Random(11);
        for (var pixels = 0; pixels <= 70; pixels++)
        {
            for (var round = 0; round < 6; round++)
            {
                var image = RandomPixels(random, pixels);
                var mask = RandomPixels(random, pixels);

                var expected = new byte[image.Length];
                PixelKernels.MaskLuminanceScalar(image, mask, expected, image.Length);
                var actual = new byte[image.Length];
                PixelKernels.MaskLuminance(image, mask, actual);

                Assert.True(expected.AsSpan().SequenceEqual(actual), $"{pixels} pixels, round {round}");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaskAlpha_MatchesTheScalarReference_ForEveryLength(bool invert)
    {
        var random = new Random(12);
        for (var pixels = 0; pixels <= 70; pixels++)
        {
            for (var round = 0; round < 6; round++)
            {
                var image = RandomPixels(random, pixels);
                var mask = RandomPixels(random, pixels);

                var expected = new byte[image.Length];
                PixelKernels.MaskAlphaScalar(image, mask, expected, image.Length, invert);
                var actual = new byte[image.Length];
                PixelKernels.MaskAlpha(image, mask, actual, invert);

                Assert.True(expected.AsSpan().SequenceEqual(actual), $"{pixels} pixels, round {round}, invert {invert}");
            }
        }
    }

    [Fact]
    public void TheMaskKernels_WorkInPlace()
    {
        var random = new Random(13);
        var image = RandomPixels(random, 53);
        var mask = RandomPixels(random, 53);

        var expectedLuminance = new byte[image.Length];
        PixelKernels.MaskLuminanceScalar(image, mask, expectedLuminance, image.Length);
        var inPlace = (byte[])image.Clone();
        PixelKernels.MaskLuminance(inPlace, mask, inPlace);
        Assert.True(expectedLuminance.AsSpan().SequenceEqual(inPlace));

        var expectedAlpha = new byte[image.Length];
        PixelKernels.MaskAlphaScalar(image, mask, expectedAlpha, image.Length, invert: true);
        inPlace = (byte[])image.Clone();
        PixelKernels.MaskAlpha(inPlace, mask, inPlace, invert: true);
        Assert.True(expectedAlpha.AsSpan().SequenceEqual(inPlace));
    }
}

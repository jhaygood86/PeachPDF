namespace PeachDrawing.Tests;

/// <summary>
/// <see cref="PixelKernels.BlendSolid"/> stores long runs of full coverage of an opaque colour with one fill; the result has to be the scalar
/// reference's, byte for byte, for every length and for every way a row can alternate between runs and partial coverage.
/// </summary>
public class BlendSolidOpaqueRunTests
{
    private static byte[] RandomDestination(Random random, int pixels)
    {
        var dst = new byte[pixels * 4];
        for (var i = 0; i < pixels; i++)
        {
            var a = random.Next(256);
            dst[i * 4] = (byte)random.Next(a + 1);
            dst[i * 4 + 1] = (byte)random.Next(a + 1);
            dst[i * 4 + 2] = (byte)random.Next(a + 1);
            dst[i * 4 + 3] = (byte)a;
        }

        return dst;
    }

    /// <summary>Coverage made of stretches: full, empty, or partial, with lengths on both sides of the run threshold.</summary>
    private static byte[] StretchedCoverage(Random random, int length)
    {
        var coverage = new byte[length];
        var i = 0;
        while (i < length)
        {
            var stretch = Math.Min(length - i, random.Next(1, 70));
            var kind = random.Next(4);
            for (var k = 0; k < stretch; k++)
                coverage[i + k] = kind switch { 0 or 1 => (byte)255, 2 => (byte)0, _ => (byte)random.Next(256) };
            i += stretch;
        }

        return coverage;
    }

    [Theory]
    [InlineData(255u << 24 | 0x336699u)]
    [InlineData(unchecked((uint)(255 << 24)))]
    [InlineData(0xFFFFFFFFu)]
    [InlineData(128u << 24 | 0x402010u)] // translucent: takes the kernels alone
    public void BlendSolid_MatchesTheScalarReference_ForEveryLengthAndRunPattern(uint color)
    {
        var random = new Random(1234);
        for (var length = 0; length <= 150; length++)
        {
            for (var round = 0; round < 8; round++)
            {
                var coverage = StretchedCoverage(random, length);
                var original = RandomDestination(random, length);

                var expected = (byte[])original.Clone();
                PixelKernels.BlendSolidScalar(expected, coverage, color);

                var actual = (byte[])original.Clone();
                PixelKernels.BlendSolid(actual, coverage, color);

                Assert.True(expected.AsSpan().SequenceEqual(actual), $"length {length}, round {round}");
            }
        }
    }

    [Fact]
    public void BlendSolid_WithAFullyCoveredRowOfAnOpaqueColour_StoresTheColourEverywhere()
    {
        var coverage = new byte[1000];
        Array.Fill(coverage, (byte)255);
        var dst = RandomDestination(new Random(5), 1000);

        PixelKernels.BlendSolid(dst, coverage, PixelKernels.Pack(10, 20, 30, 255));

        for (var i = 0; i < 1000; i++)
            Assert.Equal(new byte[] { 10, 20, 30, 255 }, dst.AsSpan(i * 4, 4).ToArray());
    }
}

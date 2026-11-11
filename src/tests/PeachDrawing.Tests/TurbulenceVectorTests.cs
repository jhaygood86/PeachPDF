using PeachDrawing.Core;
using PeachDrawing.Filters;

namespace PeachDrawing.Tests;

/// <summary>
/// The vector turbulence render has to write the reference render's bytes for any seed, frequency, octave count and stitching, on surfaces
/// that start away from the origin and are not a multiple of anything, and when the surface is scaled against user space.
/// </summary>
public class TurbulenceVectorTests
{
    private static byte[] Bytes(RasterSurface surface)
    {
        var bytes = new byte[surface.Width * surface.Height * 4];
        for (var y = 0; y < surface.Height; y++)
            surface.Row(y)[..(surface.Width * 4)].CopyTo(bytes.AsSpan(y * surface.Width * 4));
        return bytes;
    }

    [Theory]
    [InlineData(1L, 0.05, 0.08, 1, true, false)]
    [InlineData(7L, 0.05, 0.08, 4, true, false)]
    [InlineData(7L, 0.05, 0.08, 4, false, false)]
    [InlineData(123456L, 0.3, 0.01, 3, true, true)]
    [InlineData(-5L, 0.0, 0.2, 2, false, true)]
    [InlineData(0L, 1.7, 1.7, 6, true, false)]
    [InlineData(99L, 0.02, 0.0, 5, false, false)]
    public void Render_MatchesTheReference(long seed, double fx, double fy, int octaves, bool fractal, bool stitch)
    {
        var generator = new Turbulence(seed);
        foreach (var (width, height, gridX, gridY, scale) in new[] { (37, 19, 0, 0, 1.0), (64, 5, 13, -7, 2.5), (1, 9, 100, 200, 0.75), (23, 23, -50, 4, 1.0) })
        {
            using var expected = new RasterSurface(width, height, gridX, gridY, scale, scale);
            using var actual = new RasterSurface(width, height, gridX, gridY, scale, scale);

            generator.RenderReference(expected, scale, scale, fx, fy, octaves, fractal, stitch, 3, 4, 40, 25);
            generator.Render(actual, scale, scale, fx, fy, octaves, fractal, stitch, 3, 4, 40, 25);

            Assert.True(Bytes(expected).AsSpan().SequenceEqual(Bytes(actual)), $"{width}x{height} at {gridX},{gridY} scale {scale}");
        }
    }

    [Fact]
    public void Render_WithAnEmptySurfaceOrNoOctaves_MatchesTheReference()
    {
        var generator = new Turbulence(3);
        using var expected = new RasterSurface(16, 16, 0, 0, 1, 1);
        using var actual = new RasterSurface(16, 16, 0, 0, 1, 1);

        generator.RenderReference(expected, 1, 1, 0.1, 0.1, 0, true, false, 0, 0, 16, 16);
        generator.Render(actual, 1, 1, 0.1, 0.1, 0, true, false, 0, 0, 16, 16);

        Assert.True(Bytes(expected).AsSpan().SequenceEqual(Bytes(actual)));
    }
}

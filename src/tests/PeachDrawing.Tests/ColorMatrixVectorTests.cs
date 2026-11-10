using System.Numerics;
using PeachDrawing.Core;

namespace PeachDrawing.Tests;

/// <summary>
/// The vector color matrix kernel has to write the scalar reference's bytes, whatever the pixels and the matrix: random premultiplied pixels
/// (and pixels whose color exceeds their alpha, which the straight-color step clamps), random matrices, every length around the four-pixel step.
/// </summary>
public class ColorMatrixVectorTests
{
    private static byte[] RandomPixels(Random random, int pixels)
    {
        var bytes = new byte[pixels * 4];
        for (var i = 0; i < pixels; i++)
        {
            var kind = random.Next(6);
            var a = kind switch { 0 => 0, 1 => 255, _ => random.Next(256) };
            // Mostly valid premultiplied color; sometimes color above alpha, and sometimes color on a transparent pixel.
            var limit = kind == 5 ? 255 : a;
            bytes[i * 4] = (byte)random.Next(limit + 1);
            bytes[i * 4 + 1] = (byte)random.Next(limit + 1);
            bytes[i * 4 + 2] = (byte)random.Next(limit + 1);
            bytes[i * 4 + 3] = (byte)a;
        }

        return bytes;
    }

    private static ColorMatrix RandomMatrix(Random random)
    {
        float F(float range) => (float)(random.NextDouble() * 2 - 1) * range;
        return new ColorMatrix(
            new Matrix4x4(F(1.5f), F(1.5f), F(1.5f), F(0.5f), F(1.5f), F(1.5f), F(1.5f), F(0.5f), F(1.5f), F(1.5f), F(1.5f), F(0.5f), F(0.5f), F(0.5f), F(0.5f), F(1.2f)),
            new Vector4(F(0.5f), F(0.5f), F(0.5f), F(0.5f)));
    }

    [Fact]
    public void Apply_MatchesTheScalarReference_ForRandomPixelsAndMatrices()
    {
        var random = new Random(77);
        for (var pixels = 0; pixels <= 40; pixels++)
        {
            for (var round = 0; round < 25; round++)
            {
                var source = RandomPixels(random, pixels);
                var matrix = round == 0 ? ColorMatrix.Identity : RandomMatrix(random);

                var expected = new byte[source.Length];
                ColorMatrixFilter.ApplyScalar(source, expected, matrix);

                var actual = new byte[source.Length];
                ColorMatrixFilter.Apply(source, actual, matrix);

                Assert.True(expected.AsSpan().SequenceEqual(actual), $"{pixels} pixels, round {round}");
            }
        }
    }

    [Fact]
    public void Apply_InPlace_MatchesTheScalarReference()
    {
        var random = new Random(78);
        var source = RandomPixels(random, 203);
        var matrix = RandomMatrix(random);

        var expected = new byte[source.Length];
        ColorMatrixFilter.ApplyScalar(source, expected, matrix);

        var actual = (byte[])source.Clone();
        ColorMatrixFilter.Apply(actual, actual, matrix);

        Assert.True(expected.AsSpan().SequenceEqual(actual));
    }

    [Fact]
    public void ApplyInPlace_WithSeveralMatrices_MatchesTheScalarReference()
    {
        var random = new Random(79);
        for (var pixels = 0; pixels <= 33; pixels += 3)
        {
            for (var stageCount = 1; stageCount <= 4; stageCount++)
            {
                var source = RandomPixels(random, pixels);
                var stages = Enumerable.Range(0, stageCount).Select(_ => RandomMatrix(random)).ToArray();

                var expected = (byte[])source.Clone();
                ColorMatrixFilter.ApplyStagesScalar(expected, stages);

                using var surface = new RasterSurface(Math.Max(pixels, 1), 1, 0, 0, 1, 1);
                source.CopyTo(surface.Pixels);
                ColorMatrixFilter.ApplyInPlace(surface, stages);

                Assert.True(expected.AsSpan().SequenceEqual(surface.Pixels[..source.Length]), $"{pixels} pixels, {stageCount} stages");
            }
        }
    }

    [Fact]
    public void Apply_WithANonFiniteMatrix_StillMatchesTheScalarReference()
    {
        var source = RandomPixels(new Random(80), 37);
        var matrix = new ColorMatrix(new Matrix4x4(float.NaN, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, float.PositiveInfinity), Vector4.Zero);

        var expected = new byte[source.Length];
        ColorMatrixFilter.ApplyScalar(source, expected, matrix);
        var actual = new byte[source.Length];
        ColorMatrixFilter.Apply(source, actual, matrix);

        Assert.True(expected.AsSpan().SequenceEqual(actual));
    }
}

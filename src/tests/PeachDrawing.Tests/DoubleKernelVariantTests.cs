using System.Runtime.Intrinsics;
using PeachDrawing.Core;
using PeachDrawing.Filters;

namespace PeachDrawing.Tests;

/// <summary>
/// The double-precision kernels (<c>feComposite</c> arithmetic, the radial gradient, <c>feTurbulence</c>) each have a 128-bit form (SSE2 on x64, NEON on ARM64) and a
/// 256-bit form (AVX, where the hardware accelerates it). Each form is called directly here, whatever the hardware's preferred one is, and has to write the scalar
/// reference's bytes. The 256-bit forms live in <c>*.Vector256.cs</c> files; their tests skip where 256-bit vectors are not accelerated (ARM64), and CI leaves those
/// files out of the macOS diff-coverage gate.
/// </summary>
public class DoubleKernelVariantTests
{
    private static byte[] RandomPixels(Random random, int pixels)
    {
        var bytes = new byte[pixels * 4];
        for (var i = 0; i < pixels; i++)
        {
            var a = random.Next(256);
            var limit = random.Next(5) == 0 ? 255 : a;
            bytes[i * 4] = (byte)random.Next(limit + 1);
            bytes[i * 4 + 1] = (byte)random.Next(limit + 1);
            bytes[i * 4 + 2] = (byte)random.Next(limit + 1);
            bytes[i * 4 + 3] = (byte)a;
        }

        return bytes;
    }

    public static TheoryData<string> Forms => new() { "128", "256" };

    private static bool Supported(string form) => form == "128" ? Vector128.IsHardwareAccelerated : Vector256.IsHardwareAccelerated;

    // ---- feComposite arithmetic ----

    [Theory]
    [MemberData(nameof(Forms))]
    public void Arithmetic_MatchesTheScalarReference(string form)
    {
        Assert.SkipUnless(Supported(form), $"{form}-bit vectors are not hardware accelerated here");

        var random = new Random(41);
        var coefficientSets = new[] { (0.0, 0.5, 0.5, 0.0), (1.0, 0.0, 0.0, 0.0), (0.3, -0.4, 1.2, 0.05), (-1.0, 1.5, 1.5, -0.5) };
        for (var pixels = 0; pixels <= 33; pixels++)
        {
            foreach (var (k1, k2, k3, k4) in coefficientSets)
            {
                var a = RandomPixels(random, pixels);
                var b = RandomPixels(random, pixels);

                var expected = new byte[a.Length];
                FilterOps.ArithmeticScalar(a, b, expected, k1, k2, k3, k4);

                var actual = new byte[a.Length];
                var done = form == "128"
                    ? FilterOps.ArithmeticVector128(a, b, actual, k1, k2, k3, k4)
                    : FilterOps.ArithmeticVector256(a, b, actual, k1, k2, k3, k4);
                FilterOps.ArithmeticScalar(a, b, actual, k1, k2, k3, k4, done);

                Assert.True(expected.AsSpan().SequenceEqual(actual), $"{form}-bit, {pixels} pixels, k = {k1}, {k2}, {k3}, {k4}");
            }
        }
    }

    [Fact]
    public void Arithmetic_WithHugeCoefficients_IsLeftToTheScalarCode()
    {
        Assert.False(FilterOps.IsVectorSafe(1e9, 0, 0, 0));
        Assert.True(FilterOps.IsVectorSafe(1.5, -2, 0.25, 1e5));
    }

    // ---- radial gradient ----

    private static PaintSource.RadialPaint Radial(RasterRenderContext context, bool repeating, bool focal)
    {
        var stops = new[] { (PaintColor.FromArgb(255, 255, 0, 0), 0d), (PaintColor.FromArgb(128, 0, 200, 40), 0.55), (PaintColor.FromArgb(220, 0, 0, 255), 1d) };
        var brush = context.GetRadialGradientBrush(new PaintPoint(31.5, 18.25), 27.5, 14.0, stops, repeating, focal ? new PaintPoint(26.0, 15.0) : null);
        return (PaintSource.RadialPaint)PaintSource.From(brush, new Affine(1.0, 0.0, 0.0, 1.0, -2.5, 1.25))!;
    }

    [Theory]
    [InlineData("128", false, false)]
    [InlineData("128", true, false)]
    [InlineData("128", false, true)]
    [InlineData("256", false, false)]
    [InlineData("256", true, false)]
    [InlineData("256", false, true)]
    public void RadialFillSpan_MatchesTheScalarReference(string form, bool repeating, bool focal)
    {
        Assert.SkipUnless(Supported(form), $"{form}-bit vectors are not hardware accelerated here");

        var context = new RasterRenderContext();
        var paint = Radial(context, repeating, focal);
        foreach (var (x0, y, count) in new[] { (0, 0, 64), (5, 7, 33), (-3, 20, 4), (10, 3, 7), (0, 11, 3) })
        {
            var expected = new byte[count * 4];
            paint.FillSpanScalar(x0, y, count, expected);

            var actual = new byte[count * 4];
            var done = count >= 4 ? (form == "128" ? paint.FillSpanVector128(x0, y, count, actual) : paint.FillSpanVector256(x0, y, count, actual)) : 0;
            paint.FillSpanScalar(x0, y, count, actual, done);

            Assert.True(expected.AsSpan().SequenceEqual(actual), $"{form}-bit, repeating {repeating}, focal {focal}, span {x0},{y} x {count}");
        }
    }

    // ---- feTurbulence ----

    [Theory]
    [MemberData(nameof(Forms))]
    public void Turbulence_MatchesTheReference(string form)
    {
        Assert.SkipUnless(Supported(form), $"{form}-bit vectors are not hardware accelerated here");

        var generator = new Turbulence(7);
        foreach (var (fx, fy, octaves, fractal, stitch) in new[] { (0.05, 0.08, 3, true, false), (0.05, 0.08, 4, false, false), (0.3, 0.01, 3, true, true), (0.0, 0.2, 2, false, true) })
        {
            foreach (var (width, height, gridX, gridY) in new[] { (37, 19, 0, 0), (23, 11, -50, 4), (1, 9, 100, 200) })
            {
                using var expected = new RasterSurface(width, height, gridX, gridY, 1, 1);
                using var actual = new RasterSurface(width, height, gridX, gridY, 1, 1);

                generator.RenderReference(expected, 1, 1, fx, fy, octaves, fractal, stitch, 3, 4, 40, 25);
                if (form == "128")
                    generator.RenderVector128(actual, 1, 1, fx, fy, octaves, fractal, stitch, 3, 4, 40, 25);
                else
                    generator.RenderVector256(actual, 1, 1, fx, fy, octaves, fractal, stitch, 3, 4, 40, 25);

                for (var y = 0; y < height; y++)
                    Assert.True(expected.Row(y)[..(width * 4)].SequenceEqual(actual.Row(y)[..(width * 4)]), $"{form}-bit, {width}x{height}, row {y}");
            }
        }
    }
}

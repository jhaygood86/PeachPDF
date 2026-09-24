using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace PeachPDF.Raster.Filters;

/// <summary>
/// A Gaussian blur of a premultiplied RGBA8 surface. For a standard deviation of 2 or more it uses the three
/// successive box blurs Filter Effects 1 §feGaussianBlur specifies (which approximate the Gaussian to within a
/// few percent and cost the same regardless of radius); below 2 it convolves with the exact kernel. Everything is
/// integer arithmetic, so the result is identical on every CPU. Pixels outside the surface count as transparent.
/// </summary>
internal static partial class GaussianBlur
{
    /// <summary>The smallest deviation, in pixels, for which the box approximation is used.</summary>
    internal const double BoxThreshold = 2.0;

    /// <summary>Blurs <paramref name="surface"/> in place with the given horizontal and vertical standard deviations, in pixels.</summary>
    public static void Apply(RasterSurface surface, double sigmaX, double sigmaY)
    {
        var width = surface.Width;
        var height = surface.Height;
        var scratch = ArrayPool<byte>.Shared.Rent(width * height * 4);
        try
        {
            var pixels = surface.Pixels;
            var other = scratch.AsSpan(0, width * height * 4);

            if (sigmaX > 0)
                Pass(pixels, other, width, height, sigmaX, horizontal: true);

            if (sigmaY > 0)
                Pass(pixels, other, width, height, sigmaY, horizontal: false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    /// <summary>One direction of the blur, leaving the result in <paramref name="pixels"/>.</summary>
    private static void Pass(Span<byte> pixels, Span<byte> scratch, int width, int height, double sigma, bool horizontal)
    {
        if (sigma < BoxThreshold)
        {
            Kernel(pixels, scratch, width, height, sigma, horizontal);
            return;
        }

        // A window wider than the surface only dilutes what is inside it with the nothing beyond the edges, so it is capped at the
        // surface's extent along the blurred axis; that also keeps the conversion in range for any sigma.
        var extent = Math.Max(1, horizontal ? width : height);
        var d = (int)Math.Min(Math.Floor(sigma * 3 * Math.Sqrt(2 * Math.PI) / 4 + 0.5), extent);
        if (d < 1) d = 1;

        // Three box blurs. Each is (size, how many pixels of the window lie before the output pixel).
        // Odd d: all three centred. Even d: two of size d offset half a pixel either way, then one of size d + 1 centred.
        (int Size, int Before)[] passes = d % 2 == 1
            ? [(d, (d - 1) / 2), (d, (d - 1) / 2), (d, (d - 1) / 2)]
            : [(d, d / 2), (d, d / 2 - 1), (d + 1, d / 2)];

        // Ping-pong between the two buffers; the final result must end up in `pixels`.
        var source = pixels;
        var target = scratch;
        foreach (var (size, before) in passes)
        {
            Box(source, target, width, height, size, before, horizontal);
            var swap = source;
            source = target;
            target = swap;
        }

        // After three swaps the newest data sits in `scratch`; copy it back.
        source.CopyTo(pixels);
    }

    /// <summary>A box blur whose window covers [i - before, i - before + size - 1] around output index i.</summary>
    private static void Box(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height, int size, int before, bool horizontal)
    {
        if (Vector128.IsHardwareAccelerated && size < 4096)
        {
            BoxVector(src, dst, width, height, size, before, horizontal);
            return;
        }

        BoxScalar(src, dst, width, height, size, before, horizontal);
    }

    /// <summary>The scalar reference box blur; the vector form must produce the same bytes.</summary>
    internal static void BoxScalar(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height, int size, int before, bool horizontal)
    {
        var reciprocal = (ulong)(((1UL << 32) + (ulong)size - 1) / (ulong)size);
        var half = size / 2;

        if (horizontal)
        {
            var sums = new int[4];
            for (var y = 0; y < height; y++)
            {
                var row = y * width * 4;
                sums[0] = sums[1] = sums[2] = sums[3] = 0;

                // Prime the window for output index 0: pixels [-before, size - 1 - before].
                var last = size - 1 - before;
                for (var k = 0; k <= last && k < width; k++)
                    for (var c = 0; c < 4; c++)
                        sums[c] += src[row + k * 4 + c];

                for (var x = 0; x < width; x++)
                {
                    for (var c = 0; c < 4; c++)
                        dst[row + x * 4 + c] = Divide(sums[c] + half, size, reciprocal);

                    var add = x + 1 + (size - 1 - before);
                    var remove = x - before;
                    if (add < width)
                        for (var c = 0; c < 4; c++)
                            sums[c] += src[row + add * 4 + c];

                    if (remove >= 0)
                        for (var c = 0; c < 4; c++)
                            sums[c] -= src[row + remove * 4 + c];
                }
            }

            return;
        }

        // Vertical: keep one running sum per column and channel and slide down the rows, so memory is read
        // row by row rather than with a large stride.
        var stride = width * 4;
        var column = ArrayPool<int>.Shared.Rent(stride);
        try
        {
            Array.Clear(column, 0, stride);
            var lastRow = size - 1 - before;
            for (var k = 0; k <= lastRow && k < height; k++)
                for (var i = 0; i < stride; i++)
                    column[i] += src[k * stride + i];

            for (var y = 0; y < height; y++)
            {
                var outRow = y * stride;
                for (var i = 0; i < stride; i++)
                    dst[outRow + i] = Divide(column[i] + half, size, reciprocal);

                var add = y + 1 + (size - 1 - before);
                var remove = y - before;
                if (add < height)
                    for (var i = 0; i < stride; i++)
                        column[i] += src[add * stride + i];

                if (remove >= 0)
                    for (var i = 0; i < stride; i++)
                        column[i] -= src[remove * stride + i];
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(column);
        }
    }

    /// <summary><c>floor(value / size)</c>, exact for the sums a box window of up to 4095 pixels of bytes can reach.</summary>
    private static byte Divide(int value, int size, ulong reciprocal)
    {
        var q = size < 4096 ? (int)(((ulong)value * reciprocal) >> 32) : value / size;
        return (byte)Math.Min(q, 255);
    }

    /// <summary>Convolves with the exact Gaussian kernel (weights in 16-bit fixed point that sum to 65536).</summary>
    private static void Kernel(Span<byte> pixels, Span<byte> scratch, int width, int height, double sigma, bool horizontal)
    {
        var (radius, weights) = KernelWeights(sigma);
        if (Vector128.IsHardwareAccelerated)
            KernelVector(pixels, scratch, width, height, radius, weights, horizontal);
        else
            KernelScalar(pixels, scratch, width, height, radius, weights, horizontal);
    }

    /// <summary>The kernel radius and its 2 * radius + 1 weights, in 16-bit fixed point summing to exactly 65536.</summary>
    internal static (int Radius, int[] Weights) KernelWeights(double sigma)
    {
        var radius = Math.Max(1, (int)Math.Ceiling(sigma * 3));
        var weights = new int[radius * 2 + 1];
        double total = 0;
        for (var i = -radius; i <= radius; i++)
            total += Math.Exp(-(i * i) / (2 * sigma * sigma));

        var assigned = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            var k = i - radius;
            weights[i] = (int)Math.Round(Math.Exp(-(k * k) / (2 * sigma * sigma)) / total * 65536);
            assigned += weights[i];
        }

        weights[radius] += 65536 - assigned;
        return (radius, weights);
    }

    /// <summary>The scalar reference for <see cref="Kernel"/>; the vector form must produce the same bytes.</summary>
    internal static void KernelScalar(Span<byte> pixels, Span<byte> scratch, int width, int height, int radius, int[] weights, bool horizontal)
    {
        var stride = width * 4;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                for (var c = 0; c < 4; c++)
                {
                    long sum = 0;
                    for (var i = -radius; i <= radius; i++)
                    {
                        int sx = x, sy = y;
                        if (horizontal) sx += i; else sy += i;
                        if (sx < 0 || sx >= width || sy < 0 || sy >= height)
                            continue;

                        sum += pixels[sy * stride + sx * 4 + c] * weights[i + radius];
                    }

                    scratch[y * stride + x * 4 + c] = (byte)Math.Min(255, (sum + 32768) >> 16);
                }
            }
        }

        scratch.CopyTo(pixels);
    }
}

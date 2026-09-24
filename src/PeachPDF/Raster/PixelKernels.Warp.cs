using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace PeachPDF.Raster;

/// <summary>
/// The per-pixel kernels of <see cref="Warp"/>: the bilinear texel blend and the depth test of a 3D rendering context. Same rules as the rest of
/// <see cref="PixelKernels"/>: a scalar reference and a <see cref="Vector128"/> implementation that produce identical results (the texel blend
/// is exact integer arithmetic; the depth test is separate IEEE multiplies and adds, never fused).
/// </summary>
internal static partial class PixelKernels
{
    // ---- Bilinear texel blend: the four channels of a pixel are one Vector128<int> ---------------------------------------

    /// <summary>
    /// The bilinear blend of the four texels whose top-left is (<paramref name="x0"/>, <paramref name="y0"/>) in a premultiplied RGBA8
    /// image, with 8.8 fixed-point weights <paramref name="tx"/> and <paramref name="ty"/> (0 to 256) towards the right and lower texel.
    /// A texel outside the image is transparent. Returns the R, G, B and A lanes, each 0 to 255.
    /// </summary>
    public static Vector128<int> BilinearTexels(ReadOnlySpan<byte> pixels, int width, int height, int x0, int y0, int tx, int ty)
    {
        if (Vector128.IsHardwareAccelerated)
            return BilinearTexelsVector128(pixels, width, height, x0, y0, tx, ty);

        var (r, g, b, a) = BilinearTexelsScalar(pixels, width, height, x0, y0, tx, ty);
        return Vector128.Create(r, g, b, a);
    }

    /// <summary>The scalar reference for <see cref="BilinearTexels"/>.</summary>
    internal static (int R, int G, int B, int A) BilinearTexelsScalar(ReadOnlySpan<byte> pixels, int width, int height, int x0, int y0, int tx, int ty)
    {
        Span<int> result = stackalloc int[4];
        for (var c = 0; c < 4; c++)
        {
            var v00 = TexelScalar(pixels, width, height, x0, y0, c);
            var v10 = TexelScalar(pixels, width, height, x0 + 1, y0, c);
            var v01 = TexelScalar(pixels, width, height, x0, y0 + 1, c);
            var v11 = TexelScalar(pixels, width, height, x0 + 1, y0 + 1, c);
            var top = v00 * (256 - tx) + v10 * tx;
            var bottom = v01 * (256 - tx) + v11 * tx;
            result[c] = (top * (256 - ty) + bottom * ty + 32768) >> 16;
        }

        return (result[0], result[1], result[2], result[3]);
    }

    private static int TexelScalar(ReadOnlySpan<byte> pixels, int width, int height, int x, int y, int channel) =>
        (uint)x < (uint)width && (uint)y < (uint)height ? pixels[(y * width + x) * 4 + channel] : 0;

    /// <summary>The <see cref="Vector128"/> implementation of <see cref="BilinearTexels"/>.</summary>
    internal static Vector128<int> BilinearTexelsVector128(ReadOnlySpan<byte> pixels, int width, int height, int x0, int y0, int tx, int ty)
    {
        Vector128<int> t00, t10, t01, t11;

        // Interior: all four texels are inside the image, so no per-texel bounds test.
        if (width > 1 && height > 1 && (uint)x0 < (uint)(width - 1) && (uint)y0 < (uint)(height - 1))
        {
            ref var origin = ref MemoryMarshal.GetReference(pixels);
            var offset = (y0 * width + x0) * 4;
            var stride = width * 4;
            t00 = LoadTexel(ref origin, offset);
            t10 = LoadTexel(ref origin, offset + 4);
            t01 = LoadTexel(ref origin, offset + stride);
            t11 = LoadTexel(ref origin, offset + stride + 4);
        }
        else
        {
            t00 = LoadTexelChecked(pixels, width, height, x0, y0);
            t10 = LoadTexelChecked(pixels, width, height, x0 + 1, y0);
            t01 = LoadTexelChecked(pixels, width, height, x0, y0 + 1);
            t11 = LoadTexelChecked(pixels, width, height, x0 + 1, y0 + 1);
        }

        var top = t00 * (256 - tx) + t10 * tx;
        var bottom = t01 * (256 - tx) + t11 * tx;
        return Vector128.ShiftRightArithmetic(top * (256 - ty) + bottom * ty + Vector128.Create(32768), 16);
    }

    /// <summary>The four channels of the texel at byte <paramref name="offset"/>, widened to one lane each.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> LoadTexel(ref byte origin, int offset) =>
        Vector128.WidenLower(Vector128.WidenLower(Vector128.CreateScalar(Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref origin, offset))).AsByte())).AsInt32();

    private static Vector128<int> LoadTexelChecked(ReadOnlySpan<byte> pixels, int width, int height, int x, int y) =>
        (uint)x < (uint)width && (uint)y < (uint)height ? LoadTexel(ref MemoryMarshal.GetReference(pixels), (y * width + x) * 4) : Vector128<int>.Zero;

    /// <summary>
    /// Writes the average of <paramref name="divisor"/> accumulated samples (each the output of <see cref="BilinearTexels"/>) to the four
    /// bytes at <paramref name="destination"/>, rounding to nearest.
    /// </summary>
    public static void StoreAveraged(Vector128<int> sum, int divisor, Span<byte> destination)
    {
        if (divisor == 1)
        {
            destination[0] = (byte)sum.GetElement(0);
            destination[1] = (byte)sum.GetElement(1);
            destination[2] = (byte)sum.GetElement(2);
            destination[3] = (byte)sum.GetElement(3);
            return;
        }

        var half = divisor / 2;
        destination[0] = (byte)Math.Clamp((sum.GetElement(0) + half) / divisor, 0, 255);
        destination[1] = (byte)Math.Clamp((sum.GetElement(1) + half) / divisor, 0, 255);
        destination[2] = (byte)Math.Clamp((sum.GetElement(2) + half) / divisor, 0, 255);
        destination[3] = (byte)Math.Clamp((sum.GetElement(3) + half) / divisor, 0, 255);
    }

    // ---- Depth test: one row of a depth-resolved composition -------------------------------------------------------------

    /// <summary>
    /// The depth test of one row of a plane being composited into a 3D rendering context. The plane's depth at pixel <c>i</c> of the row is
    /// <c>z0 + zs * i</c> (a plane's depth is affine in screen coordinates, see <c>Warp.Composite</c>); larger is nearer the viewer. A pixel
    /// stays covered only when it is covered on entry (non-zero <paramref name="coverage"/>), has some alpha in <paramref name="samples"/>
    /// (RGBA, premultiplied), and is not behind what <paramref name="depth"/> already holds (allowing <paramref name="epsilon"/> of slack,
    /// so a later coplanar plane wins). A surviving pixel's coverage becomes 255, and its depth is recorded when it is fully opaque: a
    /// translucent pixel must not hide what is behind it from a plane painted after it.
    /// </summary>
    public static void DepthTestRow(Span<float> depth, Span<byte> coverage, ReadOnlySpan<byte> samples, float z0, float zs, float epsilon)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(depth.Length, coverage.Length);
        ArgumentOutOfRangeException.ThrowIfLessThan(samples.Length, coverage.Length * 4);

        var done = 0;
        if (Vector128.IsHardwareAccelerated && coverage.Length >= 4)
            done = DepthTestRowVector128(depth, coverage, samples, z0, zs, epsilon);

        DepthTestRowScalar(depth, coverage, samples, z0, zs, epsilon, done);
    }

    /// <summary>The scalar reference for <see cref="DepthTestRow"/>, starting at pixel <paramref name="start"/>.</summary>
    internal static void DepthTestRowScalar(Span<float> depth, Span<byte> coverage, ReadOnlySpan<byte> samples, float z0, float zs, float epsilon, int start = 0)
    {
        for (var i = start; i < coverage.Length; i++)
        {
            if (coverage[i] == 0)
                continue;

            var alpha = samples[i * 4 + 3];
            if (alpha == 0)
            {
                coverage[i] = 0;
                continue;
            }

            // Multiply, then add: two rounded operations, never a fused one, so the vector path matches bit for bit.
            var scaled = zs * (float)i;
            var d = z0 + scaled;
            var limit = depth[i] - epsilon;
            if (!(d >= limit))
            {
                coverage[i] = 0;
                continue;
            }

            coverage[i] = 255;
            if (alpha == 255)
                depth[i] = d;
        }
    }

    /// <summary>The <see cref="Vector128"/> implementation of <see cref="DepthTestRow"/>; returns the number of pixels done (a multiple of 4).</summary>
    internal static int DepthTestRowVector128(Span<float> depth, Span<byte> coverage, ReadOnlySpan<byte> samples, float z0, float zs, float epsilon)
    {
        var count = coverage.Length / 4 * 4;
        ref var depthRef = ref MemoryMarshal.GetReference(depth);
        ref var coverageRef = ref MemoryMarshal.GetReference(coverage);
        ref var sampleRef = ref MemoryMarshal.GetReference(samples);

        var lanes = Vector128.Create(0f, 1f, 2f, 3f);
        var base0 = Vector128.Create(z0);
        var slope = Vector128.Create(zs);
        var slack = Vector128.Create(epsilon);
        var opaque = Vector128.Create(255u);

        for (var i = 0; i < count; i += 4)
        {
            var entering = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref coverageRef, i));
            if (entering == 0)
                continue;

            var index = Vector128.Create((float)i) + lanes;
            var d = base0 + slope * index;
            var held = Vector128.LoadUnsafe(ref depthRef, (nuint)i);

            var alpha = Vector128.ShiftRightLogical(Vector128.LoadUnsafe(ref sampleRef, (nuint)(i * 4)).AsUInt32(), 24);
            var covered = Vector128.WidenLower(Vector128.WidenLower(Vector128.CreateScalar(entering).AsByte())).AsUInt32();

            // A NaN depth compares false and so is culled, exactly as in the scalar reference.
            var visible = Vector128.GreaterThanOrEqual(d, held - slack).AsUInt32();
            var passes = Vector128.AndNot(Vector128.AndNot(visible, Vector128.Equals(covered, Vector128<uint>.Zero)), Vector128.Equals(alpha, Vector128<uint>.Zero));

            var write = passes & Vector128.Equals(alpha, opaque);
            Vector128.ConditionalSelect(write.AsSingle(), d, held).StoreUnsafe(ref depthRef, (nuint)i);

            // 0xFFFFFFFF lanes narrow, by truncation, to 0xFFFF and then 0xFF: the four coverage bytes.
            var narrow16 = Vector128.Narrow(passes, passes);
            var narrowed = Vector128.Narrow(narrow16, narrow16);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref coverageRef, i), narrowed.AsUInt32().ToScalar());
        }

        return count;
    }
}

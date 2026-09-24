using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace PeachPDF.Raster;

/// <summary>
/// The hot per-pixel loops of the rasterizer. Every kernel has a scalar reference implementation and a
/// vectorized one, and the two are required to produce <b>bit-identical</b> output: all arithmetic is
/// exact 8-bit fixed point (no floating point, no fused operations), so a PDF is the same on every CPU.
/// The portable vector paths use only the <see cref="Vector128"/> API; where measured to win by more than 1.3x on a real
/// buffer, an <c>Avx2</c> path is added beside it (see PixelKernels.Vector256.cs), producing the same bytes.
///
/// <b>Shuffle constants:</b> an index vector handed to <c>Vector128.Shuffle</c> must be the literal <c>Vector128.Create(...)</c> at the call
/// site. The same vector held in a local made the JIT emit a per-element software shuffle, and the kernels ran at scalar speed
/// (5.7 ms per million pixels instead of 0.8 ms).
/// </summary>
internal static partial class PixelKernels
{
    /// <summary>Rounded <c>x / 255</c> for <c>0 &lt;= x &lt;= 65025 (255 * 255)</c>, exact for every such x.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Div255(int x) => (x + 128 + ((x + 128) >> 8)) >> 8;

    /// <summary>Packs a premultiplied colour into the little-endian byte order R, G, B, A.</summary>
    public static uint Pack(byte r, byte g, byte b, byte a) => (uint)(r | (g << 8) | (b << 16) | (a << 24));

    // ---- BlendSolid: source-over of one constant premultiplied colour, scaled by per-pixel coverage ---------

    /// <summary>
    /// Composites the constant premultiplied colour <paramref name="color"/> (see <see cref="Pack"/>) over
    /// <paramref name="dst"/> (RGBA, premultiplied), each pixel weighted by its 0-255 <paramref name="coverage"/>.
    /// </summary>
    public static void BlendSolid(Span<byte> dst, ReadOnlySpan<byte> coverage, uint color)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dst.Length, coverage.Length * 4);

        var done = 0;
        if (System.Runtime.Intrinsics.X86.Avx2.IsSupported && coverage.Length >= 8)
            done = BlendSolidVector256(dst, coverage, color);

        if (Vector128.IsHardwareAccelerated && coverage.Length - done >= 4)
            done = BlendSolidVector128(dst, coverage, color, done);

        BlendSolidScalar(dst, coverage, color, done);
    }

    /// <summary>The scalar reference for <see cref="BlendSolid"/>, starting at pixel <paramref name="start"/>.</summary>
    internal static void BlendSolidScalar(Span<byte> dst, ReadOnlySpan<byte> coverage, uint color, int start = 0)
    {
        int cr = (int)(color & 0xFF), cg = (int)((color >> 8) & 0xFF), cb = (int)((color >> 16) & 0xFF), ca = (int)(color >> 24);

        for (var i = start; i < coverage.Length; i++)
        {
            var c = coverage[i];
            if (c == 0)
                continue;

            var p = i * 4;
            int sr, sg, sb, sa;
            if (c == 255)
            {
                sr = cr; sg = cg; sb = cb; sa = ca;
            }
            else
            {
                sr = Div255(cr * c); sg = Div255(cg * c); sb = Div255(cb * c); sa = Div255(ca * c);
            }

            if (sa == 255)
            {
                dst[p] = (byte)sr; dst[p + 1] = (byte)sg; dst[p + 2] = (byte)sb; dst[p + 3] = 255;
                continue;
            }

            var inv = 255 - sa;
            dst[p] = (byte)Math.Min(255, sr + Div255(dst[p] * inv));
            dst[p + 1] = (byte)Math.Min(255, sg + Div255(dst[p + 1] * inv));
            dst[p + 2] = (byte)Math.Min(255, sb + Div255(dst[p + 2] * inv));
            dst[p + 3] = (byte)Math.Min(255, sa + Div255(dst[p + 3] * inv));
        }
    }

    internal static int BlendSolidVector128(Span<byte> dst, ReadOnlySpan<byte> coverage, uint color, int start = 0)
    {
        var pixelCount = coverage.Length / 4 * 4;
        ref var dstRef = ref MemoryMarshal.GetReference(dst);
        ref var covRef = ref MemoryMarshal.GetReference(coverage);

        var colorBytes = Vector128.Create(color).AsByte();
        var colorLo = Vector128.WidenLower(colorBytes);
        var colorHi = Vector128.WidenUpper(colorBytes);
        var rounding = Vector128.Create((ushort)128);
        var max = Vector128.Create((ushort)255);

        for (var i = start; i < pixelCount; i += 4)
        {
            var cov4 = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref covRef, i));
            if (cov4 == 0)
                continue;

            var cov = Vector128.Shuffle(Vector128.CreateScalar(cov4).AsByte(), Vector128.Create((byte)0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3));
            var covLo = Vector128.WidenLower(cov);
            var covHi = Vector128.WidenUpper(cov);

            var sLo = Div255(colorLo * covLo, rounding);
            var sHi = Div255(colorHi * covHi, rounding);
            var s = Vector128.Narrow(sLo, sHi);

            var invAlpha = Vector128.OnesComplement(Vector128.Shuffle(s, Vector128.Create((byte)3, 3, 3, 3, 7, 7, 7, 7, 11, 11, 11, 11, 15, 15, 15, 15)));

            var d = Vector128.LoadUnsafe(ref dstRef, (nuint)(i * 4));
            var dLo = Vector128.WidenLower(d);
            var dHi = Vector128.WidenUpper(d);
            var invLo = Vector128.WidenLower(invAlpha);
            var invHi = Vector128.WidenUpper(invAlpha);

            var rLo = Vector128.Min(sLo + Div255(dLo * invLo, rounding), max);
            var rHi = Vector128.Min(sHi + Div255(dHi * invHi, rounding), max);
            Vector128.Narrow(rLo, rHi).StoreUnsafe(ref dstRef, (nuint)(i * 4));
        }

        return pixelCount;
    }

    // ---- BlendSpan: source-over of a per-pixel premultiplied colour, scaled by per-pixel coverage ---------

    /// <summary>
    /// Composites <paramref name="src"/> (RGBA, premultiplied, one pixel per coverage entry) over
    /// <paramref name="dst"/>, each source pixel weighted by its <paramref name="coverage"/>.
    /// </summary>
    public static void BlendSpan(Span<byte> dst, ReadOnlySpan<byte> src, ReadOnlySpan<byte> coverage)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dst.Length, coverage.Length * 4);
        ArgumentOutOfRangeException.ThrowIfLessThan(src.Length, coverage.Length * 4);

        var done = 0;
        if (System.Runtime.Intrinsics.X86.Avx2.IsSupported && coverage.Length >= 8)
            done = BlendSpanVector256(dst, src, coverage);

        if (Vector128.IsHardwareAccelerated && coverage.Length - done >= 4)
            done = BlendSpanVector128(dst, src, coverage, done);

        BlendSpanScalar(dst, src, coverage, done);
    }

    internal static void BlendSpanScalar(Span<byte> dst, ReadOnlySpan<byte> src, ReadOnlySpan<byte> coverage, int start = 0)
    {
        for (var i = start; i < coverage.Length; i++)
        {
            var c = coverage[i];
            if (c == 0)
                continue;

            var p = i * 4;
            int sr = src[p], sg = src[p + 1], sb = src[p + 2], sa = src[p + 3];
            if (c != 255)
            {
                sr = Div255(sr * c); sg = Div255(sg * c); sb = Div255(sb * c); sa = Div255(sa * c);
            }

            if (sa == 255)
            {
                dst[p] = (byte)sr; dst[p + 1] = (byte)sg; dst[p + 2] = (byte)sb; dst[p + 3] = 255;
                continue;
            }

            var inv = 255 - sa;
            dst[p] = (byte)Math.Min(255, sr + Div255(dst[p] * inv));
            dst[p + 1] = (byte)Math.Min(255, sg + Div255(dst[p + 1] * inv));
            dst[p + 2] = (byte)Math.Min(255, sb + Div255(dst[p + 2] * inv));
            dst[p + 3] = (byte)Math.Min(255, sa + Div255(dst[p + 3] * inv));
        }
    }

    internal static int BlendSpanVector128(Span<byte> dst, ReadOnlySpan<byte> src, ReadOnlySpan<byte> coverage, int start = 0)
    {
        var pixelCount = coverage.Length / 4 * 4;
        ref var dstRef = ref MemoryMarshal.GetReference(dst);
        ref var srcRef = ref MemoryMarshal.GetReference(src);
        ref var covRef = ref MemoryMarshal.GetReference(coverage);

        var rounding = Vector128.Create((ushort)128);
        var max = Vector128.Create((ushort)255);

        for (var i = start; i < pixelCount; i += 4)
        {
            var cov4 = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref covRef, i));
            if (cov4 == 0)
                continue;

            var cov = Vector128.Shuffle(Vector128.CreateScalar(cov4).AsByte(), Vector128.Create((byte)0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3));
            var s0 = Vector128.LoadUnsafe(ref srcRef, (nuint)(i * 4));

            var sLo = Div255(Vector128.WidenLower(s0) * Vector128.WidenLower(cov), rounding);
            var sHi = Div255(Vector128.WidenUpper(s0) * Vector128.WidenUpper(cov), rounding);
            var s = Vector128.Narrow(sLo, sHi);

            var invAlpha = Vector128.OnesComplement(Vector128.Shuffle(s, Vector128.Create((byte)3, 3, 3, 3, 7, 7, 7, 7, 11, 11, 11, 11, 15, 15, 15, 15)));

            var d = Vector128.LoadUnsafe(ref dstRef, (nuint)(i * 4));
            var rLo = Vector128.Min(sLo + Div255(Vector128.WidenLower(d) * Vector128.WidenLower(invAlpha), rounding), max);
            var rHi = Vector128.Min(sHi + Div255(Vector128.WidenUpper(d) * Vector128.WidenUpper(invAlpha), rounding), max);
            Vector128.Narrow(rLo, rHi).StoreUnsafe(ref dstRef, (nuint)(i * 4));
        }

        return pixelCount;
    }

    /// <summary>Vector form of <see cref="Div255(int)"/> for values that fit in 16 bits (products of two bytes).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ushort> Div255(Vector128<ushort> x, Vector128<ushort> rounding)
    {
        var t = x + rounding;
        return Vector128.ShiftRightLogical(t + Vector128.ShiftRightLogical(t, 8), 8);
    }

    // ---- Coverage multiply: coverage[i] = Div255(coverage[i] * mask[i]) -----------------------------------

    /// <summary>Multiplies <paramref name="coverage"/> in place by a per-pixel clip <paramref name="mask"/>.</summary>
    public static void MultiplyCoverage(Span<byte> coverage, ReadOnlySpan<byte> mask)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mask.Length, coverage.Length);

        var done = 0;
        if (Vector128.IsHardwareAccelerated && coverage.Length >= 16)
            done = MultiplyCoverageVector128(coverage, mask);

        MultiplyCoverageScalar(coverage, mask, done);
    }

    internal static void MultiplyCoverageScalar(Span<byte> coverage, ReadOnlySpan<byte> mask, int start = 0)
    {
        for (var i = start; i < coverage.Length; i++)
            coverage[i] = (byte)Div255(coverage[i] * mask[i]);
    }

    internal static int MultiplyCoverageVector128(Span<byte> coverage, ReadOnlySpan<byte> mask)
    {
        var count = coverage.Length / 16 * 16;
        ref var covRef = ref MemoryMarshal.GetReference(coverage);
        ref var maskRef = ref MemoryMarshal.GetReference(mask);
        var rounding = Vector128.Create((ushort)128);

        for (var i = 0; i < count; i += 16)
        {
            var c = Vector128.LoadUnsafe(ref covRef, (nuint)i);
            var m = Vector128.LoadUnsafe(ref maskRef, (nuint)i);
            var lo = Div255(Vector128.WidenLower(c) * Vector128.WidenLower(m), rounding);
            var hi = Div255(Vector128.WidenUpper(c) * Vector128.WidenUpper(m), rounding);
            Vector128.Narrow(lo, hi).StoreUnsafe(ref covRef, (nuint)i);
        }

        return count;
    }
}

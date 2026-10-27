using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace PeachDrawing;

/// <summary>The Porter-Duff operators <see cref="PixelKernels.PorterDuff"/> evaluates.</summary>
internal enum PorterDuffOp
{
    /// <summary><c>a + b * (1 - alpha(a))</c></summary>
    Over,
    /// <summary><c>a * alpha(b)</c></summary>
    In,
    /// <summary><c>a * (1 - alpha(b))</c></summary>
    Out,
    /// <summary><c>a * alpha(b) + b * (1 - alpha(a))</c></summary>
    Atop,
    /// <summary><c>a * (1 - alpha(b)) + b * (1 - alpha(a))</c></summary>
    Xor,
}

internal static partial class PixelKernels
{
    // ---- Scale coverage by a constant: work[i] = Div255(work[i] * opacity) -------------------------------

    /// <summary>Scales every <paramref name="coverage"/> byte by <paramref name="opacity"/> (0-255).</summary>
    public static void ScaleCoverage(Span<byte> coverage, int opacity)
    {
        var done = 0;
        if (Vector128.IsHardwareAccelerated && coverage.Length >= 16)
            done = ScaleCoverageVector128(coverage, opacity);

        ScaleCoverageScalar(coverage, opacity, done);
    }

    internal static void ScaleCoverageScalar(Span<byte> coverage, int opacity, int start = 0)
    {
        for (var i = start; i < coverage.Length; i++)
            coverage[i] = (byte)Div255(coverage[i] * opacity);
    }

    internal static int ScaleCoverageVector128(Span<byte> coverage, int opacity)
    {
        var count = coverage.Length / 16 * 16;
        ref var covRef = ref MemoryMarshal.GetReference(coverage);
        var rounding = Vector128.Create((ushort)128);
        var factor = Vector128.Create((ushort)opacity);

        for (var i = 0; i < count; i += 16)
        {
            var c = Vector128.LoadUnsafe(ref covRef, (nuint)i);
            var lo = Div255(Vector128.WidenLower(c) * factor, rounding);
            var hi = Div255(Vector128.WidenUpper(c) * factor, rounding);
            Vector128.Narrow(lo, hi).StoreUnsafe(ref covRef, (nuint)i);
        }

        return count;
    }

    // ---- Erase: dst *= (255 - coverage), channel-wise ------------------------------------------------------

    /// <summary>Scales each RGBA pixel of <paramref name="dst"/> by one minus its 0-255 <paramref name="coverage"/>.</summary>
    public static void EraseSpan(Span<byte> dst, ReadOnlySpan<byte> coverage)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dst.Length, coverage.Length * 4);

        var done = 0;
        if (Vector128.IsHardwareAccelerated && coverage.Length >= 4)
            done = EraseSpanVector128(dst, coverage);

        EraseSpanScalar(dst, coverage, done);
    }

    internal static void EraseSpanScalar(Span<byte> dst, ReadOnlySpan<byte> coverage, int start = 0)
    {
        for (var i = start; i < coverage.Length; i++)
        {
            var keep = 255 - coverage[i];
            if (keep == 255)
                continue;

            var p = i * 4;
            dst[p] = (byte)Div255(dst[p] * keep);
            dst[p + 1] = (byte)Div255(dst[p + 1] * keep);
            dst[p + 2] = (byte)Div255(dst[p + 2] * keep);
            dst[p + 3] = (byte)Div255(dst[p + 3] * keep);
        }
    }

    internal static int EraseSpanVector128(Span<byte> dst, ReadOnlySpan<byte> coverage)
    {
        var pixelCount = coverage.Length / 4 * 4;
        ref var dstRef = ref MemoryMarshal.GetReference(dst);
        ref var covRef = ref MemoryMarshal.GetReference(coverage);
        var rounding = Vector128.Create((ushort)128);

        for (var i = 0; i < pixelCount; i += 4)
        {
            var cov4 = System.Runtime.CompilerServices.Unsafe.ReadUnaligned<uint>(ref System.Runtime.CompilerServices.Unsafe.Add(ref covRef, i));
            if (cov4 == 0)
                continue;

            var cov = Vector128.Shuffle(Vector128.CreateScalar(cov4).AsByte(), Vector128.Create((byte)0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3));
            var keep = Vector128.OnesComplement(cov);
            var d = Vector128.LoadUnsafe(ref dstRef, (nuint)(i * 4));
            var lo = Div255(Vector128.WidenLower(d) * Vector128.WidenLower(keep), rounding);
            var hi = Div255(Vector128.WidenUpper(d) * Vector128.WidenUpper(keep), rounding);
            Vector128.Narrow(lo, hi).StoreUnsafe(ref dstRef, (nuint)(i * 4));
        }

        return pixelCount;
    }

    // ---- Porter-Duff: Div255(a * fa) + Div255(b * fb), saturated -------------------------------------------
    //
    // Every operator is the sum of "a scaled by a function of alpha(b)" and optionally "b scaled by one minus
    // alpha(a)": over uses a unscaled (a factor of 255 is exact), in/atop use alpha(b), out/xor use its complement.
    // Div255 of zero is zero, so a missing term simply drops out.

    /// <summary>
    /// Composites premultiplied RGBA <paramref name="a"/> with <paramref name="b"/> under <paramref name="op"/> into
    /// <paramref name="result"/>; <paramref name="result"/> may be <paramref name="a"/> or <paramref name="b"/> (each pixel
    /// is read before it is written). With <paramref name="skipTransparentSource"/> a pixel whose <paramref name="a"/> alpha
    /// is zero becomes <paramref name="b"/> unchanged, whatever its colour channels hold.
    /// </summary>
    public static void PorterDuff(PorterDuffOp op, ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, Span<byte> result, bool skipTransparentSource = false)
    {
        var length = a.Length / 4 * 4;
        ArgumentOutOfRangeException.ThrowIfLessThan(b.Length, length);
        ArgumentOutOfRangeException.ThrowIfLessThan(result.Length, length);

        var done = 0;
        if (Vector128.IsHardwareAccelerated && length >= 16)
            done = PorterDuffVector128(op, a, b, result, skipTransparentSource, length);

        PorterDuffScalar(op, a, b, result, skipTransparentSource, length, done);
    }

    internal static void PorterDuffScalar(PorterDuffOp op, ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, Span<byte> result, bool skipTransparentSource, int length, int start = 0)
    {
        var useB = op is PorterDuffOp.Over or PorterDuffOp.Atop or PorterDuffOp.Xor;
        for (var i = start; i < length; i += 4)
        {
            int aa = a[i + 3];
            int ab = b[i + 3];
            if (skipTransparentSource && aa == 0)
            {
                result[i] = b[i];
                result[i + 1] = b[i + 1];
                result[i + 2] = b[i + 2];
                result[i + 3] = b[i + 3];
                continue;
            }

            var fa = op switch
            {
                PorterDuffOp.Over => 255,
                PorterDuffOp.In or PorterDuffOp.Atop => ab,
                _ => 255 - ab,
            };
            var fb = useB ? 255 - aa : 0;

            for (var c = 0; c < 4; c++)
            {
                var v = Div255(a[i + c] * fa) + (useB ? Div255(b[i + c] * fb) : 0);
                result[i + c] = (byte)Math.Min(v, 255);
            }
        }
    }

    internal static int PorterDuffVector128(PorterDuffOp op, ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, Span<byte> result, bool skipTransparentSource, int length)
    {
        var count = length / 16 * 16;
        ref var aRef = ref MemoryMarshal.GetReference(a);
        ref var bRef = ref MemoryMarshal.GetReference(b);
        ref var rRef = ref MemoryMarshal.GetReference(result);
        var rounding = Vector128.Create((ushort)128);
        var max = Vector128.Create((ushort)255);
        var useB = op is PorterDuffOp.Over or PorterDuffOp.Atop or PorterDuffOp.Xor;

        for (var i = 0; i < count; i += 16)
        {
            var va = Vector128.LoadUnsafe(ref aRef, (nuint)i);
            var vb = Vector128.LoadUnsafe(ref bRef, (nuint)i);
            var alphaA = Vector128.Shuffle(va, Vector128.Create((byte)3, 3, 3, 3, 7, 7, 7, 7, 11, 11, 11, 11, 15, 15, 15, 15));
            var alphaB = Vector128.Shuffle(vb, Vector128.Create((byte)3, 3, 3, 3, 7, 7, 7, 7, 11, 11, 11, 11, 15, 15, 15, 15));

            var fa = op switch
            {
                PorterDuffOp.Over => Vector128<byte>.AllBitsSet,
                PorterDuffOp.In or PorterDuffOp.Atop => alphaB,
                _ => Vector128.OnesComplement(alphaB),
            };

            var lo = Div255(Vector128.WidenLower(va) * Vector128.WidenLower(fa), rounding);
            var hi = Div255(Vector128.WidenUpper(va) * Vector128.WidenUpper(fa), rounding);
            if (useB)
            {
                var fb = Vector128.OnesComplement(alphaA);
                lo += Div255(Vector128.WidenLower(vb) * Vector128.WidenLower(fb), rounding);
                hi += Div255(Vector128.WidenUpper(vb) * Vector128.WidenUpper(fb), rounding);
            }

            var packed = Vector128.Narrow(Vector128.Min(lo, max), Vector128.Min(hi, max));
            if (skipTransparentSource)
                packed = Vector128.ConditionalSelect(Vector128.Equals(alphaA, Vector128<byte>.Zero), vb, packed);

            packed.StoreUnsafe(ref rRef, (nuint)i);
        }

        return count;
    }
}

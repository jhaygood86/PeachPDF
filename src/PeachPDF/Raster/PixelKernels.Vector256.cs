using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace PeachPDF.Raster;

/// <summary>The 256-bit paths of the hot kernels: the same integer arithmetic as the 128-bit ones, two lanes at a time.</summary>
internal static partial class PixelKernels
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<ushort> Div255(Vector256<ushort> x, Vector256<ushort> rounding)
    {
        var t = x + rounding;
        return Vector256.ShiftRightLogical(t + Vector256.ShiftRightLogical(t, 8), 8);
    }

    // The shuffle index vectors are written out at each call site: the JIT lowers a shuffle to one byte-shuffle instruction only when it
    // can see the indices are constants (a local, or a property, made a per-element software shuffle - 50x slower than a copy).

    internal static int BlendSolidVector256(Span<byte> dst, ReadOnlySpan<byte> coverage, uint color)
    {
        var pixelCount = coverage.Length / 8 * 8;
        ref var dstRef = ref MemoryMarshal.GetReference(dst);
        ref var covRef = ref MemoryMarshal.GetReference(coverage);

        var colorBytes = Vector256.Create(color).AsByte();
        var colorLo = Vector256.WidenLower(colorBytes);
        var colorHi = Vector256.WidenUpper(colorBytes);
        var rounding = Vector256.Create((ushort)128);
        var max = Vector256.Create((ushort)255);

        for (var i = 0; i < pixelCount; i += 8)
        {
            var cov8 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref covRef, i));
            if (cov8 == 0)
                continue;

            var cov = Avx2.Shuffle(
                Vector256.Create(Vector128.CreateScalar((uint)cov8).AsByte(), Vector128.CreateScalar((uint)(cov8 >> 32)).AsByte()),
                Vector256.Create((byte)0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3));
            var sLo = Div255(colorLo * Vector256.WidenLower(cov), rounding);
            var sHi = Div255(colorHi * Vector256.WidenUpper(cov), rounding);
            var s = Vector256.Narrow(sLo, sHi);

            var invAlpha = Vector256.OnesComplement(Avx2.Shuffle(s, Vector256.Create((byte)3, 3, 3, 3, 7, 7, 7, 7, 11, 11, 11, 11, 15, 15, 15, 15, 3, 3, 3, 3, 7, 7, 7, 7, 11, 11, 11, 11, 15, 15, 15, 15)));

            var d = Vector256.LoadUnsafe(ref dstRef, (nuint)(i * 4));
            var rLo = Vector256.Min(sLo + Div255(Vector256.WidenLower(d) * Vector256.WidenLower(invAlpha), rounding), max);
            var rHi = Vector256.Min(sHi + Div255(Vector256.WidenUpper(d) * Vector256.WidenUpper(invAlpha), rounding), max);
            Vector256.Narrow(rLo, rHi).StoreUnsafe(ref dstRef, (nuint)(i * 4));
        }

        return pixelCount;
    }

    internal static int BlendSpanVector256(Span<byte> dst, ReadOnlySpan<byte> src, ReadOnlySpan<byte> coverage)
    {
        var pixelCount = coverage.Length / 8 * 8;
        ref var dstRef = ref MemoryMarshal.GetReference(dst);
        ref var srcRef = ref MemoryMarshal.GetReference(src);
        ref var covRef = ref MemoryMarshal.GetReference(coverage);

        var rounding = Vector256.Create((ushort)128);
        var max = Vector256.Create((ushort)255);

        for (var i = 0; i < pixelCount; i += 8)
        {
            var cov8 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref covRef, i));
            if (cov8 == 0)
                continue;

            var cov = Avx2.Shuffle(
                Vector256.Create(Vector128.CreateScalar((uint)cov8).AsByte(), Vector128.CreateScalar((uint)(cov8 >> 32)).AsByte()),
                Vector256.Create((byte)0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3));
            var s0 = Vector256.LoadUnsafe(ref srcRef, (nuint)(i * 4));

            var sLo = Div255(Vector256.WidenLower(s0) * Vector256.WidenLower(cov), rounding);
            var sHi = Div255(Vector256.WidenUpper(s0) * Vector256.WidenUpper(cov), rounding);
            var s = Vector256.Narrow(sLo, sHi);

            var invAlpha = Vector256.OnesComplement(Avx2.Shuffle(s, Vector256.Create((byte)3, 3, 3, 3, 7, 7, 7, 7, 11, 11, 11, 11, 15, 15, 15, 15, 3, 3, 3, 3, 7, 7, 7, 7, 11, 11, 11, 11, 15, 15, 15, 15)));

            var d = Vector256.LoadUnsafe(ref dstRef, (nuint)(i * 4));
            var rLo = Vector256.Min(sLo + Div255(Vector256.WidenLower(d) * Vector256.WidenLower(invAlpha), rounding), max);
            var rHi = Vector256.Min(sHi + Div255(Vector256.WidenUpper(d) * Vector256.WidenUpper(invAlpha), rounding), max);
            Vector256.Narrow(rLo, rHi).StoreUnsafe(ref dstRef, (nuint)(i * 4));
        }

        return pixelCount;
    }
}

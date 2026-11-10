using PeachDrawing.Core;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace PeachDrawing;

public static partial class ColorMatrixFilter
{
    /// <summary>The largest matrix entry the vector path accepts; a bigger one (or a non-finite one) is left to the scalar code.</summary>
    private const float VectorEntryLimit = 1e6f;

    /// <summary>Whether every entry of <paramref name="matrix"/> is finite and small enough that no intermediate can overflow to infinity.</summary>
    private static bool IsVectorSafe(in ColorMatrix matrix)
    {
        var m = matrix.Linear;
        var o = matrix.Offset;
        ReadOnlySpan<float> entries =
        [
            m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44, o.X, o.Y, o.Z, o.W,
        ];

        foreach (var entry in entries)
        {
            if (!(Math.Abs(entry) <= VectorEntryLimit))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Applies <paramref name="stages"/>, one after another, to the first multiple of four pixels of <paramref name="source"/>, four pixels at a
    /// time, and returns how many bytes it handled. Every operation is the scalar code's, in the scalar code's order, on the same single-precision
    /// values - separate multiplies and adds, never fused - so the bytes written are the ones the scalar loop writes.
    /// </summary>
    internal static int ApplyVector128(ReadOnlySpan<byte> source, Span<byte> destination, ReadOnlySpan<ColorMatrix> stages)
    {
        var count = source.Length / 16 * 16;
        ref var srcRef = ref MemoryReference(source);
        ref var dstRef = ref MemoryMarshal.GetReference(destination);

        // The 20 coefficients of every stage as broadcast vectors, in the order of the arithmetic below.
        Span<Vector128<float>> coefficients = stackalloc Vector128<float>[stages.Length * 20];
        for (var s = 0; s < stages.Length; s++)
        {
            var m = stages[s].Linear;
            var o = stages[s].Offset;
            var c = coefficients.Slice(s * 20, 20);
            c[0] = Vector128.Create(m.M11); c[1] = Vector128.Create(m.M21); c[2] = Vector128.Create(m.M31); c[3] = Vector128.Create(m.M41); c[4] = Vector128.Create(o.X);
            c[5] = Vector128.Create(m.M12); c[6] = Vector128.Create(m.M22); c[7] = Vector128.Create(m.M32); c[8] = Vector128.Create(m.M42); c[9] = Vector128.Create(o.Y);
            c[10] = Vector128.Create(m.M13); c[11] = Vector128.Create(m.M23); c[12] = Vector128.Create(m.M33); c[13] = Vector128.Create(m.M43); c[14] = Vector128.Create(o.Z);
            c[15] = Vector128.Create(m.M14); c[16] = Vector128.Create(m.M24); c[17] = Vector128.Create(m.M34); c[18] = Vector128.Create(m.M44); c[19] = Vector128.Create(o.W);
        }

        var zero = Vector128<float>.Zero;
        var one = Vector128.Create(1f);
        var scale = Vector128.Create(255f);

        for (var i = 0; i < count; i += 16)
        {
            var pixels = Vector128.LoadUnsafe(ref srcRef, (nuint)i);

            // Channel k of the four pixels, widened to 32 bits in the four lanes (an index of 255 selects zero).
            var rU = Vector128.WidenLower(Vector128.WidenLower(Vector128.Shuffle(pixels, Vector128.Create((byte)0, 4, 8, 12, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255))));
            var gU = Vector128.WidenLower(Vector128.WidenLower(Vector128.Shuffle(pixels, Vector128.Create((byte)1, 5, 9, 13, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255))));
            var bU = Vector128.WidenLower(Vector128.WidenLower(Vector128.Shuffle(pixels, Vector128.Create((byte)2, 6, 10, 14, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255))));
            var aU = Vector128.WidenLower(Vector128.WidenLower(Vector128.Shuffle(pixels, Vector128.Create((byte)3, 7, 11, 15, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255))));

            var a = Vector128.ConvertToSingle(aU.AsInt32()) / scale;
            var transparent = Vector128.Equals(aU, Vector128<uint>.Zero).AsSingle();

            // Straight color: min(1, channel / 255 / alpha), and zero where alpha is zero (the lanes' 0/0 are discarded here).
            var r = Vector128.ConditionalSelect(transparent, zero, Vector128.Min(one, Vector128.ConvertToSingle(rU.AsInt32()) / scale / a));
            var g = Vector128.ConditionalSelect(transparent, zero, Vector128.Min(one, Vector128.ConvertToSingle(gU.AsInt32()) / scale / a));
            var b = Vector128.ConditionalSelect(transparent, zero, Vector128.Min(one, Vector128.ConvertToSingle(bU.AsInt32()) / scale / a));

            for (var s = 0; s < stages.Length; s++)
            {
                var c = coefficients.Slice(s * 20, 20);
                var nr = r * c[0] + g * c[1] + b * c[2] + a * c[3] + c[4];
                var ng = r * c[5] + g * c[6] + b * c[7] + a * c[8] + c[9];
                var nb = r * c[10] + g * c[11] + b * c[12] + a * c[13] + c[14];
                var na = r * c[15] + g * c[16] + b * c[17] + a * c[18] + c[19];
                r = Vector128.Min(Vector128.Max(nr, zero), one);
                g = Vector128.Min(Vector128.Max(ng, zero), one);
                b = Vector128.Min(Vector128.Max(nb, zero), one);
                a = Vector128.Min(Vector128.Max(na, zero), one);
            }

            // Premultiply, round half to even like MathF.Round, and store as bytes.
            var outR = Vector128.ConvertToInt32(Vector128.Round(r * a * scale));
            var outG = Vector128.ConvertToInt32(Vector128.Round(g * a * scale));
            var outB = Vector128.ConvertToInt32(Vector128.Round(b * a * scale));
            var outA = Vector128.ConvertToInt32(Vector128.Round(a * scale));

            var rg = Vector128.Narrow(outR.AsUInt32(), outG.AsUInt32());
            var ba = Vector128.Narrow(outB.AsUInt32(), outA.AsUInt32());
            var planar = Vector128.Narrow(rg, ba);
            Vector128.Shuffle(planar, Vector128.Create((byte)0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15)).StoreUnsafe(ref dstRef, (nuint)i);
        }

        return count;
    }

    private static ref byte MemoryReference(ReadOnlySpan<byte> span) => ref MemoryMarshal.GetReference(span);
}

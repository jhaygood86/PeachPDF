using System;
using System.Runtime.Intrinsics;

namespace PeachDrawing.Filters;

public static partial class FilterOps
{
    /// <summary>The four channels of the pixel at <paramref name="i"/> as doubles in 0-1, one per lane of a 256-bit vector.</summary>
    private static Vector256<double> PixelToUnit(ReadOnlySpan<byte> pixels, int i) =>
        Vector256.Create(ByteOverTwoFiftyFive[pixels[i]], ByteOverTwoFiftyFive[pixels[i + 1]], ByteOverTwoFiftyFive[pixels[i + 2]], ByteOverTwoFiftyFive[pixels[i + 3]]);

    /// <summary>
    /// The 256-bit form of <see cref="ArithmeticVector128"/>: one pixel's four channels in one <see cref="Vector256{T}"/> (AVX on x64; not hardware accelerated on
    /// ARM64, where the 128-bit form runs): the same multiplies and additions in the same order, so the bytes are the scalar loop's. Returns how many bytes it handled.
    /// </summary>
    internal static int ArithmeticVector256(ReadOnlySpan<byte> pa, ReadOnlySpan<byte> pb, Span<byte> pr, double k1, double k2, double k3, double k4)
    {
        var vk1 = Vector256.Create(k1);
        var vk2 = Vector256.Create(k2);
        var vk3 = Vector256.Create(k3);
        var vk4 = Vector256.Create(k4);
        var scale = Vector256.Create(255.0);
        var zero = Vector256<double>.Zero;
        var one = Vector256.Create(1.0);

        var count = pa.Length / 4 * 4;
        for (var i = 0; i < count; i += 4)
        {
            var ia = PixelToUnit(pa, i);
            var ib = PixelToUnit(pb, i);
            var v = vk1 * ia * ib + vk2 * ia + vk3 * ib + vk4;
            v = Vector256.Min(Vector256.Max(v, zero), one);

            // Color is kept within alpha; in the alpha lane this is its own value.
            v = Vector256.Min(v, Vector256.Create(v.GetElement(3)));

            var rounded = Vector256.Round(v * scale);
            pr[i] = (byte)rounded.GetElement(0);
            pr[i + 1] = (byte)rounded.GetElement(1);
            pr[i + 2] = (byte)rounded.GetElement(2);
            pr[i + 3] = (byte)rounded.GetElement(3);
        }

        return count;
    }
}

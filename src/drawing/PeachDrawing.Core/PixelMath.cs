using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace PeachDrawing.Core;

/// <summary>Integer pixel arithmetic shared by every raster consumer of <see cref="PixelBuffer"/> and <see cref="RasterSurface"/>.</summary>
public static class PixelMath
{
    /// <summary>
    /// <paramref name="x"/> / 255, rounded to nearest: the exact result for every product of two 8-bit values (<c>0..65025</c>),
    /// without a division. This is how a channel is scaled by an alpha or coverage byte.
    /// </summary>
    /// <param name="x">the value to divide, normally the product of two bytes</param>
    /// <returns>the rounded quotient</returns>
    public static int Div255(int x) => (x + 128 + ((x + 128) >> 8)) >> 8;

    /// <summary>Premultiplies straight-alpha RGBA8 in place: each colour channel is scaled by the pixel's alpha.</summary>
    /// <param name="rgba">the pixels, four bytes each in R, G, B, A order; converted in place</param>
    public static void Premultiply(Span<byte> rgba)
    {
        var done = 0;
        if (Vector128.IsHardwareAccelerated && rgba.Length >= 16)
            done = PremultiplyVector128(rgba);

        PremultiplyScalar(rgba, done);
    }

    private static void PremultiplyScalar(Span<byte> rgba, int start)
    {
        for (var i = start; i < rgba.Length; i += 4)
        {
            int a = rgba[i + 3];
            if (a == 255)
                continue;

            rgba[i] = (byte)Div255(rgba[i] * a);
            rgba[i + 1] = (byte)Div255(rgba[i + 1] * a);
            rgba[i + 2] = (byte)Div255(rgba[i + 2] * a);
        }
    }

    // Four pixels per step; the same exact integer arithmetic as the scalar loop, so the bytes are identical on every CPU.
    private static int PremultiplyVector128(Span<byte> rgba)
    {
        var count = rgba.Length / 16 * 16;
        ref var r = ref MemoryMarshal.GetReference(rgba);
        var rounding = Vector128.Create((ushort)128);
        var alphaLanes = Vector128.Create((byte)0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255);

        for (var i = 0; i < count; i += 16)
        {
            var px = Vector128.LoadUnsafe(ref r, (nuint)i);
            var alpha = Vector128.Shuffle(px, Vector128.Create((byte)3, 3, 3, 3, 7, 7, 7, 7, 11, 11, 11, 11, 15, 15, 15, 15));
            var lo = Div255Vector(Vector128.WidenLower(px) * Vector128.WidenLower(alpha), rounding);
            var hi = Div255Vector(Vector128.WidenUpper(px) * Vector128.WidenUpper(alpha), rounding);
            Vector128.ConditionalSelect(alphaLanes, px, Vector128.Narrow(lo, hi)).StoreUnsafe(ref r, (nuint)i);
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ushort> Div255Vector(Vector128<ushort> x, Vector128<ushort> rounding)
    {
        var t = x + rounding;
        return Vector128.ShiftRightLogical(t + Vector128.ShiftRightLogical(t, 8), 8);
    }

    /// <summary>Converts premultiplied RGBA8 to straight alpha into <paramref name="destination"/> (same length).</summary>
    /// <param name="premultiplied">the premultiplied pixels, four bytes each in R, G, B, A order</param>
    /// <param name="destination">receives the straight-alpha pixels; the same length as <paramref name="premultiplied"/></param>
    public static void Unpremultiply(ReadOnlySpan<byte> premultiplied, Span<byte> destination)
    {
        var done = 0;
        if (Vector128.IsHardwareAccelerated && premultiplied.Length >= 16 && destination.Length >= premultiplied.Length)
            done = UnpremultiplyVector128(premultiplied, destination);

        UnpremultiplyScalar(premultiplied, destination, done);
    }

    private static void UnpremultiplyScalar(ReadOnlySpan<byte> premultiplied, Span<byte> destination, int start)
    {
        for (var i = start; i < premultiplied.Length; i += 4)
        {
            int a = premultiplied[i + 3];
            if (a == 255)
            {
                destination[i] = premultiplied[i];
                destination[i + 1] = premultiplied[i + 1];
                destination[i + 2] = premultiplied[i + 2];
                destination[i + 3] = 255;
            }
            else if (a == 0)
            {
                destination[i] = destination[i + 1] = destination[i + 2] = destination[i + 3] = 0;
            }
            else
            {
                destination[i] = (byte)Math.Min(255, (premultiplied[i] * 255 + a / 2) / a);
                destination[i + 1] = (byte)Math.Min(255, (premultiplied[i + 1] * 255 + a / 2) / a);
                destination[i + 2] = (byte)Math.Min(255, (premultiplied[i + 2] * 255 + a / 2) / a);
                destination[i + 3] = (byte)a;
            }
        }
    }

    // (c * 255 + a / 2) / a with the division done in float32: the numerator is at most 65152 (exact in a float), and a
    // non-integer quotient is at least 1/255 from the next integer, far more than the 2^-24 relative error of an IEEE
    // division, so truncating the float quotient gives the same integer the scalar loop does, on every CPU.
    private static int UnpremultiplyVector128(ReadOnlySpan<byte> premultiplied, Span<byte> destination)
    {
        var count = premultiplied.Length / 16 * 16;
        ref var src = ref MemoryMarshal.GetReference(premultiplied);
        ref var dst = ref MemoryMarshal.GetReference(destination);
        var max = Vector128.Create(255u);
        var alphaLane = Vector128.Create(0u, 0u, 0u, uint.MaxValue);

        for (var i = 0; i < count; i += 16)
        {
            var px = Vector128.LoadUnsafe(ref src, (nuint)i);
            var lo = Vector128.WidenLower(px);
            var hi = Vector128.WidenUpper(px);

            var p0 = UnpremultiplyPixel(Vector128.WidenLower(lo), max, alphaLane);
            var p1 = UnpremultiplyPixel(Vector128.WidenUpper(lo), max, alphaLane);
            var p2 = UnpremultiplyPixel(Vector128.WidenLower(hi), max, alphaLane);
            var p3 = UnpremultiplyPixel(Vector128.WidenUpper(hi), max, alphaLane);

            Vector128.Narrow(Vector128.Narrow(p0, p1), Vector128.Narrow(p2, p3)).StoreUnsafe(ref dst, (nuint)i);
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> UnpremultiplyPixel(Vector128<uint> channels, Vector128<uint> max, Vector128<uint> alphaLane)
    {
        var alpha = Vector128.Shuffle(channels, Vector128.Create(3u, 3u, 3u, 3u));
        var numerator = channels * Vector128.Create(255u) + Vector128.ShiftRightLogical(alpha, 1);
        var quotient = Vector128.ConvertToUInt32(Vector128.ConvertToSingle(numerator) / Vector128.ConvertToSingle(alpha));
        var straight = Vector128.ConditionalSelect(alphaLane, channels, Vector128.Min(quotient, max));
        return Vector128.ConditionalSelect(Vector128.Equals(alpha, Vector128<uint>.Zero), Vector128<uint>.Zero, straight);
    }
}

using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace PeachDrawing;

/// <summary>
/// The per-pixel kernels behind <c>DrawImageMasked</c> and <c>DrawImageAlphaMasked</c>: each pixel of a premultiplied image is multiplied, every
/// channel, by a mask value taken from the mask's pixel (its luminance over black, or its alpha, or the inverse of its alpha). Exact 8-bit fixed
/// point, like the other kernels, so the vector form writes the scalar form's bytes.
/// </summary>
internal static partial class PixelKernels
{
    /// <summary>
    /// <c>image * lum(mask)</c> per pixel, with <c>lum = (77 r + 151 g + 28 b + 128) &gt;&gt; 8</c> of the mask's premultiplied color (its luminance over black).
    /// <paramref name="result"/> may be <paramref name="image"/>.
    /// </summary>
    public static void MaskLuminance(ReadOnlySpan<byte> image, ReadOnlySpan<byte> mask, Span<byte> result)
    {
        var length = image.Length / 4 * 4;
        ArgumentOutOfRangeException.ThrowIfLessThan(mask.Length, length);
        ArgumentOutOfRangeException.ThrowIfLessThan(result.Length, length);

        var done = 0;
        if (Vector128.IsHardwareAccelerated && length >= 16)
            done = MaskLuminanceVector128(image, mask, result, length);

        MaskLuminanceScalar(image, mask, result, length, done);
    }

    internal static void MaskLuminanceScalar(ReadOnlySpan<byte> image, ReadOnlySpan<byte> mask, Span<byte> result, int length, int start = 0)
    {
        for (var i = start; i < length; i += 4)
        {
            var lum = (77 * mask[i] + 151 * mask[i + 1] + 28 * mask[i + 2] + 128) >> 8;
            result[i] = (byte)Div255(image[i] * lum);
            result[i + 1] = (byte)Div255(image[i + 1] * lum);
            result[i + 2] = (byte)Div255(image[i + 2] * lum);
            result[i + 3] = (byte)Div255(image[i + 3] * lum);
        }
    }

    internal static int MaskLuminanceVector128(ReadOnlySpan<byte> image, ReadOnlySpan<byte> mask, Span<byte> result, int length)
    {
        var count = length / 16 * 16;
        ref var imageRef = ref MemoryMarshal.GetReference(image);
        ref var maskRef = ref MemoryMarshal.GetReference(mask);
        ref var resultRef = ref MemoryMarshal.GetReference(result);
        var rounding = Vector128.Create((ushort)128);
        var wr = Vector128.Create((ushort)77);
        var wg = Vector128.Create((ushort)151);
        var wb = Vector128.Create((ushort)28);

        for (var i = 0; i < count; i += 16)
        {
            var pixels = Vector128.LoadUnsafe(ref imageRef, (nuint)i);
            var m = Vector128.LoadUnsafe(ref maskRef, (nuint)i);

            // The mask's R, G and B of the four pixels as 16-bit lanes (an index of 255 selects zero). 77 * 255 + 151 * 255 + 28 * 255 + 128 is
            // 65408, so the weighted sum fits in 16 bits.
            var r = Vector128.WidenLower(Vector128.Shuffle(m, Vector128.Create((byte)0, 4, 8, 12, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255)));
            var g = Vector128.WidenLower(Vector128.Shuffle(m, Vector128.Create((byte)1, 5, 9, 13, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255)));
            var b = Vector128.WidenLower(Vector128.Shuffle(m, Vector128.Create((byte)2, 6, 10, 14, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255)));
            var lum = Vector128.ShiftRightLogical(r * wr + g * wg + b * wb + rounding, 8);

            // One luminance byte per pixel, repeated across that pixel's four channels.
            var lumBytes = Vector128.Narrow(lum, lum);
            var perChannel = Vector128.Shuffle(lumBytes, Vector128.Create((byte)0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3));

            var lo = Div255(Vector128.WidenLower(pixels) * Vector128.WidenLower(perChannel), rounding);
            var hi = Div255(Vector128.WidenUpper(pixels) * Vector128.WidenUpper(perChannel), rounding);
            Vector128.Narrow(lo, hi).StoreUnsafe(ref resultRef, (nuint)i);
        }

        return count;
    }

    /// <summary>
    /// <c>image * k</c> per pixel, <c>k</c> being the mask's alpha, or 255 minus it when <paramref name="invert"/>.
    /// <paramref name="result"/> may be <paramref name="image"/>.
    /// </summary>
    public static void MaskAlpha(ReadOnlySpan<byte> image, ReadOnlySpan<byte> mask, Span<byte> result, bool invert)
    {
        var length = image.Length / 4 * 4;
        ArgumentOutOfRangeException.ThrowIfLessThan(mask.Length, length);
        ArgumentOutOfRangeException.ThrowIfLessThan(result.Length, length);

        var done = 0;
        if (Vector128.IsHardwareAccelerated && length >= 16)
            done = MaskAlphaVector128(image, mask, result, length, invert);

        MaskAlphaScalar(image, mask, result, length, invert, done);
    }

    internal static void MaskAlphaScalar(ReadOnlySpan<byte> image, ReadOnlySpan<byte> mask, Span<byte> result, int length, bool invert, int start = 0)
    {
        for (var i = start; i < length; i += 4)
        {
            var k = invert ? 255 - mask[i + 3] : mask[i + 3];
            result[i] = (byte)Div255(image[i] * k);
            result[i + 1] = (byte)Div255(image[i + 1] * k);
            result[i + 2] = (byte)Div255(image[i + 2] * k);
            result[i + 3] = (byte)Div255(image[i + 3] * k);
        }
    }

    internal static int MaskAlphaVector128(ReadOnlySpan<byte> image, ReadOnlySpan<byte> mask, Span<byte> result, int length, bool invert)
    {
        var count = length / 16 * 16;
        ref var imageRef = ref MemoryMarshal.GetReference(image);
        ref var maskRef = ref MemoryMarshal.GetReference(mask);
        ref var resultRef = ref MemoryMarshal.GetReference(result);
        var rounding = Vector128.Create((ushort)128);

        for (var i = 0; i < count; i += 16)
        {
            var pixels = Vector128.LoadUnsafe(ref imageRef, (nuint)i);
            var m = Vector128.LoadUnsafe(ref maskRef, (nuint)i);

            // The mask's alpha of each pixel, repeated across that pixel's four channels.
            var k = Vector128.Shuffle(m, Vector128.Create((byte)3, 3, 3, 3, 7, 7, 7, 7, 11, 11, 11, 11, 15, 15, 15, 15));
            if (invert)
                k = Vector128.OnesComplement(k);

            var lo = Div255(Vector128.WidenLower(pixels) * Vector128.WidenLower(k), rounding);
            var hi = Div255(Vector128.WidenUpper(pixels) * Vector128.WidenUpper(k), rounding);
            Vector128.Narrow(lo, hi).StoreUnsafe(ref resultRef, (nuint)i);
        }

        return count;
    }
}

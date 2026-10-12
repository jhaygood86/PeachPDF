using System;

namespace PeachPDF.PDF.Internal;

/// <summary>
/// Row-wise conversions of 32-bit BGRA pixels into the separate sample planes a PDF image XObject holds.
/// </summary>
internal static class BgraPixels
{
    /// <summary>
    /// Splits one row of BGRA pixels into RGB samples and an alpha plane.
    /// </summary>
    /// <param name="bgra">The row, four bytes per pixel: blue, green, red, alpha.</param>
    /// <param name="rgb">Receives three bytes per pixel: red, green, blue.</param>
    /// <param name="alpha">Receives one byte per pixel.</param>
    public static void SplitRow(ReadOnlySpan<byte> bgra, Span<byte> rgb, Span<byte> alpha)
    {
        int count = alpha.Length;
        if (bgra.Length < 4 * count || rgb.Length < 3 * count)
            throw new ArgumentException("The pixel buffers are too short for the alpha plane.");

        for (int i = 0; i < count; i++)
        {
            rgb[3 * i] = bgra[4 * i + 2];
            rgb[3 * i + 1] = bgra[4 * i + 1];
            rgb[3 * i + 2] = bgra[4 * i];
            alpha[i] = bgra[4 * i + 3];
        }
    }

    /// <summary>
    /// Packs a row of alpha values into a 1-bit mask, eight pixels per byte, the first pixel in the highest bit.
    /// A set bit marks a transparent pixel (alpha below 128); the unused bits of the last byte are zero.
    /// </summary>
    public static void PackTransparency(ReadOnlySpan<byte> alpha, Span<byte> packed)
    {
        if (packed.Length < (alpha.Length + 7) / 8)
            throw new ArgumentException("The mask row is too short for the alpha plane.");

        packed.Clear();
        for (int i = 0; i < alpha.Length; i++)
        {
            if (alpha[i] < 128)
                packed[i >> 3] |= (byte)(0x80 >> (i & 7));
        }
    }

    /// <summary>
    /// Records whether a row holds any pixel that is not fully opaque, and any that is partly transparent.
    /// </summary>
    public static void Classify(ReadOnlySpan<byte> alpha, ref bool anyTransparent, ref bool anyPartlyTransparent)
    {
        anyTransparent |= alpha.IndexOfAnyExcept((byte)255) >= 0;
        anyPartlyTransparent |= alpha.IndexOfAnyExcept((byte)0, (byte)255) >= 0;
    }
}

using System;

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
        for (var i = 0; i < rgba.Length; i += 4)
        {
            int a = rgba[i + 3];
            if (a == 255)
                continue;

            rgba[i] = (byte)Div255(rgba[i] * a);
            rgba[i + 1] = (byte)Div255(rgba[i + 1] * a);
            rgba[i + 2] = (byte)Div255(rgba[i + 2] * a);
        }
    }

    /// <summary>Converts premultiplied RGBA8 to straight alpha into <paramref name="destination"/> (same length).</summary>
    /// <param name="premultiplied">the premultiplied pixels, four bytes each in R, G, B, A order</param>
    /// <param name="destination">receives the straight-alpha pixels; the same length as <paramref name="premultiplied"/></param>
    public static void Unpremultiply(ReadOnlySpan<byte> premultiplied, Span<byte> destination)
    {
        for (var i = 0; i < premultiplied.Length; i += 4)
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
}

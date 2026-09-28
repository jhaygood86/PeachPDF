using System;

namespace PeachDrawing;

/// <summary>A decoded bitmap as premultiplied RGBA8, ready for sampling.</summary>
internal sealed class Bitmap
{
    public Bitmap(int width, int height, byte[] premultiplied)
    {
        Width = width;
        Height = height;
        Pixels = premultiplied;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    /// <summary>Premultiplies straight-alpha RGBA in place.</summary>
    public static void Premultiply(Span<byte> rgba)
    {
        for (var i = 0; i < rgba.Length; i += 4)
        {
            int a = rgba[i + 3];
            if (a == 255)
                continue;

            rgba[i] = (byte)PixelKernels.Div255(rgba[i] * a);
            rgba[i + 1] = (byte)PixelKernels.Div255(rgba[i + 1] * a);
            rgba[i + 2] = (byte)PixelKernels.Div255(rgba[i + 2] * a);
        }
    }

    /// <summary>Converts premultiplied RGBA to straight alpha into <paramref name="destination"/> (same length).</summary>
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

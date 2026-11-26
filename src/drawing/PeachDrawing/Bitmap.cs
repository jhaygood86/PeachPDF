using PeachDrawing.Core;
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

    /// <summary>The same pixels as premultiplied floats (RGBA, four per pixel) that are not held to 0 to 1, or null for a bitmap of eight bits only.</summary>
    public float[]? Wide { get; init; }

    /// <summary>Premultiplies straight-alpha RGBA in place.</summary>
    public static void Premultiply(Span<byte> rgba) => PixelMath.Premultiply(rgba);

    /// <summary>Converts premultiplied RGBA to straight alpha into <paramref name="destination"/> (same length).</summary>
    public static void Unpremultiply(ReadOnlySpan<byte> premultiplied, Span<byte> destination) => PixelMath.Unpremultiply(premultiplied, destination);
}

using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Runtime.CompilerServices;

namespace PeachPDF.Raster;

/// <summary>
/// Implemented by an image source that can hand its decoded pixels to the raster backend. Optional on
/// purpose: <c>ImageSource.IImageSource</c> is the PDF embedder's contract and has no need for pixel access.
/// </summary>
internal interface IRgbaPixelProvider
{
    /// <summary>
    /// Decodes to tightly packed, straight-alpha RGBA8 (<c>width * height * 4</c> bytes). Returns false when
    /// the source has no RGB rendition (a CMYK image).
    /// </summary>
    bool TryGetRgba(out int width, out int height, out byte[] rgba);
}

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

/// <summary>Decodes an <see cref="XImage"/> to a <see cref="Bitmap"/> once and remembers the result for the image's lifetime.</summary>
internal static class ImageBitmaps
{
    private static readonly ConditionalWeakTable<XImage, Bitmap?> Cache = [];

    public static Bitmap? Get(XImage image)
    {
        if (Cache.TryGetValue(image, out var cached))
            return cached;

        Bitmap? bitmap = null;
        if (image.TryGetRgba(out var w, out var h, out var rgba) && w > 0 && h > 0 && rgba.Length >= w * h * 4)
        {
            Bitmap.Premultiply(rgba.AsSpan(0, w * h * 4));
            bitmap = new Bitmap(w, h, rgba);
        }

        Cache.AddOrUpdate(image, bitmap);
        return bitmap;
    }
}

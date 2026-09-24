using System;
using System.Buffers;

namespace PeachPDF.Raster.Filters;

/// <summary>
/// CSS <c>drop-shadow()</c> (Filter Effects 1 §8.3): "a blurred, offset version of the input image's alpha mask drawn in a
/// particular colour, composited below the image". The shadow follows the real alpha shape of what was painted - glyphs,
/// a PNG's transparent corners, a clipped outline - not its bounding box.
/// </summary>
internal static class DropShadow
{
    /// <summary>
    /// Adds a shadow under the pixels of <paramref name="surface"/> in place: its alpha, tinted with the premultiplied
    /// shadow colour (<paramref name="r"/>, <paramref name="g"/>, <paramref name="b"/>, <paramref name="a"/>), moved by
    /// (<paramref name="dx"/>, <paramref name="dy"/>) pixels, and blurred with standard deviations <paramref name="sigmaX"/> and
    /// <paramref name="sigmaY"/> pixels.
    /// </summary>
    /// <remarks>Offsets are rounded to whole pixels; at the raster resolutions in use a rounding error is a small fraction of a point.</remarks>
    public static void Apply(RasterSurface surface, int dx, int dy, double sigmaX, double sigmaY, byte r, byte g, byte b, byte a)
    {
        var width = surface.Width;
        var height = surface.Height;
        var length = width * height * 4;

        using var shadow = new RasterSurface(width, height, surface.GridX, surface.GridY, surface.PixelsPerUnitX, surface.PixelsPerUnitY);
        var content = surface.Pixels;
        var shade = shadow.Pixels;

        for (var y = 0; y < height; y++)
        {
            var sy = y - dy;
            if (sy < 0 || sy >= height)
                continue;

            for (var x = 0; x < width; x++)
            {
                var sx = x - dx;
                if (sx < 0 || sx >= width)
                    continue;

                int alpha = content[(sy * width + sx) * 4 + 3];
                if (alpha == 0)
                    continue;

                // The shadow colour, scaled by the source's alpha (and by its own alpha, already premultiplied in a).
                var p = (y * width + x) * 4;
                shade[p] = (byte)PixelKernels.Div255(r * alpha);
                shade[p + 1] = (byte)PixelKernels.Div255(g * alpha);
                shade[p + 2] = (byte)PixelKernels.Div255(b * alpha);
                shade[p + 3] = (byte)PixelKernels.Div255(a * alpha);
            }
        }

        if (sigmaX > 0 || sigmaY > 0)
            GaussianBlur.Apply(shadow, sigmaX, sigmaY);

        // content over shadow.
        for (var i = 0; i < length; i += 4)
        {
            var inverse = 255 - content[i + 3];
            if (inverse == 0)
                continue;

            content[i] = (byte)Math.Min(255, content[i] + PixelKernels.Div255(shade[i] * inverse));
            content[i + 1] = (byte)Math.Min(255, content[i + 1] + PixelKernels.Div255(shade[i + 1] * inverse));
            content[i + 2] = (byte)Math.Min(255, content[i + 2] + PixelKernels.Div255(shade[i + 2] * inverse));
            content[i + 3] = (byte)Math.Min(255, content[i + 3] + PixelKernels.Div255(shade[i + 3] * inverse));
        }
    }
}

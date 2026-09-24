using PeachPDF.Html.Adapters.Entities;
using System;
using System.Collections.Generic;

namespace PeachPDF.Raster;

/// <summary>
/// Applies a <see cref="ColorMatrix"/> to every pixel: the mechanism behind CSS <c>grayscale()</c>,
/// <c>sepia()</c>, <c>saturate()</c>, <c>hue-rotate()</c> and friends, including the cross-channel ones a PDF
/// transfer function cannot express.
/// </summary>
internal static class ColorMatrixFilter
{
    /// <summary>Returns a new bitmap with <paramref name="matrix"/> applied to <paramref name="source"/>.</summary>
    public static Bitmap Apply(Bitmap source, in ColorMatrix matrix)
    {
        var result = new byte[source.Width * source.Height * 4];
        Apply(source.Pixels.AsSpan(0, result.Length), result, matrix);
        return new Bitmap(source.Width, source.Height, result);
    }

    /// <summary>Applies <paramref name="matrix"/> to <paramref name="surface"/> in place.</summary>
    public static void ApplyInPlace(RasterSurface surface, in ColorMatrix matrix)
    {
        var pixels = surface.Pixels;
        Apply(pixels, pixels, matrix);
    }

    /// <summary>
    /// Applies <paramref name="matrices"/> to <paramref name="surface"/> in place, one after another, in a single pass over the
    /// pixels. Each stage's result is clamped to the 0-1 range before the next runs, as Filter Effects 1 evaluates a filter list
    /// function by function: <c>brightness(2) sepia(1)</c> is not the composition of the two matrices, because the brightened
    /// channels clip at 1 before sepia mixes them.
    /// </summary>
    public static void ApplyInPlace(RasterSurface surface, IReadOnlyList<ColorMatrix> matrices)
    {
        if (matrices.Count == 0)
            return;

        var pixels = surface.Pixels;
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            int a8 = pixels[i + 3];
            float r, g, b, a = a8 / 255f;
            if (a8 == 0)
            {
                r = g = b = 0;
            }
            else
            {
                r = Math.Min(1f, pixels[i] / 255f / a);
                g = Math.Min(1f, pixels[i + 1] / 255f / a);
                b = Math.Min(1f, pixels[i + 2] / 255f / a);
            }

            foreach (var matrix in matrices)
            {
                var m = matrix.Linear;
                var o = matrix.Offset;
                var nr = r * m.M11 + g * m.M21 + b * m.M31 + a * m.M41 + o.X;
                var ng = r * m.M12 + g * m.M22 + b * m.M32 + a * m.M42 + o.Y;
                var nb = r * m.M13 + g * m.M23 + b * m.M33 + a * m.M43 + o.Z;
                var na = r * m.M14 + g * m.M24 + b * m.M34 + a * m.M44 + o.W;
                r = Math.Clamp(nr, 0f, 1f);
                g = Math.Clamp(ng, 0f, 1f);
                b = Math.Clamp(nb, 0f, 1f);
                a = Math.Clamp(na, 0f, 1f);
            }

            pixels[i] = (byte)MathF.Round(r * a * 255f);
            pixels[i + 1] = (byte)MathF.Round(g * a * 255f);
            pixels[i + 2] = (byte)MathF.Round(b * a * 255f);
            pixels[i + 3] = (byte)MathF.Round(a * 255f);
        }
    }

    /// <summary>
    /// Transforms <paramref name="source"/> into <paramref name="destination"/> (the same span is allowed). The matrix
    /// works on straight (unpremultiplied) 0-1 colour, as Filter Effects 1 defines it, so pixels are unpremultiplied,
    /// transformed, clamped and premultiplied again.
    /// </summary>
    /// <remarks>
    /// Written with plain multiplies and adds, never <c>Vector4.Transform</c>: that may fuse operations on some
    /// CPUs, and the raster backend's output must not depend on the CPU it runs on.
    /// </remarks>
    public static void Apply(ReadOnlySpan<byte> source, Span<byte> destination, in ColorMatrix matrix)
    {
        var m = matrix.Linear;
        var o = matrix.Offset;

        for (var i = 0; i + 3 < source.Length; i += 4)
        {
            int a8 = source[i + 3];
            float r, g, b, a = a8 / 255f;
            if (a8 == 0)
            {
                r = g = b = 0;
            }
            else
            {
                r = Math.Min(1f, source[i] / 255f / a);
                g = Math.Min(1f, source[i + 1] / 255f / a);
                b = Math.Min(1f, source[i + 2] / 255f / a);
            }

            var nr = r * m.M11 + g * m.M21 + b * m.M31 + a * m.M41 + o.X;
            var ng = r * m.M12 + g * m.M22 + b * m.M32 + a * m.M42 + o.Y;
            var nb = r * m.M13 + g * m.M23 + b * m.M33 + a * m.M43 + o.Z;
            var na = r * m.M14 + g * m.M24 + b * m.M34 + a * m.M44 + o.W;

            na = Math.Clamp(na, 0f, 1f);
            nr = Math.Clamp(nr, 0f, 1f) * na;
            ng = Math.Clamp(ng, 0f, 1f) * na;
            nb = Math.Clamp(nb, 0f, 1f) * na;

            destination[i] = (byte)MathF.Round(nr * 255f);
            destination[i + 1] = (byte)MathF.Round(ng * 255f);
            destination[i + 2] = (byte)MathF.Round(nb * 255f);
            destination[i + 3] = (byte)MathF.Round(na * 255f);
        }
    }
}

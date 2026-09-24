using System;

namespace PeachPDF.Raster;

/// <summary>
/// Draws one surface as the picture a <see cref="Homography"/> makes of it: the raster half of a CSS <c>perspective</c> or 3D transform
/// (a PDF <c>cm</c> is affine and cannot). Every destination pixel is mapped back through the inverse to the source and sampled there.
/// </summary>
internal static class Warp
{
    /// <summary>The most samples taken along one axis of a pixel when the source is being shrunk (so up to 16 per pixel).</summary>
    private const int MaxSamplesPerAxis = 4;

    /// <summary>
    /// Fills <paramref name="destination"/> with <paramref name="source"/> mapped by <paramref name="sourceToDestination"/>. Both maps work in
    /// layout units, the units the surfaces record their placement in; a destination pixel whose source lies outside the source surface, or
    /// behind the viewer, stays transparent. Edges are soft because the source is sampled bilinearly against transparency beyond its border.
    /// </summary>
    public static void Apply(RasterSurface source, RasterSurface destination, in Homography sourceToDestination)
    {
        if (sourceToDestination.Invert() is not { } inverse)
            return;

        var src = source.Pixels;
        var dst = destination.Pixels;
        var sw = source.Width;
        var sh = source.Height;
        var samples = new int[4];

        for (var y = 0; y < destination.Height; y++)
        {
            for (var x = 0; x < destination.Width; x++)
            {
                var dx = (destination.GridX + x + 0.5) / destination.PixelsPerUnitX;
                var dy = (destination.GridY + y + 0.5) / destination.PixelsPerUnitY;
                var (u, v, w) = inverse.ApplyHomogeneous(dx, dy);
                if (!(w > 1e-9))
                    continue;

                var sxc = u / w * source.PixelsPerUnitX - source.GridX;
                var syc = v / w * source.PixelsPerUnitY - source.GridY;

                // How many source pixels one destination pixel spans here: enough samples to average them, so a receding plane
                // (which packs many source pixels into one) does not alias.
                var axis = SamplesPerAxis(inverse, destination, source, x, y, sxc, syc);
                Array.Clear(samples);
                var taken = 0;
                for (var j = 0; j < axis; j++)
                {
                    for (var i = 0; i < axis; i++)
                    {
                        var px = destination.GridX + x + (i + 0.5) / axis;
                        var py = destination.GridY + y + (j + 0.5) / axis;
                        var (su, sv, sw2) = inverse.ApplyHomogeneous(px / destination.PixelsPerUnitX, py / destination.PixelsPerUnitY);
                        if (!(sw2 > 1e-9))
                            continue;

                        Bilinear(src, sw, sh, su / sw2 * source.PixelsPerUnitX - source.GridX, sv / sw2 * source.PixelsPerUnitY - source.GridY, samples);
                        taken++;
                    }
                }

                if (taken == 0)
                    continue;

                var o = (y * destination.Width + x) * 4;
                var divisor = axis * axis;
                for (var c = 0; c < 4; c++)
                    dst[o + c] = (byte)Math.Clamp((samples[c] + divisor / 2) / divisor, 0, 255);
            }
        }
    }

    /// <summary>Adds the bilinear sample of the premultiplied source at pixel-space position (<paramref name="x"/>, <paramref name="y"/>) (pixel centres at +0.5) to <paramref name="sum"/>, against transparency outside.</summary>
    private static void Bilinear(ReadOnlySpan<byte> pixels, int width, int height, double x, double y, int[] sum)
    {
        var fx = x - 0.5;
        var fy = y - 0.5;
        var x0 = (int)Math.Floor(fx);
        var y0 = (int)Math.Floor(fy);
        var tx = (int)Math.Round((fx - x0) * 256);
        var ty = (int)Math.Round((fy - y0) * 256);

        for (var c = 0; c < 4; c++)
        {
            var v00 = Texel(pixels, width, height, x0, y0, c);
            var v10 = Texel(pixels, width, height, x0 + 1, y0, c);
            var v01 = Texel(pixels, width, height, x0, y0 + 1, c);
            var v11 = Texel(pixels, width, height, x0 + 1, y0 + 1, c);
            var top = v00 * (256 - tx) + v10 * tx;
            var bottom = v01 * (256 - tx) + v11 * tx;
            sum[c] += (top * (256 - ty) + bottom * ty + 32768) >> 16;
        }
    }

    private static int Texel(ReadOnlySpan<byte> pixels, int width, int height, int x, int y, int channel) =>
        (uint)x < (uint)width && (uint)y < (uint)height ? pixels[(y * width + x) * 4 + channel] : 0;

    private static int SamplesPerAxis(Homography inverse, RasterSurface destination, RasterSurface source, int x, int y, double sx, double sy)
    {
        (double, double)? Sample(int dx, int dy)
        {
            var (u, v, w) = inverse.ApplyHomogeneous(
                (destination.GridX + x + 0.5 + dx) / destination.PixelsPerUnitX,
                (destination.GridY + y + 0.5 + dy) / destination.PixelsPerUnitY);
            return w > 1e-9 ? (u / w * source.PixelsPerUnitX - source.GridX, v / w * source.PixelsPerUnitY - source.GridY) : null;
        }

        if (Sample(1, 0) is not { } right || Sample(0, 1) is not { } below)
            return MaxSamplesPerAxis;

        var stepX = Math.Sqrt((right.Item1 - sx) * (right.Item1 - sx) + (right.Item2 - sy) * (right.Item2 - sy));
        var stepY = Math.Sqrt((below.Item1 - sx) * (below.Item1 - sx) + (below.Item2 - sy) * (below.Item2 - sy));
        var scale = Math.Max(stepX, stepY);
        return Math.Clamp((int)Math.Ceiling(scale), 1, MaxSamplesPerAxis);
    }
}

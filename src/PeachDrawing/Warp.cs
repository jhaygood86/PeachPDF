using PeachDrawing.Core;
using System;
using System.Buffers;
using System.Runtime.Intrinsics;

namespace PeachDrawing;

/// <summary>
/// Draws one surface as the picture a <see cref="Homography"/> makes of it: the raster half of a CSS <c>perspective</c> or 3D transform
/// (a PDF <c>cm</c> is affine and cannot). Every destination pixel is mapped back through the inverse to the source and sampled there.
/// </summary>
public static class Warp
{
    /// <summary>The most samples taken along one axis of a pixel when the source is being shrunk (so up to 16 per pixel).</summary>
    private const int MaxSamplesPerAxis = 4;

    /// <summary>Rows at most this wide are worked on in stack memory; wider ones rent a buffer for the call.</summary>
    private const int StackRowPixels = 1024;

    /// <summary>How far behind what a depth buffer holds a pixel may be and still be drawn (layout units), so a plane painted later wins over a coplanar one.</summary>
    private const float DepthEpsilon = 0.01f;

    /// <summary>
    /// Fills <paramref name="destination"/> with <paramref name="source"/> mapped by <paramref name="sourceToDestination"/>. Both maps work in
    /// layout units, the units the surfaces record their placement in; a destination pixel whose source lies outside the source surface, or
    /// behind the viewer, stays transparent. Edges are soft because the source is sampled bilinearly against transparency beyond its border.
    /// </summary>
    public static void Apply(RasterSurface source, RasterSurface destination, in Homography sourceToDestination)
    {
        if (sourceToDestination.Invert() is not { } inverse)
            return;

        var width = destination.Width;
        var pixels = destination.Pixels;
        for (var y = 0; y < destination.Height; y++)
            SampleRow(source, destination, inverse, y, 0, width, pixels.Slice(y * width * 4, width * 4), default);
    }

    /// <summary>
    /// Draws <paramref name="source"/> mapped by <paramref name="sourceToDestination"/> <em>over</em> what <paramref name="destination"/> already holds,
    /// where it is not behind what <paramref name="depth"/> already holds: one plane of a 3D rendering context. <paramref name="plane"/> is the
    /// depth of the same map (both are made from one 4x4 matrix). A fully opaque pixel records its depth; a translucent one does not, so it
    /// is composited over whatever was drawn before it without hiding what is behind it from a plane painted after.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the depth is affine in screen coordinates.</b> A destination point maps back through the inverse to <c>(u, v, w) = (u', v', 1) / W'</c>,
    /// where <c>W'</c> is the divisor the forward map divided by. The plane's depth there is <c>z' / W'</c>, and <c>z'</c> is affine in
    /// <c>(u', v')</c>, so it is <c>Za * u + Zb * v + Zc * w</c> of the inverse's raw output: an affine function of the screen position, whose
    /// per-row slope is one subtraction. That is what lets the depth test run a row at a time in the depth-test row kernel.
    /// </para>
    /// <para>Works in place row by row: one stack (or one rented) buffer per call, nothing else is allocated.</para>
    /// </remarks>
    public static void Composite(RasterSurface source, RasterSurface destination, in Homography sourceToDestination, in DepthPlane plane, DepthBuffer depth)
    {
        if (sourceToDestination.Invert() is not { } inverse)
            return;

        // Only the destination pixels the source can reach (its projected bounds, with a pixel of margin for the soft edge).
        var extent = source.LayoutRect;
        if (!sourceToDestination.TryProjectRectangleBounds(extent.Left, extent.Top, extent.Right, extent.Bottom, out var minX, out var minY, out var maxX, out var maxY) ||
            !double.IsFinite(minX + minY + maxX + maxY))
            return;

        var pitchX = destination.PixelsPerUnitX;
        var pitchY = destination.PixelsPerUnitY;
        var xStart = (int)Math.Clamp(Math.Floor(minX * pitchX) - destination.GridX - 1, 0, destination.Width);
        var xEnd = (int)Math.Clamp(Math.Ceiling(maxX * pitchX) - destination.GridX + 1, 0, destination.Width);
        var yStart = (int)Math.Clamp(Math.Floor(minY * pitchY) - destination.GridY - 1, 0, destination.Height);
        var yEnd = (int)Math.Clamp(Math.Ceiling(maxY * pitchY) - destination.GridY + 1, 0, destination.Height);
        var count = xEnd - xStart;
        if (count <= 0 || yEnd <= yStart)
            return;

        var width = destination.Width;
        byte[]? rented = null;
        Span<byte> scratch = width <= StackRowPixels ? stackalloc byte[StackRowPixels * 5] : (rented = ArrayPool<byte>.Shared.Rent(width * 5));
        try
        {
            var samples = scratch[..(width * 4)];
            var coverage = scratch.Slice(width * 4, width);

            for (var y = yStart; y < yEnd; y++)
            {
                SampleRow(source, destination, inverse, y, xStart, xEnd, samples, coverage);

                // The depth along this row: the raw inverse output at the first pixel, and one pixel further for the slope.
                var dy = (destination.GridY + y + 0.5) / pitchY;
                var (u0, v0, w0) = inverse.ApplyHomogeneous((destination.GridX + xStart + 0.5) / pitchX, dy);
                var (u1, v1, w1) = inverse.ApplyHomogeneous((destination.GridX + xStart + 1.5) / pitchX, dy);
                var z0 = plane.Numerator(u0, v0, w0);
                var zs = plane.Numerator(u1, v1, w1) - z0;

                var rowSamples = samples.Slice(xStart * 4, count * 4);
                var rowCoverage = coverage.Slice(xStart, count);
                PixelKernels.DepthTestRow(depth.Row(y).Slice(xStart, count), rowCoverage, rowSamples, (float)z0, (float)zs, DepthEpsilon);
                PixelKernels.BlendSpan(destination.Row(y).Slice(xStart * 4, count * 4), rowSamples, rowCoverage);
            }
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Samples destination pixels <paramref name="xStart"/> to <paramref name="xEnd"/> of row <paramref name="y"/> into
    /// <paramref name="pixels"/> (indexed by the destination x, four bytes each). A pixel whose source is behind the viewer is left as it
    /// was. When <paramref name="valid"/> is not empty it receives 255 for a sampled pixel and 0 for one that was not.
    /// </summary>
    private static void SampleRow(RasterSurface source, RasterSurface destination, in Homography inverse, int y, int xStart, int xEnd, Span<byte> pixels, Span<byte> valid)
    {
        var src = source.Pixels;
        var sw = source.Width;
        var sh = source.Height;
        var srcPitchX = source.PixelsPerUnitX;
        var srcPitchY = source.PixelsPerUnitY;
        var srcGridX = source.GridX;
        var srcGridY = source.GridY;

        // The inverse's raw output (u, v, w) is affine in the destination position, so along a row it moves by the same step every pixel, and from
        // one row to the next by another: it is computed once for the first pixel of the row, then stepped, instead of being recomputed (with the
        // two neighbours the sampling rate needs) for every pixel.
        var unitX = 1.0 / destination.PixelsPerUnitX;
        var unitY = 1.0 / destination.PixelsPerUnitY;
        var (u, v, w) = inverse.ApplyHomogeneous((destination.GridX + xStart + 0.5) * unitX, (destination.GridY + y + 0.5) * unitY);
        double du = inverse.M11 * unitX, dv = inverse.M21 * unitX, dw = inverse.M31 * unitX; // one pixel to the right
        double eu = inverse.M12 * unitY, ev = inverse.M22 * unitY, ew = inverse.M32 * unitY; // one pixel down

        // No perspective: the divisor is the same everywhere, so is how many source pixels a destination pixel spans.
        var constantAxis = inverse.IsAffine && w > 1e-9 ? SamplesPerAxis(u / w, v / w, u, v, du, dv, dw, eu, ev, ew, srcPitchX, srcPitchY, w) : 0;

        for (var x = xStart; x < xEnd; x++, u += du, v += dv, w += dw)
        {
            if (!(w > 1e-9))
            {
                if (!valid.IsEmpty)
                    valid[x] = 0;
                continue;
            }

            var reciprocal = 1.0 / w;
            var ur = u * reciprocal;
            var vr = v * reciprocal;
            var sxc = ur * srcPitchX - srcGridX;
            var syc = vr * srcPitchY - srcGridY;

            // How many source pixels one destination pixel spans here: enough samples to average them, so a receding plane
            // (which packs many source pixels into one) does not alias.
            var axis = constantAxis != 0 ? constantAxis : SamplesPerAxis(ur, vr, u, v, du, dv, dw, eu, ev, ew, srcPitchX, srcPitchY, w);

            // One sample per pixel is the one at the pixel's centre, which is what was just computed.
            if (axis == 1)
            {
                PixelKernels.StoreAveraged(Bilinear(src, sw, sh, sxc, syc), 1, pixels.Slice(x * 4, 4));
                if (!valid.IsEmpty)
                    valid[x] = 255;
                continue;
            }

            // The sub-samples sit at fixed offsets from the pixel centre, and (u, v, w) is affine in the position, so each is the centre's plus a constant.
            var offsets = SubSampleOffsets[axis];
            var sum = Vector128<int>.Zero;
            var taken = 0;
            for (var j = 0; j < axis; j++)
            {
                for (var i = 0; i < axis; i++)
                {
                    var ox = offsets[i];
                    var oy = offsets[j];
                    var sw2 = w + ox * dw + oy * ew;
                    if (!(sw2 > 1e-9))
                        continue;

                    var r2 = 1.0 / sw2;
                    sum += Bilinear(src, sw, sh, (u + ox * du + oy * eu) * r2 * srcPitchX - srcGridX, (v + ox * dv + oy * ev) * r2 * srcPitchY - srcGridY);
                    taken++;
                }
            }

            if (taken == 0)
            {
                if (!valid.IsEmpty)
                    valid[x] = 0;
                continue;
            }

            PixelKernels.StoreAveraged(sum, axis * axis, pixels.Slice(x * 4, 4));
            if (!valid.IsEmpty)
                valid[x] = 255;
        }
    }

    /// <summary>For each number of samples along an axis, where they sit relative to the pixel's centre, in pixels: <c>(i + 0.5) / axis - 0.5</c>.</summary>
    private static readonly double[][] SubSampleOffsets = BuildSubSampleOffsets();

    private static double[][] BuildSubSampleOffsets()
    {
        var table = new double[MaxSamplesPerAxis + 1][];
        for (var axis = 1; axis <= MaxSamplesPerAxis; axis++)
        {
            table[axis] = new double[axis];
            for (var i = 0; i < axis; i++)
                table[axis][i] = (i + 0.5) / axis - 0.5;
        }

        return table;
    }

    /// <summary>The bilinear sample of the premultiplied source at pixel-space position (<paramref name="x"/>, <paramref name="y"/>) (pixel centres at +0.5), against transparency outside.</summary>
    private static Vector128<int> Bilinear(ReadOnlySpan<byte> pixels, int width, int height, double x, double y)
    {
        var fx = x - 0.5;
        var fy = y - 0.5;

        // Far outside any image (or not a number): every texel is transparent, and the casts below would not be defined.
        if (!(Math.Abs(fx) < 1e6 && Math.Abs(fy) < 1e6))
            return Vector128<int>.Zero;

        var x0 = (int)Math.Floor(fx);
        var y0 = (int)Math.Floor(fy);
        var tx = (int)Math.Round((fx - x0) * 256);
        var ty = (int)Math.Round((fy - y0) * 256);
        return PixelKernels.BilinearTexels(pixels, width, height, x0, y0, tx, ty);
    }

    /// <summary>
    /// How many samples along each axis a destination pixel needs: the number of source pixels it spans, rounded up, at most <see cref="MaxSamplesPerAxis"/>.
    /// The raw output (<paramref name="u"/>, <paramref name="v"/>, <paramref name="w"/>) is at the pixel's centre, where the source position is
    /// (<paramref name="ur"/>, <paramref name="vr"/>) = (<c>u / w</c>, <c>v / w</c>); <paramref name="du"/> and the like are its steps to the next pixel across
    /// and down. Compared as squared lengths against 1, 4, 9 and 16, which is the same as rounding the length up without taking a root.
    /// </summary>
    private static int SamplesPerAxis(double ur, double vr, double u, double v, double du, double dv, double dw, double eu, double ev, double ew, double srcPitchX, double srcPitchY, double w)
    {
        var nextW = w + dw;
        var belowW = w + ew;
        if (!(nextW > 1e-9) || !(belowW > 1e-9))
            return MaxSamplesPerAxis;

        var nextReciprocal = 1.0 / nextW;
        var belowReciprocal = 1.0 / belowW;
        var rx = ((u + du) * nextReciprocal - ur) * srcPitchX;
        var ry = ((v + dv) * nextReciprocal - vr) * srcPitchY;
        var bx = ((u + eu) * belowReciprocal - ur) * srcPitchX;
        var by = ((v + ev) * belowReciprocal - vr) * srcPitchY;

        var squared = Math.Max(rx * rx + ry * ry, bx * bx + by * by);
        return squared <= 1 ? 1 : squared <= 4 ? 2 : squared <= 9 ? 3 : 4;
    }
}

/// <summary>
/// How deep a plane is (its z over its divisor, larger being nearer the viewer) at each point of it, from the same 4x4 matrix as its
/// <see cref="Homography"/>: the z of the plane z = 0 after the matrix, and the divisor the map shares.
/// </summary>
public readonly struct DepthPlane(double za, double zb, double zc, double wa, double wb, double wc)
{
    private readonly double _za = za, _zb = zb, _zc = zc, _wa = wa, _wb = wb, _wc = wc;

    /// <summary>The depth model of what <paramref name="m"/> (System.Numerics, row-vector convention) does to the plane z = 0.</summary>
    public static DepthPlane FromMatrix4(in System.Numerics.Matrix4x4 m) => new(m.M13, m.M23, m.M43, m.M14, m.M24, m.M44);

    /// <summary>The plane's depth at its own point (<paramref name="u"/>, <paramref name="v"/>), or NaN where the divisor is not positive (behind the viewer).</summary>
    public double At(double u, double v)
    {
        var divisor = _wa * u + _wb * v + _wc;
        return divisor > 1e-9 ? (_za * u + _zb * v + _zc) / divisor : double.NaN;
    }

    /// <summary>The depth at the screen point whose raw inverse-homography output is (<paramref name="u"/>, <paramref name="v"/>, <paramref name="w"/>): affine in the screen point.</summary>
    public double Numerator(double u, double v, double w) => _za * u + _zb * v + _zc * w;
}

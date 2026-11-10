using PeachDrawing.Core;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace PeachDrawing.Filters;

public sealed partial class Turbulence
{
    /// <summary>
    /// <see cref="RenderReference"/> with the work that does not depend on the channel done once: the stitching set-up for the whole surface,
    /// and, for each pixel and octave, the lattice lookups the four channels share. The four channels' gradient arithmetic then runs in one
    /// 256-bit vector - the same multiplies, subtractions and additions, in the same order, on the same doubles, one lane per channel - so the
    /// values, and the bytes written, are the reference's.
    /// </summary>
    private void RenderVector(RasterSurface surface, double pixelsPerUserX, double pixelsPerUserY, double baseFrequencyX, double baseFrequencyY,
        int octaves, bool fractalSum, bool stitching, double tileX, double tileY, double tileWidth, double tileHeight)
    {
        // What Sample works out for every call: the frequencies snapped to the tile, and where the lattice wraps.
        var stitchStart = new Stitch();
        if (stitching)
        {
            if (baseFrequencyX != 0.0)
            {
                var lo = Math.Floor(tileWidth * baseFrequencyX) / tileWidth;
                var hi = Math.Ceiling(tileWidth * baseFrequencyX) / tileWidth;
                baseFrequencyX = baseFrequencyX / lo < hi / baseFrequencyX ? lo : hi;
            }

            if (baseFrequencyY != 0.0)
            {
                var lo = Math.Floor(tileHeight * baseFrequencyY) / tileHeight;
                var hi = Math.Ceiling(tileHeight * baseFrequencyY) / tileHeight;
                baseFrequencyY = baseFrequencyY / lo < hi / baseFrequencyY ? lo : hi;
            }

            stitchStart.Width = (int)(tileWidth * baseFrequencyX + 0.5);
            stitchStart.WrapX = (int)(tileX * baseFrequencyX + PerlinN + stitchStart.Width);
            stitchStart.Height = (int)(tileHeight * baseFrequencyY + 0.5);
            stitchStart.WrapY = (int)(tileY * baseFrequencyY + PerlinN + stitchStart.Height);
        }

        var pixels = surface.Pixels;
        var lattice = _lattice;
        ref var gx = ref MemoryMarshal.GetArrayDataReference(_gradientX);
        ref var gy = ref MemoryMarshal.GetArrayDataReference(_gradientY);
        var one = Vector256.Create(1.0);

        for (var y = 0; y < surface.Height; y++)
        {
            var uy = (surface.GridY + y + 0.5) / pixelsPerUserY;
            for (var x = 0; x < surface.Width; x++)
            {
                var ux = (surface.GridX + x + 0.5) / pixelsPerUserX;

                var stitch = stitchStart;
                var sum = Vector256<double>.Zero;
                var vx = ux * baseFrequencyX;
                var vy = uy * baseFrequencyY;
                var ratio = 1.0;
                for (var octave = 0; octave < octaves; octave++)
                {
                    // The lattice part of Noise2, which every channel shares.
                    var t = vx + PerlinN;
                    var bx0 = (int)t;
                    var bx1 = bx0 + 1;
                    var rx0 = t - (int)t;
                    var rx1 = rx0 - 1.0;
                    t = vy + PerlinN;
                    var by0 = (int)t;
                    var by1 = by0 + 1;
                    var ry0 = t - (int)t;
                    var ry1 = ry0 - 1.0;

                    if (stitching)
                    {
                        if (bx0 >= stitch.WrapX) bx0 -= stitch.Width;
                        if (bx1 >= stitch.WrapX) bx1 -= stitch.Width;
                        if (by0 >= stitch.WrapY) by0 -= stitch.Height;
                        if (by1 >= stitch.WrapY) by1 -= stitch.Height;
                    }

                    bx0 &= BM;
                    bx1 &= BM;
                    by0 &= BM;
                    by1 &= BM;

                    var i = lattice[bx0];
                    var j = lattice[bx1];
                    var b00 = lattice[i + by0] * 4;
                    var b10 = lattice[j + by0] * 4;
                    var b01 = lattice[i + by1] * 4;
                    var b11 = lattice[j + by1] * 4;
                    var sx = Vector256.Create(SCurve(rx0));
                    var sy = Vector256.Create(SCurve(ry0));

                    var rx0v = Vector256.Create(rx0);
                    var rx1v = Vector256.Create(rx1);
                    var ry0v = Vector256.Create(ry0);
                    var ry1v = Vector256.Create(ry1);

                    // The four channels' Noise2: u and v along the lower edge, then along the upper, each pair lerped by sx, those two by sy.
                    var u = rx0v * Vector256.LoadUnsafe(ref gx, (nuint)b00) + ry0v * Vector256.LoadUnsafe(ref gy, (nuint)b00);
                    var v = rx1v * Vector256.LoadUnsafe(ref gx, (nuint)b10) + ry0v * Vector256.LoadUnsafe(ref gy, (nuint)b10);
                    var a = u + sx * (v - u);
                    u = rx0v * Vector256.LoadUnsafe(ref gx, (nuint)b01) + ry1v * Vector256.LoadUnsafe(ref gy, (nuint)b01);
                    v = rx1v * Vector256.LoadUnsafe(ref gx, (nuint)b11) + ry1v * Vector256.LoadUnsafe(ref gy, (nuint)b11);
                    var b = u + sx * (v - u);
                    var n = a + sy * (b - a);

                    sum += (fractalSum ? n : Vector256.Abs(n)) / Vector256.Create(ratio);
                    vx *= 2;
                    vy *= 2;
                    ratio *= 2;
                    if (stitching)
                    {
                        stitch.Width *= 2;
                        stitch.WrapX = 2 * stitch.WrapX - PerlinN;
                        stitch.Height *= 2;
                        stitch.WrapY = 2 * stitch.WrapY - PerlinN;
                    }
                }

                // The rest is the reference's own scalar code on the four sums.
                var red = Math.Clamp(fractalSum ? (sum.GetElement(0) + 1.0) / 2.0 : sum.GetElement(0), 0.0, 1.0);
                var green = Math.Clamp(fractalSum ? (sum.GetElement(1) + 1.0) / 2.0 : sum.GetElement(1), 0.0, 1.0);
                var blue = Math.Clamp(fractalSum ? (sum.GetElement(2) + 1.0) / 2.0 : sum.GetElement(2), 0.0, 1.0);
                var alpha = Math.Clamp(fractalSum ? (sum.GetElement(3) + 1.0) / 2.0 : sum.GetElement(3), 0.0, 1.0);

                var o = (y * surface.Width + x) * 4;
                pixels[o] = (byte)Math.Round(red * alpha * 255.0);
                pixels[o + 1] = (byte)Math.Round(green * alpha * 255.0);
                pixels[o + 2] = (byte)Math.Round(blue * alpha * 255.0);
                pixels[o + 3] = (byte)Math.Round(alpha * 255.0);
            }
        }
    }
}

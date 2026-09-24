using System;

namespace PeachPDF.Raster.Filters;

/// <summary>
/// Perlin noise exactly as the SVG specification's reference implementation defines it (Filter Effects 1, <c>feTurbulence</c>),
/// so a given seed, frequency and octave count yields the same picture every conforming renderer draws.
/// </summary>
internal sealed class Turbulence
{
    private const int BSize = 0x100;
    private const int BM = 0xff;
    private const int PerlinN = 0x1000;
    private const int RandM = 2147483647;
    private const int RandA = 16807;
    private const int RandQ = 127773;
    private const int RandR = 2836;

    private readonly int[] _lattice = new int[BSize + BSize + 2];
    private readonly double[,,] _gradient = new double[4, BSize + BSize + 2, 2];

    private struct Stitch
    {
        public int Width, Height, WrapX, WrapY;
    }

    public Turbulence(long seed)
    {
        Init(seed);
    }

    private static long SetupSeed(long seed)
    {
        if (seed <= 0)
            seed = -(seed % (RandM - 1)) + 1;

        if (seed > RandM - 1)
            seed = RandM - 1;

        return seed;
    }

    private static long Random(long seed)
    {
        var result = RandA * (seed % RandQ) - RandR * (seed / RandQ);
        if (result <= 0)
            result += RandM;

        return result;
    }

    private void Init(long seed)
    {
        seed = SetupSeed(seed);
        int i, j, k;
        for (k = 0; k < 4; k++)
        {
            for (i = 0; i < BSize; i++)
            {
                _lattice[i] = i;
                for (j = 0; j < 2; j++)
                {
                    seed = Random(seed);
                    _gradient[k, i, j] = (double)((seed % (BSize + BSize)) - BSize) / BSize;
                }

                var s = Math.Sqrt(_gradient[k, i, 0] * _gradient[k, i, 0] + _gradient[k, i, 1] * _gradient[k, i, 1]);
                if (s > 0)
                {
                    _gradient[k, i, 0] /= s;
                    _gradient[k, i, 1] /= s;
                }
            }
        }

        for (i = BSize - 1; i > 0; i--)
        {
            k = _lattice[i];
            seed = Random(seed);
            j = (int)(seed % BSize);
            _lattice[i] = _lattice[j];
            _lattice[j] = k;
        }

        for (i = 0; i < BSize + 2; i++)
        {
            _lattice[BSize + i] = _lattice[i];
            for (k = 0; k < 4; k++)
            {
                for (j = 0; j < 2; j++)
                    _gradient[k, BSize + i, j] = _gradient[k, i, j];
            }
        }
    }

    private static double SCurve(double t) => t * t * (3.0 - 2.0 * t);

    private static double Lerp(double t, double a, double b) => a + t * (b - a);

    private double Noise2(int channel, double vx, double vy, bool stitching, in Stitch stitch)
    {
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

        var i = _lattice[bx0];
        var j = _lattice[bx1];
        var b00 = _lattice[i + by0];
        var b10 = _lattice[j + by0];
        var b01 = _lattice[i + by1];
        var b11 = _lattice[j + by1];
        var sx = SCurve(rx0);
        var sy = SCurve(ry0);

        var u = rx0 * _gradient[channel, b00, 0] + ry0 * _gradient[channel, b00, 1];
        var v = rx1 * _gradient[channel, b10, 0] + ry0 * _gradient[channel, b10, 1];
        var a = Lerp(sx, u, v);
        u = rx0 * _gradient[channel, b01, 0] + ry1 * _gradient[channel, b01, 1];
        v = rx1 * _gradient[channel, b11, 0] + ry1 * _gradient[channel, b11, 1];
        var b = Lerp(sx, u, v);
        return Lerp(sy, a, b);
    }

    /// <summary>The noise value of one colour channel at a point of the filter's user space.</summary>
    public double Sample(int channel, double x, double y, double baseFrequencyX, double baseFrequencyY, int octaves,
        bool fractalSum, bool stitching, double tileX, double tileY, double tileWidth, double tileHeight)
    {
        var stitch = new Stitch();
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

            stitch.Width = (int)(tileWidth * baseFrequencyX + 0.5);
            stitch.WrapX = (int)(tileX * baseFrequencyX + PerlinN + stitch.Width);
            stitch.Height = (int)(tileHeight * baseFrequencyY + 0.5);
            stitch.WrapY = (int)(tileY * baseFrequencyY + PerlinN + stitch.Height);
        }

        var sum = 0.0;
        var vx = x * baseFrequencyX;
        var vy = y * baseFrequencyY;
        var ratio = 1.0;
        for (var octave = 0; octave < octaves; octave++)
        {
            var n = Noise2(channel, vx, vy, stitching, stitch);
            sum += (fractalSum ? n : Math.Abs(n)) / ratio;
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

        return sum;
    }

    /// <summary>
    /// Fills <paramref name="surface"/> with the noise, sampling each pixel's centre in user space (<paramref name="pixelsPerUserX"/>
    /// pixels per user unit) and premultiplying the result, which the reference algorithm defines as straight colour.
    /// </summary>
    public void Render(RasterSurface surface, double pixelsPerUserX, double pixelsPerUserY, double baseFrequencyX, double baseFrequencyY,
        int octaves, bool fractalSum, bool stitching, double tileX, double tileY, double tileWidth, double tileHeight)
    {
        var pixels = surface.Pixels;
        var channels = new double[4];
        for (var y = 0; y < surface.Height; y++)
        {
            var uy = (surface.GridY + y + 0.5) / pixelsPerUserY;
            for (var x = 0; x < surface.Width; x++)
            {
                var ux = (surface.GridX + x + 0.5) / pixelsPerUserX;
                for (var c = 0; c < 4; c++)
                {
                    var n = Sample(c, ux, uy, baseFrequencyX, baseFrequencyY, octaves, fractalSum, stitching, tileX, tileY, tileWidth, tileHeight);
                    channels[c] = Math.Clamp(fractalSum ? (n + 1.0) / 2.0 : n, 0.0, 1.0);
                }

                var o = (y * surface.Width + x) * 4;
                var a = channels[3];
                pixels[o] = (byte)Math.Round(channels[0] * a * 255.0);
                pixels[o + 1] = (byte)Math.Round(channels[1] * a * 255.0);
                pixels[o + 2] = (byte)Math.Round(channels[2] * a * 255.0);
                pixels[o + 3] = (byte)Math.Round(a * 255.0);
            }
        }
    }
}

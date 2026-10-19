using PeachDrawing.Core;
using PeachDrawing;
using System;

namespace PeachPDF.Svg;

/// <summary>
/// The two <c>PeachDrawing.Filters.FilterOps</c> pixel operations that stay PeachPDF-side rather than
/// moving with the rest of that class: both take an SVG filter-primitive descriptor
/// (<see cref="TransferFunction"/>, <see cref="FeConvolveMatrix"/>) straight from SVG parsing as a
/// parameter, which is genuinely SVG-specific - <c>PeachDrawing</c> has no SVG concept at all - unlike
/// every other <c>FilterOps</c> method, which takes only <c>RasterSurface</c>s and plain values and moved
/// with the rest of the raster backend. See <c>PeachDrawing.Filters.FilterOps</c>'s own remarks.
/// </summary>
internal static class SvgFilterOps
{
    /// <summary>Evaluates one transfer function over the 256 possible 8-bit inputs.</summary>
    public static byte[] BuildTransferLut(TransferFunction function)
    {
        var lut = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var c = i / 255.0;
            double v;
            switch (function.Kind)
            {
                case TransferKind.Table:
                {
                    var n = function.Table.Length - 1;
                    var k = Math.Min((int)Math.Floor(c * n), n);
                    v = k == n || n == 0
                        ? function.Table[n]
                        : function.Table[k] + (c - (double)k / n) * n * (function.Table[k + 1] - function.Table[k]);
                    break;
                }

                case TransferKind.Discrete:
                {
                    var n = function.Table.Length;
                    v = function.Table[Math.Min((int)Math.Floor(c * n), n - 1)];
                    break;
                }

                case TransferKind.Linear:
                    v = function.Slope * c + function.Intercept;
                    break;

                case TransferKind.Gamma:
                    v = function.Amplitude * Math.Pow(c, function.Exponent) + function.Offset;
                    break;

                default:
                    v = c;
                    break;
            }

            // Clamped before the integer conversion: an out-of-range double converts differently on different runtimes and CPUs
            // (Pow(0, -1) is infinity), and the output has to be the same everywhere.
            lut[i] = double.IsNaN(v) ? (byte)0 : (byte)Math.Round(Math.Clamp(v * 255.0, 0.0, 255.0));
        }

        return lut;
    }

    public static void ConvolveMatrix(RasterSurface source, RasterSurface destination, FeConvolveMatrix p)
    {
        var w = source.Width;
        var h = source.Height;
        var ps = source.Pixels;

        // Straight colour when alpha is preserved (the colour is convolved and premultiplied by the original alpha), premultiplied otherwise.
        var values = new float[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            float a = ps[i * 4 + 3] / 255f;
            for (var c = 0; c < 3; c++)
            {
                var v = ps[i * 4 + c] / 255f;
                values[i * 4 + c] = p.PreserveAlpha ? (a > 0 ? Math.Min(v / a, 1f) : 0f) : v;
            }

            values[i * 4 + 3] = a;
        }

        var pd = destination.Pixels;
        var sum = new double[4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                Array.Clear(sum);
                for (var i = 0; i < p.OrderY; i++)
                {
                    for (var j = 0; j < p.OrderX; j++)
                    {
                        var sx = x - p.TargetX + j;
                        var sy = y - p.TargetY + i;
                        if (sx < 0 || sx >= w || sy < 0 || sy >= h)
                        {
                            switch (p.EdgeMode)
                            {
                                case FilterEdgeMode.None:
                                    continue;
                                case FilterEdgeMode.Wrap:
                                    sx = PositiveModulo(sx, w);
                                    sy = PositiveModulo(sy, h);
                                    break;
                                default:
                                    sx = Math.Clamp(sx, 0, w - 1);
                                    sy = Math.Clamp(sy, 0, h - 1);
                                    break;
                            }
                        }

                        var k = p.Kernel[p.OrderX - j - 1 + (p.OrderY - i - 1) * p.OrderX];
                        var o = (sy * w + sx) * 4;
                        sum[0] += values[o] * k;
                        sum[1] += values[o + 1] * k;
                        sum[2] += values[o + 2] * k;
                        sum[3] += values[o + 3] * k;
                    }
                }

                var centre = (y * w + x) * 4;
                var alpha = p.PreserveAlpha ? values[centre + 3] : Math.Clamp(sum[3] / p.Divisor + p.Bias, 0.0, 1.0);
                for (var c = 0; c < 3; c++)
                {
                    var v = Math.Clamp(sum[c] / p.Divisor + p.Bias * alpha, 0.0, 1.0);
                    pd[centre + c] = (byte)Math.Round((p.PreserveAlpha ? v * alpha : Math.Min(v, alpha)) * 255.0);
                }

                pd[centre + 3] = (byte)Math.Round(alpha * 255.0);
            }
        }
    }

    private static int PositiveModulo(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}

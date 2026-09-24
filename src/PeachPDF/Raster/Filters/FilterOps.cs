using PeachPDF.Html.Adapters.Entities;
using System;

namespace PeachPDF.Raster.Filters;

/// <summary>
/// The pixel operations behind the SVG filter primitives that PDF cannot express, on premultiplied RGBA8 surfaces of one
/// size. Each takes its inputs and writes a result of the same size; none keeps state. Geometry-dependent parameters
/// (radii, offsets, scales) arrive already converted to pixels.
/// </summary>
internal static partial class FilterOps
{
    private static readonly byte[] ToLinearLut = BuildLut(toLinear: true);
    private static readonly byte[] ToSrgbLut = BuildLut(toLinear: false);

    private static byte[] BuildLut(bool toLinear)
    {
        var lut = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var c = i / 255.0;
            var v = toLinear
                ? c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4)
                : c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
            lut[i] = (byte)Math.Clamp((int)Math.Round(v * 255.0), 0, 255);
        }

        return lut;
    }

    /// <summary>Converts a colour from sRGB to linear light (or back) - straight colour, so the surface is unpremultiplied, mapped and premultiplied again.</summary>
    public static RColor ConvertColor(RColor color, bool toLinear)
    {
        var lut = toLinear ? ToLinearLut : ToSrgbLut;
        return RColor.FromArgb(color.A, lut[color.R], lut[color.G], lut[color.B]);
    }

    /// <summary>Re-encodes <paramref name="surface"/> from sRGB to linear light, or from linear light to sRGB, in place.</summary>
    public static void ConvertColorSpace(RasterSurface surface, bool toLinear)
    {
        var lut = toLinear ? ToLinearLut : ToSrgbLut;
        var p = surface.Pixels;
        for (var i = 0; i + 3 < p.Length; i += 4)
        {
            int a = p[i + 3];
            if (a == 0)
                continue;

            if (a == 255)
            {
                p[i] = lut[p[i]];
                p[i + 1] = lut[p[i + 1]];
                p[i + 2] = lut[p[i + 2]];
                continue;
            }

            for (var c = 0; c < 3; c++)
            {
                var straight = Math.Min(255, (p[i + c] * 255 + a / 2) / a);
                p[i + c] = (byte)((lut[straight] * a + 127) / 255);
            }
        }
    }

    /// <summary>A copy of <paramref name="source"/> on the same grid.</summary>
    public static RasterSurface Clone(RasterSurface source)
    {
        var copy = new RasterSurface(source.Width, source.Height, source.GridX, source.GridY, source.PixelsPerUnitX, source.PixelsPerUnitY);
        source.Pixels.CopyTo(copy.Pixels);
        return copy;
    }

    /// <summary>
    /// A surface <paramref name="padX"/> x <paramref name="padY"/> pixels larger on every side than <paramref name="source"/>, on the same
    /// grid, whose border is <paramref name="source"/> reflected across its edges (edge mode "mirror").
    /// </summary>
    public static RasterSurface MirrorPad(RasterSurface source, int padX, int padY)
    {
        var w = source.Width;
        var h = source.Height;
        var padded = new RasterSurface(w + 2 * padX, h + 2 * padY, source.GridX - padX, source.GridY - padY,
            source.PixelsPerUnitX, source.PixelsPerUnitY);
        var s = source.Pixels;
        var d = padded.Pixels;
        for (var y = 0; y < padded.Height; y++)
        {
            var sy = Reflect(y - padY, h);
            for (var x = 0; x < padded.Width; x++)
            {
                var sx = Reflect(x - padX, w);
                s.Slice((sy * w + sx) * 4, 4).CopyTo(d.Slice((y * padded.Width + x) * 4, 4));
            }
        }

        return padded;
    }

    private static int Reflect(int index, int length)
    {
        if (length == 1)
            return 0;

        var period = 2 * length;
        var i = PositiveModulo(index, period);
        return i < length ? i : period - 1 - i;
    }

    /// <summary>Copies the middle of <paramref name="padded"/> (inside a border of <paramref name="padX"/> x <paramref name="padY"/> pixels) into <paramref name="destination"/>.</summary>
    public static void CopyCentre(RasterSurface padded, RasterSurface destination, int padX, int padY)
    {
        var p = padded.Pixels;
        var d = destination.Pixels;
        for (var y = 0; y < destination.Height; y++)
        {
            p.Slice(((y + padY) * padded.Width + padX) * 4, destination.Width * 4).CopyTo(d.Slice(y * destination.Width * 4, destination.Width * 4));
        }
    }

    /// <summary>A transparent surface on the same grid as <paramref name="like"/>.</summary>
    public static RasterSurface Blank(RasterSurface like) =>
        new(like.Width, like.Height, like.GridX, like.GridY, like.PixelsPerUnitX, like.PixelsPerUnitY);

    /// <summary>Makes every pixel outside <paramref name="keep"/> transparent.</summary>
    public static void ClipTo(RasterSurface surface, IntRect keep)
    {
        var p = surface.Pixels;
        var w = surface.Width;
        for (var y = 0; y < surface.Height; y++)
        {
            var row = p.Slice(y * w * 4, w * 4);
            if (y < keep.Top || y >= keep.Bottom)
            {
                row.Clear();
                continue;
            }

            if (keep.Left > 0)
                row[..(Math.Min(keep.Left, w) * 4)].Clear();

            if (keep.Right < w)
                row[(Math.Max(keep.Right, 0) * 4)..].Clear();
        }
    }

    public static void Fill(RasterSurface surface, RColor color, double opacity)
    {
        var a = Math.Clamp((int)Math.Round(color.A * opacity), 0, 255);
        var r = (byte)((color.R * a + 127) / 255);
        var g = (byte)((color.G * a + 127) / 255);
        var b = (byte)((color.B * a + 127) / 255);
        var p = surface.Pixels;
        for (var i = 0; i + 3 < p.Length; i += 4)
        {
            p[i] = r;
            p[i + 1] = g;
            p[i + 2] = b;
            p[i + 3] = (byte)a;
        }
    }

    /// <summary>Copies <paramref name="source"/> shifted by (<paramref name="dx"/>, <paramref name="dy"/>) whole pixels; what moves in from outside is transparent.</summary>
    public static void Offset(RasterSurface source, RasterSurface destination, int dx, int dy)
    {
        destination.Clear();
        var w = source.Width;
        var h = source.Height;
        var s = source.Pixels;
        var d = destination.Pixels;
        for (var y = 0; y < h; y++)
        {
            var sy = y - dy;
            if (sy < 0 || sy >= h)
                continue;

            var x0 = Math.Max(0, dx);
            var x1 = Math.Min(w, w + dx);
            if (x1 <= x0)
                continue;

            s.Slice((sy * w + x0 - dx) * 4, (x1 - x0) * 4).CopyTo(d.Slice((y * w + x0) * 4, (x1 - x0) * 4));
        }
    }

    /// <summary>Repeats the <paramref name="tile"/> rectangle of <paramref name="source"/> across the whole of <paramref name="destination"/>.</summary>
    public static void Tile(RasterSurface source, RasterSurface destination, IntRect tile)
    {
        tile = tile.Intersect(source.Bounds);
        if (tile.IsEmpty)
        {
            destination.Clear();
            return;
        }

        var tw = tile.Width;
        var th = tile.Height;
        var s = source.Pixels;
        var d = destination.Pixels;
        for (var y = 0; y < destination.Height; y++)
        {
            var sy = tile.Top + PositiveModulo(y - tile.Top, th);
            for (var x = 0; x < destination.Width; x++)
            {
                var sx = tile.Left + PositiveModulo(x - tile.Left, tw);
                s.Slice((sy * source.Width + sx) * 4, 4).CopyTo(d.Slice((y * destination.Width + x) * 4, 4));
            }
        }
    }

    private static int PositiveModulo(int value, int modulus) => ((value % modulus) + modulus) % modulus;

    // ---- compositing ------------------------------------------------------------------------------------

    /// <summary>Porter-Duff <c>over</c>, <c>in</c>, <c>out</c>, <c>atop</c> or <c>xor</c> of <paramref name="a"/> (the <c>in</c> input) with <paramref name="b"/> (<c>in2</c>).</summary>
    public static void PorterDuff(string op, RasterSurface a, RasterSurface b, RasterSurface result)
    {
        var pa = a.Pixels;
        var pb = b.Pixels;
        var pr = result.Pixels;
        for (var i = 0; i + 3 < pa.Length; i += 4)
        {
            var aa = pa[i + 3];
            var ab = pb[i + 3];
            for (var c = 0; c < 4; c++)
            {
                int ca = pa[i + c];
                int cb = pb[i + c];
                int v = op switch
                {
                    "in" => PixelKernels.Div255(ca * ab),
                    "out" => PixelKernels.Div255(ca * (255 - ab)),
                    "atop" => PixelKernels.Div255(ca * ab) + PixelKernels.Div255(cb * (255 - aa)),
                    "xor" => PixelKernels.Div255(ca * (255 - ab)) + PixelKernels.Div255(cb * (255 - aa)),
                    _ => ca + PixelKernels.Div255(cb * (255 - aa)),
                };
                pr[i + c] = (byte)Math.Min(v, 255);
            }
        }
    }

    /// <summary><c>k1*i1*i2 + k2*i1 + k3*i2 + k4</c> on premultiplied 0-1 channels, clamped, with colour kept within alpha.</summary>
    public static void Arithmetic(RasterSurface a, RasterSurface b, RasterSurface result, double k1, double k2, double k3, double k4)
    {
        var pa = a.Pixels;
        var pb = b.Pixels;
        var pr = result.Pixels;
        for (var i = 0; i + 3 < pa.Length; i += 4)
        {
            var values = new double[4];
            for (var c = 0; c < 4; c++)
            {
                var ia = pa[i + c] / 255.0;
                var ib = pb[i + c] / 255.0;
                values[c] = Math.Clamp(k1 * ia * ib + k2 * ia + k3 * ib + k4, 0.0, 1.0);
            }

            for (var c = 0; c < 3; c++)
                values[c] = Math.Min(values[c], values[3]);

            for (var c = 0; c < 4; c++)
                pr[i + c] = (byte)Math.Round(values[c] * 255.0);
        }
    }

    /// <summary>Composites <paramref name="top"/> over <paramref name="bottom"/> with a blend mode, into <paramref name="result"/>.</summary>
    public static void Blend(RBlendMode mode, RasterSurface top, RasterSurface bottom, RasterSurface result)
    {
        bottom.Pixels.CopyTo(result.Pixels);
        var pt = top.Pixels;
        var pr = result.Pixels;
        for (var i = 0; i + 3 < pt.Length; i += 4)
        {
            if (pt[i + 3] == 0)
                continue;

            if (mode == RBlendMode.Normal)
            {
                int inverse = 255 - pt[i + 3];
                for (var c = 0; c < 4; c++)
                    pr[i + c] = (byte)Math.Min(pt[i + c] + PixelKernels.Div255(pr[i + c] * inverse), 255);
            }
            else
            {
                BlendModes.Blend(mode, pr.Slice(i, 4), pt[i], pt[i + 1], pt[i + 2], pt[i + 3]);
            }
        }
    }

    /// <summary>Draws <paramref name="top"/> over <paramref name="destination"/> in place (source-over).</summary>
    public static void OverInPlace(RasterSurface destination, RasterSurface top)
    {
        var pd = destination.Pixels;
        var pt = top.Pixels;
        for (var i = 0; i + 3 < pd.Length; i += 4)
        {
            int inverse = 255 - pt[i + 3];
            if (inverse == 255)
                continue;

            for (var c = 0; c < 4; c++)
                pd[i + c] = (byte)Math.Min(pt[i + c] + PixelKernels.Div255(pd[i + c] * inverse), 255);
        }
    }

    // ---- component transfer -----------------------------------------------------------------------------

    /// <summary>Evaluates one transfer function over the 256 possible 8-bit inputs.</summary>
    public static byte[] BuildTransferLut(PeachPDF.Svg.TransferFunction function)
    {
        var lut = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var c = i / 255.0;
            double v;
            switch (function.Kind)
            {
                case PeachPDF.Svg.TransferKind.Table:
                {
                    var n = function.Table.Length - 1;
                    var k = Math.Min((int)Math.Floor(c * n), n);
                    v = k == n || n == 0
                        ? function.Table[n]
                        : function.Table[k] + (c - (double)k / n) * n * (function.Table[k + 1] - function.Table[k]);
                    break;
                }

                case PeachPDF.Svg.TransferKind.Discrete:
                {
                    var n = function.Table.Length;
                    v = function.Table[Math.Min((int)Math.Floor(c * n), n - 1)];
                    break;
                }

                case PeachPDF.Svg.TransferKind.Linear:
                    v = function.Slope * c + function.Intercept;
                    break;

                case PeachPDF.Svg.TransferKind.Gamma:
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

    /// <summary>Applies four per-channel lookup tables (R, G, B, A) to the straight colour of every pixel.</summary>
    public static void ComponentTransfer(RasterSurface source, RasterSurface destination, byte[] r, byte[] g, byte[] b, byte[] a)
    {
        var ps = source.Pixels;
        var pd = destination.Pixels;
        for (var i = 0; i + 3 < ps.Length; i += 4)
        {
            int alpha = ps[i + 3];
            int sr = 0, sg = 0, sb = 0;
            if (alpha > 0)
            {
                sr = Math.Min(255, (ps[i] * 255 + alpha / 2) / alpha);
                sg = Math.Min(255, (ps[i + 1] * 255 + alpha / 2) / alpha);
                sb = Math.Min(255, (ps[i + 2] * 255 + alpha / 2) / alpha);
            }

            int na = a[alpha];
            pd[i] = (byte)((r[sr] * na + 127) / 255);
            pd[i + 1] = (byte)((g[sg] * na + 127) / 255);
            pd[i + 2] = (byte)((b[sb] * na + 127) / 255);
            pd[i + 3] = (byte)na;
        }
    }

    // ---- morphology -------------------------------------------------------------------------------------

    /// <summary>Erodes (per-channel minimum) or dilates (maximum) with a (2rx+1) x (2ry+1) window; what lies outside the surface counts as transparent.</summary>
    public static void Morphology(RasterSurface source, RasterSurface destination, int radiusX, int radiusY, bool dilate)
    {
        if (System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated)
            MorphologyVector(source, destination, radiusX, radiusY, dilate);
        else
            MorphologyScalar(source, destination, radiusX, radiusY, dilate);
    }

    /// <summary>The scalar reference for <see cref="Morphology"/>; the vector form must produce the same bytes.</summary>
    internal static void MorphologyScalar(RasterSurface source, RasterSurface destination, int radiusX, int radiusY, bool dilate)
    {
        var w = source.Width;
        var h = source.Height;
        var temp = new byte[w * h * 4];
        var ps = source.Pixels;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                for (var c = 0; c < 4; c++)
                    temp[(y * w + x) * 4 + c] = Extreme(dilate, x - radiusX, x + radiusX, w, ps, y * w * 4 + c, 4);
            }
        }

        var pd = destination.Pixels;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                for (var c = 0; c < 4; c++)
                    pd[(y * w + x) * 4 + c] = Extreme(dilate, y - radiusY, y + radiusY, h, temp, x * 4 + c, w * 4);
            }
        }
    }

    /// <summary>The minimum or maximum of <paramref name="length"/> samples starting at <paramref name="origin"/> spaced <paramref name="stride"/> apart, over the window [from, to]; samples outside count as 0.</summary>
    private static byte Extreme(bool max, int from, int to, int length, ReadOnlySpan<byte> data, int origin, int stride)
    {
        if (!max && (from < 0 || to >= length))
            return 0;

        byte best = max ? (byte)0 : (byte)255;
        for (var i = Math.Max(from, 0); i <= Math.Min(to, length - 1); i++)
        {
            var v = data[origin + i * stride];
            best = max ? Math.Max(best, v) : Math.Min(best, v);
        }

        return best;
    }

    // ---- convolution ------------------------------------------------------------------------------------

    public static void ConvolveMatrix(RasterSurface source, RasterSurface destination, PeachPDF.Svg.FeConvolveMatrix p)
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
                                case PeachPDF.Svg.FilterEdgeMode.None:
                                    continue;
                                case PeachPDF.Svg.FilterEdgeMode.Wrap:
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

    // ---- displacement -----------------------------------------------------------------------------------

    /// <summary>Each output pixel takes the input pixel displaced by the map's channels: <c>P'(x, y) = P(x + sx * (XC - 0.5), y + sy * (YC - 0.5))</c>, nearest pixel, transparent outside.</summary>
    public static void Displace(RasterSurface source, RasterSurface map, RasterSurface destination, double scaleX, double scaleY, int xChannel, int yChannel)
    {
        var w = source.Width;
        var h = source.Height;
        var ps = source.Pixels;
        var pm = map.Buffer;
        var pd = destination.Pixels;
        destination.Clear();

        double Channel(int pixel, int channel)
        {
            int a = pm[pixel * 4 + 3];
            if (channel == 3)
                return a / 255.0;

            if (a == 0)
                return 0;

            return Math.Min(1.0, pm[pixel * 4 + channel] / (double)a);
        }

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var pixel = y * w + x;
                var sx = (int)Math.Floor(x + scaleX * (Channel(pixel, xChannel) - 0.5) + 0.5);
                var sy = (int)Math.Floor(y + scaleY * (Channel(pixel, yChannel) - 0.5) + 0.5);
                if (sx < 0 || sx >= w || sy < 0 || sy >= h)
                    continue;

                ps.Slice((sy * w + sx) * 4, 4).CopyTo(pd.Slice(pixel * 4, 4));
            }
        }
    }
}

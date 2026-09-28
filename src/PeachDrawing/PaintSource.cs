using PeachDrawing.Abstractions;
using System;
using System.Linq;

namespace PeachDrawing;

/// <summary>
/// Produces the source colour for each pixel of a fill or stroke: a constant, a gradient, or a bitmap.
/// Pixels are premultiplied RGBA8, evaluated at pixel centres.
/// </summary>
internal abstract class PaintSource
{
    /// <summary>True when every pixel has the same colour, so callers can use the cheaper solid kernels.</summary>
    public virtual bool IsSolid => false;

    /// <summary>The constant premultiplied colour (see <see cref="PixelKernels.Pack"/>); valid only when <see cref="IsSolid"/>.</summary>
    public virtual uint SolidColor => 0;

    /// <summary>Writes <paramref name="count"/> premultiplied RGBA pixels for device pixels (<paramref name="x0"/>, <paramref name="y"/>) onwards.</summary>
    public abstract void FillSpan(int x0, int y, int count, Span<byte> destination);

    /// <summary>
    /// Builds the paint for <paramref name="brush"/> - a self-describing <see cref="Brush"/>, read directly
    /// (no PDF-specific type involved, unlike this method before the brush redesign). <paramref name="deviceToUser"/>
    /// maps a device pixel back to the user space the brush's geometry was authored in (the CTM that was current
    /// when it is used). Returns null for a brush kind that cannot be painted.
    /// </summary>
    public static PaintSource? From(Brush? brush, in Affine deviceToUser)
    {
        switch (brush)
        {
            case SolidBrush solid:
                return new SolidPaint(solid.PaintColor);
            case LinearGradientBrush linear:
                return LinearPaint.Create(linear, deviceToUser);
            case RadialGradientBrush radial:
                return RadialPaint.Create(radial, deviceToUser);
            case ConicGradientBrush conic:
                return ConicPaint.Create(conic, deviceToUser);
            default:
                return null;
        }
    }

    public static PaintSource FromColor(PaintColor color) => new SolidPaint(color);

    /// <summary>Premultiplies a straight colour (0-255 channels, 0-1 alpha) into bytes.</summary>
    internal static void Premultiply(float r, float g, float b, float a, out byte pr, out byte pg, out byte pb, out byte pa)
    {
        a = Math.Clamp(a, 0f, 1f);
        pa = (byte)MathF.Round(a * 255f);
        pr = (byte)Math.Clamp(MathF.Round(r * a), 0f, 255f);
        pg = (byte)Math.Clamp(MathF.Round(g * a), 0f, 255f);
        pb = (byte)Math.Clamp(MathF.Round(b * a), 0f, 255f);
    }

    private sealed class SolidPaint : PaintSource
    {
        private readonly uint _color;

        public SolidPaint(PaintColor color)
        {
            // PaintColor.ToRgb() gives an RGB rendition for RGB or CMYK alike (naive for CMYK); the raster
            // backend has no colour management.
            var (cr, cg, cb) = color.ToRgb();
            Premultiply(cr, cg, cb, color.A / 255f, out var r, out var g, out var b, out var a);
            _color = PixelKernels.Pack(r, g, b, a);
        }

        public override bool IsSolid => true;

        public override uint SolidColor => _color;

        public override void FillSpan(int x0, int y, int count, Span<byte> destination)
        {
            var c = _color;
            for (var i = 0; i < count; i++)
            {
                destination[i * 4] = (byte)c;
                destination[i * 4 + 1] = (byte)(c >> 8);
                destination[i * 4 + 2] = (byte)(c >> 16);
                destination[i * 4 + 3] = (byte)(c >> 24);
            }
        }
    }

    /// <summary>Colour stops evaluated exactly (no lookup table), so hard stops stay crisp.</summary>
    internal sealed class Stops
    {
        private readonly float[] _positions;
        private readonly float[] _r, _g, _b, _a;

        public Stops(System.Collections.Generic.IReadOnlyList<GradientStop> stops)
        {
            var n = stops.Count;
            _positions = new float[n];
            _r = new float[n];
            _g = new float[n];
            _b = new float[n];
            _a = new float[n];
            var last = float.NegativeInfinity;
            for (var i = 0; i < n; i++)
            {
                var p = Math.Max((float)stops[i].Position, last);
                last = p;
                _positions[i] = p;
                var (cr, cg, cb) = stops[i].PaintColor.ToRgb();
                _r[i] = cr;
                _g[i] = cg;
                _b[i] = cb;
                _a[i] = stops[i].PaintColor.A / 255f;
            }
        }

        public float First => _positions[0];

        public float Last => _positions[^1];

        /// <summary>The premultiplied colour at position <paramref name="t"/>, padding before the first and after the last stop.</summary>
        public uint At(float t)
        {
            var n = _positions.Length;
            if (t <= _positions[0])
                return Pack(0, 0f);

            if (t >= _positions[n - 1])
                return Pack(n - 1, 0f);

            var i = 1;
            while (i < n - 1 && _positions[i] < t)
                i++;

            var span = _positions[i] - _positions[i - 1];
            var f = span > 0 ? (t - _positions[i - 1]) / span : 1f;
            return Pack(i - 1, f, i);
        }

        private uint Pack(int i, float f, int j = -1)
        {
            float r, g, b, a;
            if (j < 0)
            {
                r = _r[i]; g = _g[i]; b = _b[i]; a = _a[i];
            }
            else
            {
                // Interpolate premultiplied, as CSS Images 3 §3.5.1 requires.
                var a0 = _a[i]; var a1 = _a[j];
                a = a0 + (a1 - a0) * f;
                var pr = _r[i] * a0 + (_r[j] * a1 - _r[i] * a0) * f;
                var pg = _g[i] * a0 + (_g[j] * a1 - _g[i] * a0) * f;
                var pb = _b[i] * a0 + (_b[j] * a1 - _b[i] * a0) * f;
                if (a > 0)
                {
                    r = pr / a; g = pg / a; b = pb / a;
                }
                else
                {
                    r = g = b = 0;
                }
            }

            Premultiply(r, g, b, a, out var qr, out var qg, out var qb, out var qa);
            return PixelKernels.Pack(qr, qg, qb, qa);
        }

        public float Wrap(float t)
        {
            var span = Last - First;
            if (!(span > 0))
                return First;

            var u = (t - First) / span;
            u -= MathF.Floor(u);
            return First + u * span;
        }
    }

    private static void Write(Span<byte> destination, int index, uint color)
    {
        var p = index * 4;
        destination[p] = (byte)color;
        destination[p + 1] = (byte)(color >> 8);
        destination[p + 2] = (byte)(color >> 16);
        destination[p + 3] = (byte)(color >> 24);
    }

    private sealed class LinearPaint : PaintSource
    {
        private readonly Affine _deviceToUser;
        private readonly Stops _stops;
        private readonly double _px, _py, _dx, _dy, _invLen2;
        private readonly bool _repeating;

        private LinearPaint(in Affine deviceToUser, Stops stops, double x1, double y1, double x2, double y2, bool repeating)
        {
            _deviceToUser = deviceToUser;
            _stops = stops;
            _px = x1;
            _py = y1;
            _dx = x2 - x1;
            _dy = y2 - y1;
            var len2 = _dx * _dx + _dy * _dy;
            _invLen2 = len2 > 0 ? 1 / len2 : 0;
            _repeating = repeating;
        }

        public static PaintSource? Create(LinearGradientBrush brush, in Affine deviceToUser)
        {
            if (brush.Stops.Count == 0)
                return null;

            return new LinearPaint(deviceToUser, new Stops(brush.Stops), brush.Start.X, brush.Start.Y, brush.End.X, brush.End.Y,
                brush.Spread == GradientSpread.Repeat);
        }

        public override void FillSpan(int x0, int y, int count, Span<byte> destination)
        {
            for (var i = 0; i < count; i++)
            {
                var (ux, uy) = _deviceToUser.Apply(x0 + i + 0.5, y + 0.5);
                var t = (float)(((ux - _px) * _dx + (uy - _py) * _dy) * _invLen2);
                if (_repeating)
                    t = _stops.Wrap(t);

                Write(destination, i, _stops.At(t));
            }
        }
    }

    private sealed class RadialPaint : PaintSource
    {
        private readonly Affine _deviceToUser;
        private readonly Stops _stops;
        private readonly double _cx, _cy, _rx, _ry, _fx, _fy, _a;
        private readonly bool _repeating;
        private readonly double _innerRatio;

        private RadialPaint(in Affine deviceToUser, Stops stops, double cx, double cy, double rx, double ry,
            double fx, double fy, bool repeating, double innerRatio)
        {
            _deviceToUser = deviceToUser;
            _stops = stops;
            _cx = cx;
            _cy = cy;
            _rx = rx > 0 ? rx : 1e-9;
            _ry = ry > 0 ? ry : 1e-9;
            _repeating = repeating;
            _innerRatio = innerRatio;

            // The focal point in the space where the ellipse is the unit circle, kept strictly inside it.
            var nx = (fx - cx) / _rx;
            var ny = (fy - cy) / _ry;
            var len = Math.Sqrt(nx * nx + ny * ny);
            if (len > 0.9999)
            {
                nx *= 0.9999 / len;
                ny *= 0.9999 / len;
                len = 0.9999;
            }

            _fx = nx;
            _fy = ny;
            _a = 1 - len * len;
        }

        public static PaintSource? Create(RadialGradientBrush brush, in Affine deviceToUser)
        {
            if (brush.Stops.Count == 0)
                return null;

            // RenderContext.GetRadialGradientBrush always resolves a focus (defaulting to the centre - see its
            // own remarks) and never expresses an inner radius, so there is no "two-colour, r1/r2" shape
            // to handle here the way XRadialGradientBrush's own dual constructor does.
            return new RadialPaint(deviceToUser, new Stops(brush.Stops), brush.Center.X, brush.Center.Y,
                brush.RadiusX, brush.RadiusY, brush.Focus.X, brush.Focus.Y, brush.Spread == GradientSpread.Repeat, innerRatio: 0);
        }

        public override void FillSpan(int x0, int y, int count, Span<byte> destination)
        {
            for (var i = 0; i < count; i++)
            {
                var (ux, uy) = _deviceToUser.Apply(x0 + i + 0.5, y + 0.5);
                var px = (ux - _cx) / _rx;
                var py = (uy - _cy) / _ry;

                // Two-point conical gradient from the focal point (radius 0) to the unit circle:
                // |p - (1 - t) f| = t  =>  a t^2 - 2 (q.f) t - |q|^2 = 0 with q = p - f.
                var qx = px - _fx;
                var qy = py - _fy;
                var qf = qx * _fx + qy * _fy;
                var q2 = qx * qx + qy * qy;
                var t = (float)((qf + Math.Sqrt(qf * qf + _a * q2)) / _a);

                if (_innerRatio > 0)
                    t = (float)((t - _innerRatio) / (1 - _innerRatio));

                if (_repeating)
                    t = _stops.Wrap(t);

                Write(destination, i, _stops.At(t));
            }
        }
    }

    private sealed class ConicPaint : PaintSource
    {
        private readonly Affine _deviceToUser;
        private readonly Stops _stops;
        private readonly double _cx, _cy;

        private ConicPaint(in Affine deviceToUser, Stops stops, double cx, double cy)
        {
            _deviceToUser = deviceToUser;
            _stops = stops;
            _cx = cx;
            _cy = cy;
        }

        public static PaintSource? Create(ConicGradientBrush brush, in Affine deviceToUser)
        {
            if (brush.Stops.Count == 0)
                return null;

            return new ConicPaint(deviceToUser, new Stops(brush.Stops), brush.Center.X, brush.Center.Y);
        }

        public override void FillSpan(int x0, int y, int count, Span<byte> destination)
        {
            for (var i = 0; i < count; i++)
            {
                var (ux, uy) = _deviceToUser.Apply(x0 + i + 0.5, y + 0.5);

                // 0 at 12 o'clock, increasing clockwise (y grows downwards in user space).
                var angle = Math.Atan2(ux - _cx, -(uy - _cy));
                if (angle < 0)
                    angle += 2 * Math.PI;

                // The stops run from the gradient's start angle (`from <angle>`, which may be negative or exceed a turn) for
                // one full turn, so every pixel angle is wrapped into [start, start + 2 pi) rather than padded.
                var start = _stops.First;
                var turn = 2 * Math.PI;
                angle = start + (angle - start - turn * Math.Floor((angle - start) / turn));

                Write(destination, i, _stops.At((float)angle));
            }
        }
    }
}

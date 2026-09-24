using System;
using System.Collections.Generic;

namespace PeachPDF.Raster;

internal enum StrokeCap { Butt, Round, Square }

internal enum StrokeJoin { Miter, Round, Bevel }

/// <summary>The geometric stroke parameters, all in the path's own (user-space) units.</summary>
internal readonly record struct StrokeStyle(
    double Width,
    StrokeCap Cap,
    StrokeJoin Join,
    double MiterLimit,
    double[]? Dashes,
    double DashOffset);

/// <summary>
/// Converts a flattened path into the union of polygons a stroke of it covers. Stroking happens in user
/// space (so a non-uniform transform gives an elliptical pen, as in PDF) and every emitted piece is then
/// transformed to device space. Each piece is a simple polygon; the caller fills the whole set with the
/// nonzero rule after <see cref="PolygonSet.NormalizeWinding"/>, which makes the pieces a true union.
/// </summary>
internal static class Stroker
{
    /// <summary>The flatness, in device pixels, used when approximating round joins and caps.</summary>
    private const double ArcTolerance = 0.1;

    public static void Stroke(FlatPath path, in StrokeStyle style, in Affine toDevice, PolygonSet output)
    {
        var halfWidth = style.Width / 2;
        if (!(halfWidth > 0) || double.IsNaN(halfWidth) || double.IsInfinity(halfWidth))
            return;

        var context = new Context(style, toDevice, output, halfWidth);

        for (var c = 0; c < path.ContourCount; c++)
        {
            var (start, end) = path.Contours.GetContour(c);
            var points = new List<(double X, double Y)>(end - start);
            for (var i = start; i < end; i++)
            {
                var p = path.Contours.GetPoint(i);
                if (points.Count == 0 || Distance(points[^1], p) > 1e-12)
                    points.Add(p);
            }

            var closed = path.Closed[c];
            if (closed && points.Count > 1 && Distance(points[0], points[^1]) <= 1e-12)
                points.RemoveAt(points.Count - 1);

            if (points.Count == 0)
                continue;

            if (style.Dashes is { Length: > 0 } dashes && Sum(dashes) > 0)
            {
                foreach (var (piece, hint) in Dash(points, closed, dashes, style.DashOffset))
                    context.StrokePolyline(piece, closed: false, hint);
            }
            else
            {
                context.StrokePolyline(points, closed && points.Count > 2, default);
            }
        }
    }

    private static double Sum(double[] values)
    {
        double sum = 0;
        foreach (var v in values)
        {
            if (v < 0 || double.IsNaN(v)) return 0;
            sum += v;
        }

        return sum;
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>Splits a polyline into dash pieces. Each piece carries the direction at its start for degenerate (dot) dashes.</summary>
    private static List<(List<(double X, double Y)> Points, (double X, double Y) Direction)> Dash(
        List<(double X, double Y)> points, bool closed, double[] pattern, double offset)
    {
        var result = new List<(List<(double, double)>, (double, double))>();

        // An odd-length pattern repeats with alternating roles (ISO 32000-1 §8.4.3.6).
        double[] pat = pattern.Length % 2 == 1 ? [.. pattern, .. pattern] : pattern;
        var total = Sum(pat);

        var index = 0;
        var phase = offset % total;
        if (phase < 0) phase += total;
        // Skip whole elements the offset has already consumed. A zero-length element at phase 0 is kept, so a
        // pattern that starts with a dot ("0 8" with round caps) draws it at the very start of the line.
        while (phase > 0 && phase >= pat[index])
        {
            phase -= pat[index];
            index = (index + 1) % pat.Length;
        }

        var remaining = pat[index] - phase;
        var on = index % 2 == 0;

        List<(double X, double Y)>? current = null;
        var count = closed ? points.Count : points.Count - 1;
        (double X, double Y) lastDir = (1, 0);

        if (on)
        {
            current = [points[0]];
        }

        for (var s = 0; s < count; s++)
        {
            var a = points[s];
            var b = points[(s + 1) % points.Count];
            var length = Distance(a, b);
            if (length <= 0)
                continue;

            var dx = (b.X - a.X) / length;
            var dy = (b.Y - a.Y) / length;
            lastDir = (dx, dy);
            var position = 0.0;

            while (length - position > remaining)
            {
                position += remaining;
                var p = (a.X + dx * position, a.Y + dy * position);

                if (on)
                {
                    current!.Add(p);
                    result.Add((current, (dx, dy)));
                    current = null;
                }
                else
                {
                    current = [p];
                }

                on = !on;
                index = (index + 1) % pat.Length;
                remaining = pat[index];

                // A zero-length dash is a dot: emit it straight away.
                if (on && remaining == 0)
                {
                    result.Add(([p], (dx, dy)));
                    on = false;
                    index = (index + 1) % pat.Length;
                    remaining = pat[index];
                    current = null;
                }
            }

            remaining -= length - position;
            current?.Add(b);
        }

        if (on && current is { Count: > 0 })
            result.Add((current, lastDir));

        return result;
    }

    private sealed class Context(StrokeStyle style, Affine toDevice, PolygonSet output, double halfWidth)
    {
        private readonly StrokeStyle _style = style;
        private readonly Affine _toDevice = toDevice;
        private readonly double _deviceHalfWidth = halfWidth * toDevice.MaxScale;

        public void StrokePolyline(List<(double X, double Y)> input, bool closed, (double X, double Y) directionHint)
        {
            // A dash that ends exactly on a vertex repeats that point; a repeat is a zero-length segment with an
            // arbitrary direction, which would add a spurious join and mis-orient a cap.
            var pts = new List<(double X, double Y)>(input.Count);
            foreach (var p in input)
            {
                if (pts.Count == 0 || Distance(pts[^1], p) > 1e-12)
                    pts.Add(p);
            }

            if (closed && pts.Count > 2 && Distance(pts[0], pts[^1]) <= 1e-12)
                pts.RemoveAt(pts.Count - 1);

            if (pts.Count == 1)
            {
                StrokeDot(pts[0], directionHint);
                return;
            }

            var n = pts.Count;
            var segments = closed ? n : n - 1;
            var dirs = new (double X, double Y)[segments];
            for (var i = 0; i < segments; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % n];
                var len = Distance(a, b);
                dirs[i] = len > 0 ? ((b.X - a.X) / len, (b.Y - a.Y) / len) : (1, 0);
            }

            for (var i = 0; i < segments; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % n];
                var d = dirs[i];
                var nx = -d.Y * halfWidth;
                var ny = d.X * halfWidth;
                AddPiece((a.X + nx, a.Y + ny), (b.X + nx, b.Y + ny), (b.X - nx, b.Y - ny), (a.X - nx, a.Y - ny));
            }

            // Joins at every interior vertex (and at the seam of a closed contour).
            var firstJoin = closed ? 0 : 1;
            var lastJoin = closed ? n - 1 : n - 2;
            for (var v = firstJoin; v <= lastJoin; v++)
            {
                var prev = dirs[(v - 1 + segments) % segments];
                var next = dirs[v % segments];
                AddJoin(pts[v], prev, next);
            }

            if (!closed)
            {
                AddCap(pts[0], (-dirs[0].X, -dirs[0].Y));
                AddCap(pts[n - 1], dirs[segments - 1]);
            }
        }

        private void StrokeDot((double X, double Y) p, (double X, double Y) hint)
        {
            switch (_style.Cap)
            {
                case StrokeCap.Round:
                    AddDisc(p);
                    break;
                case StrokeCap.Square:
                    var d = hint == default ? (1.0, 0.0) : hint;
                    var nx = -d.Item2 * halfWidth;
                    var ny = d.Item1 * halfWidth;
                    var ex = d.Item1 * halfWidth;
                    var ey = d.Item2 * halfWidth;
                    AddPiece((p.X - ex + nx, p.Y - ey + ny), (p.X + ex + nx, p.Y + ey + ny),
                        (p.X + ex - nx, p.Y + ey - ny), (p.X - ex - nx, p.Y - ey - ny));
                    break;
            }
        }

        private void AddJoin((double X, double Y) p, (double X, double Y) d0, (double X, double Y) d1)
        {
            var cross = d0.X * d1.Y - d0.Y * d1.X;
            var dot = d0.X * d1.X + d0.Y * d1.Y;
            if (Math.Abs(cross) < 1e-12 && dot > 0)
                return;

            var n0 = (X: -d0.Y * halfWidth, Y: d0.X * halfWidth);
            var n1 = (X: -d1.Y * halfWidth, Y: d1.X * halfWidth);
            var sgn = cross > 0 ? -1.0 : 1.0;
            var o0 = (X: p.X + sgn * n0.X, Y: p.Y + sgn * n0.Y);
            var o1 = (X: p.X + sgn * n1.X, Y: p.Y + sgn * n1.Y);

            switch (_style.Join)
            {
                case StrokeJoin.Round:
                    AddArcFan(p, o0, o1);
                    return;

                case StrokeJoin.Miter:
                    // cos(turn angle); the miter tip sits at halfWidth / cos(turn / 2) from the vertex.
                    var cosTurn = dot;
                    var cosHalf = Math.Sqrt(Math.Max(0, (1 + cosTurn) / 2));
                    var limit = _style.MiterLimit >= 1 ? _style.MiterLimit : 10;
                    if (cosHalf > 1e-9 && 1 / cosHalf <= limit)
                    {
                        var k = 1 / (1 + cosTurn);
                        var tip = (X: p.X + sgn * (n0.X + n1.X) * k, Y: p.Y + sgn * (n0.Y + n1.Y) * k);
                        AddPiece(p, o0, tip, o1);
                        return;
                    }

                    break;
            }

            AddPiece(p, o0, o1);
        }

        private void AddCap((double X, double Y) p, (double X, double Y) outward)
        {
            switch (_style.Cap)
            {
                case StrokeCap.Square:
                    var nx = -outward.Y * halfWidth;
                    var ny = outward.X * halfWidth;
                    var ex = outward.X * halfWidth;
                    var ey = outward.Y * halfWidth;
                    AddPiece((p.X + nx, p.Y + ny), (p.X + nx + ex, p.Y + ny + ey), (p.X - nx + ex, p.Y - ny + ey), (p.X - nx, p.Y - ny));
                    break;

                case StrokeCap.Round:
                    AddSemicircle(p, outward);
                    break;
            }
        }

        private int ArcSteps(double sweep)
        {
            var r = Math.Max(_deviceHalfWidth, 1e-6);
            var da = 2 * Math.Acos(1 - Math.Min(ArcTolerance / r, 0.5));
            if (!(da > 1e-3)) da = 1e-3;
            return Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / da), 1, 128);
        }

        private void AddArcFan((double X, double Y) center, (double X, double Y) from, (double X, double Y) to)
        {
            var a0 = Math.Atan2(from.Y - center.Y, from.X - center.X);
            var a1 = Math.Atan2(to.Y - center.Y, to.X - center.X);
            var sweep = a1 - a0;
            while (sweep > Math.PI) sweep -= 2 * Math.PI;
            while (sweep < -Math.PI) sweep += 2 * Math.PI;

            var steps = ArcSteps(sweep);
            output.BeginContour();
            AddDevicePoint(center.X, center.Y);
            for (var i = 0; i <= steps; i++)
            {
                var a = a0 + sweep * i / steps;
                AddDevicePoint(center.X + Math.Cos(a) * halfWidth, center.Y + Math.Sin(a) * halfWidth);
            }
        }

        private void AddSemicircle((double X, double Y) p, (double X, double Y) outward)
        {
            // Start at the left normal, sweep through the outward direction to the right normal.
            var a0 = Math.Atan2(outward.X, -outward.Y);
            var sweep = Math.PI;
            var mid = a0 + sweep / 2;
            if (Math.Cos(mid) * outward.X + Math.Sin(mid) * outward.Y < 0)
                sweep = -sweep;

            var steps = ArcSteps(sweep);
            output.BeginContour();
            for (var i = 0; i <= steps; i++)
            {
                var a = a0 + sweep * i / steps;
                AddDevicePoint(p.X + Math.Cos(a) * halfWidth, p.Y + Math.Sin(a) * halfWidth);
            }
        }

        private void AddDisc((double X, double Y) p)
        {
            var steps = Math.Max(8, ArcSteps(2 * Math.PI));
            output.BeginContour();
            for (var i = 0; i < steps; i++)
            {
                var a = 2 * Math.PI * i / steps;
                AddDevicePoint(p.X + Math.Cos(a) * halfWidth, p.Y + Math.Sin(a) * halfWidth);
            }
        }

        private void AddPiece((double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
        {
            output.BeginContour();
            AddDevicePoint(a.X, a.Y);
            AddDevicePoint(b.X, b.Y);
            AddDevicePoint(c.X, c.Y);
        }

        private void AddPiece((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
        {
            output.BeginContour();
            AddDevicePoint(a.X, a.Y);
            AddDevicePoint(b.X, b.Y);
            AddDevicePoint(c.X, c.Y);
            AddDevicePoint(d.X, d.Y);
        }

        private void AddDevicePoint(double x, double y)
        {
            var (dx, dy) = _toDevice.Apply(x, y);
            output.Add(dx, dy);
        }
    }
}

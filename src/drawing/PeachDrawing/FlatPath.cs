using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachDrawing;

/// <summary>
/// A path reduced to polylines: every curve replaced by straight segments within a flatness tolerance.
/// Unlike <see cref="PolygonSet"/> it remembers which subpaths were explicitly closed, which the stroker
/// needs (an open subpath gets caps, a closed one gets a join where it meets itself).
/// </summary>
internal sealed class FlatPath
{
    public PolygonSet Contours { get; } = new();

    /// <summary>One flag per contour, in the same order as <see cref="Contours"/>.</summary>
    public List<bool> Closed { get; } = [];

    public int ContourCount => Contours.ContourCount;

    private void Begin(double x, double y)
    {
        Contours.BeginContour();
        Closed.Add(false);
        Contours.Add(x, y);
    }

    /// <summary>Appends a ready-made polyline as one contour.</summary>
    public void AddContour(ReadOnlySpan<(double X, double Y)> points, bool closed)
    {
        if (points.Length == 0)
            return;

        Begin(points[0].X, points[0].Y);
        for (var i = 1; i < points.Length; i++)
            Contours.Add(points[i].X, points[i].Y);

        if (closed)
            MarkClosed();
    }

    /// <summary>Flattens a cubic Bezier from (<paramref name="x0"/>, <paramref name="y0"/>), appending its points to the current contour.</summary>
    public void CubicTo(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, double tolerance) =>
        AddCubic(Contours, x0, y0, x1, y1, x2, y2, x3, y3, tolerance);

    /// <summary>Starts a new contour.</summary>
    public void MoveTo(double x, double y) => Begin(x, y);

    /// <summary>Appends a point to the current contour.</summary>
    public void LineTo(double x, double y) => Contours.Add(x, y);

    /// <summary>Marks the current contour closed.</summary>
    public void Close() => MarkClosed();

    private void MarkClosed()
    {
        if (Closed.Count > 0)
            Closed[^1] = true;
    }

    /// <summary>
    /// Builds a <see cref="FlatPath"/> from an <see cref="GraphicsPath"/>'s own flattened geometry
    /// (<see cref="GraphicsPath.Flatten"/>) - the backend-agnostic replacement for reading
    /// <c>PdfGraphicsPath</c>'s wrapped <c>GraphicsPath</c> directly.
    /// </summary>
    public static FlatPath From(GraphicsPath path, double tolerance)
    {
        var flat = new FlatPath();
        foreach (var contour in path.Flatten(tolerance))
            flat.AddContour(contour.Points.Select(p => (p.X, p.Y)).ToArray(), contour.Closed);

        return flat;
    }

    /// <summary>
    /// Like <see cref="From"/>, for stroking: the curves are flattened the same way, but an open subpath that starts or ends on a curve gets one extra point a hair from that end,
    /// so the first and last pieces point along the curve's own tangent. A cap is drawn square to those pieces, and without the point it would be square to a chord that is as
    /// much as half a flattening step off the curve - a visible tilt on a wide stroke. A stroke wide against its curve has more to get right than the curve's own shape: each
    /// chord is stroked as a rectangle whose ends point across the curve, and at <paramref name="halfWidth"/> from it a turn of a chord's angle moves that end by the half width
    /// times the angle, so the curve is cut finely enough that this stays within <paramref name="tolerance"/> too.
    /// </summary>
    public static FlatPath ForStroke(GraphicsPath path, double tolerance, double halfWidth = 0)
    {
        const double Hair = 1e-3;
        var flat = new FlatPath();
        foreach (var contour in path.GetCurveContours())
        {
            flat.MoveTo(contour.Start.X, contour.Start.Y);
            var (px, py) = (contour.Start.X, contour.Start.Y);
            for (var i = 0; i < contour.Commands.Count; i++)
            {
                var c = contour.Commands[i];
                if (c.Kind != PathCommandKind.Cubic)
                {
                    flat.LineTo(c.End.X, c.End.Y);
                }
                else
                {
                    var (x0, y0) = (px, py);
                    if (!contour.Closed && i == 0)
                        AddPointAt(flat.Contours, x0, y0, c, Hair);

                    AddCubic(flat.Contours, x0, y0, c.Control1.X, c.Control1.Y, c.Control2.X, c.Control2.Y, c.End.X, c.End.Y, tolerance, halfWidth, !contour.Closed && i == contour.Commands.Count - 1 ? Hair : 0);
                }

                (px, py) = (c.End.X, c.End.Y);
            }

            if (contour.Closed)
                flat.Close();
        }

        return flat;
    }

    private static void AddPointAt(PolygonSet set, double x0, double y0, PathCommand c, double t)
    {
        var mt = 1 - t;
        var x = mt * mt * mt * x0 + 3 * mt * mt * t * c.Control1.X + 3 * mt * t * t * c.Control2.X + t * t * t * c.End.X;
        var y = mt * mt * mt * y0 + 3 * mt * mt * t * c.Control1.Y + 3 * mt * t * t * c.Control2.Y + t * t * t * c.End.Y;
        if (Math.Abs(x - x0) + Math.Abs(y - y0) > 1e-9)
            set.Add(x, y);
    }

    private static void AddCubic(PolygonSet set, double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, double tolerance, double halfWidth = 0, double hairBeforeEnd = 0)
    {
        // Uniform parameter subdivision; the segment count bounds the deviation by the curve's second
        // differences (Wang's formula), so it needs no recursion and is fully deterministic.
        var ddx = Math.Max(Math.Abs(x0 - 2 * x1 + x2), Math.Abs(x1 - 2 * x2 + x3));
        var ddy = Math.Max(Math.Abs(y0 - 2 * y1 + y2), Math.Abs(y1 - 2 * y2 + y3));
        var dd = Math.Sqrt(ddx * ddx + ddy * ddy);
        var n = (int)Math.Ceiling(Math.Sqrt(0.75 * dd / tolerance));
        var limit = 500;

        // The curve's turn from its first direction to its last, taken from the control polygon, spread so that no chord turns more than 2 * tolerance / halfWidth.
        if (halfWidth > tolerance)
        {
            var (ax, ay) = Math.Abs(x1 - x0) + Math.Abs(y1 - y0) > 1e-12 ? (x1 - x0, y1 - y0) : (x2 - x0, y2 - y0);
            var (bx, by) = Math.Abs(x3 - x2) + Math.Abs(y3 - y2) > 1e-12 ? (x3 - x2, y3 - y2) : (x3 - x1, y3 - y1);
            var turn = Math.Abs(Math.Atan2(ax * by - ay * bx, ax * bx + ay * by));
            var steps = (int)Math.Min(Math.Ceiling(turn * halfWidth / (2 * tolerance)), 4000);
            if (steps > n) n = steps;
            limit = 4000;
        }

        if (n < 1) n = 1;
        else if (n > limit) n = limit;

        for (var s = 1; s <= n; s++)
        {
            var t = (double)s / n;
            if (s == n && hairBeforeEnd > 0)
            {
                var u = 1 - hairBeforeEnd;
                var (ua, ub, uc, ud) = (hairBeforeEnd * hairBeforeEnd * hairBeforeEnd, 3 * hairBeforeEnd * hairBeforeEnd * u, 3 * hairBeforeEnd * u * u, u * u * u);
                var (hx, hy) = (ua * x0 + ub * x1 + uc * x2 + ud * x3, ua * y0 + ub * y1 + uc * y2 + ud * y3);
                if (Math.Abs(hx - x3) + Math.Abs(hy - y3) > 1e-9)
                    set.Add(hx, hy);
            }

            var mt = 1 - t;
            var a = mt * mt * mt;
            var b = 3 * mt * mt * t;
            var c = 3 * mt * t * t;
            var d = t * t * t;
            set.Add(a * x0 + b * x1 + c * x2 + d * x3, a * y0 + b * y1 + c * y2 + d * y3);
        }
    }
}

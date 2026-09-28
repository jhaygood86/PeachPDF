using PeachDrawing.Abstractions;
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
    /// <c>GraphicsPathAdapter</c>'s wrapped <c>XGraphicsPath</c> directly.
    /// </summary>
    public static FlatPath From(GraphicsPath path, double tolerance)
    {
        var flat = new FlatPath();
        foreach (var contour in path.Flatten(tolerance))
            flat.AddContour(contour.Points.Select(p => (p.X, p.Y)).ToArray(), contour.Closed);

        return flat;
    }

    private static void AddCubic(PolygonSet set, double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, double tolerance)
    {
        // Uniform parameter subdivision; the segment count bounds the deviation by the curve's second
        // differences (Wang's formula), so it needs no recursion and is fully deterministic.
        var ddx = Math.Max(Math.Abs(x0 - 2 * x1 + x2), Math.Abs(x1 - 2 * x2 + x3));
        var ddy = Math.Max(Math.Abs(y0 - 2 * y1 + y2), Math.Abs(y1 - 2 * y2 + y3));
        var dd = Math.Sqrt(ddx * ddx + ddy * ddy);
        var n = (int)Math.Ceiling(Math.Sqrt(0.75 * dd / tolerance));
        if (n < 1) n = 1;
        else if (n > 500) n = 500;

        for (var s = 1; s <= n; s++)
        {
            var t = (double)s / n;
            var mt = 1 - t;
            var a = mt * mt * mt;
            var b = 3 * mt * mt * t;
            var c = 3 * mt * t * t;
            var d = t * t * t;
            set.Add(a * x0 + b * x1 + c * x2 + d * x3, a * y0 + b * y1 + c * y2 + d * y3);
        }
    }
}

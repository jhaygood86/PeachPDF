using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;

namespace PeachPDF.Raster;

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
    /// Flattens <paramref name="path"/> (points in user space) into polylines. <paramref name="tolerance"/>
    /// is the maximum distance, in the path's own units, a flattened segment may deviate from the curve.
    /// </summary>
    public static FlatPath From(XGraphicsPath path, double tolerance)
    {
        var flat = new FlatPath();
        var points = path._corePath.PathPointsSpan;
        var types = path._corePath.PathTypesSpan;

        if (tolerance <= 0 || double.IsNaN(tolerance))
            tolerance = 0.1;

        var hasContour = false;
        var i = 0;
        while (i < points.Length)
        {
            var type = types[i] & 0x07;
            var closes = (types[i] & 0x80) != 0;

            if (type == 0)
            {
                flat.Begin(points[i].X, points[i].Y);
                hasContour = true;
                i++;
                if (closes)
                {
                    flat.MarkClosed();
                    hasContour = false;
                }

                continue;
            }

            if (!hasContour)
            {
                // A segment with no preceding start (should not happen): begin at its own point.
                flat.Begin(points[i].X, points[i].Y);
                hasContour = true;
                if (type != 3)
                {
                    i++;
                    if (closes)
                    {
                        flat.MarkClosed();
                        hasContour = false;
                    }

                    continue;
                }
            }

            if (type == 3)
            {
                if (i + 2 >= points.Length)
                    break;

                var (px, py) = flat.LastPoint();
                AddCubic(flat.Contours, px, py,
                    points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y, points[i + 2].X, points[i + 2].Y, tolerance);
                closes = (types[i + 2] & 0x80) != 0;
                i += 3;
            }
            else
            {
                flat.Contours.Add(points[i].X, points[i].Y);
                i++;
            }

            if (closes)
            {
                flat.MarkClosed();
                hasContour = false;
            }
        }

        return flat;
    }

    private (double X, double Y) LastPoint()
    {
        var (_, end) = Contours.GetContour(Contours.ContourCount - 1);
        return Contours.GetPoint(end - 1);
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

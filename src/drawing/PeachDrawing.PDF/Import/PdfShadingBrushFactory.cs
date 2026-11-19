using System;
using System.Collections.Generic;
using System.Numerics;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// Turns an axial (type 2) or radial (type 3) shading into a canvas gradient brush. The shading's color function is
/// sampled into gradient stops; a shading that does not extend past its ends paints as if it did (the brush has no
/// notion of "stop painting here"), and the other shading types (function-based, meshes) are not read yet.
/// </summary>
internal static class PdfShadingBrushFactory
{
    const int Samples = 48;

    /// <summary>
    /// Makes the brush for <paramref name="shading"/>. <paramref name="toCurrent"/> carries shading space into the
    /// canvas' current user space (the pattern matrix, the stream's base space and the inverse of the current transform).
    /// <paramref name="region"/> is where the shading paints, in that space, when its <c>/Extend</c> stops it short of everywhere (PDF 8.7.4.5.3
    /// and 8.7.4.5.4); null when it extends without limit. A gradient brush always pads its end colors, so the caller clips to it.
    /// </summary>
    public static Brush? CreateBrush(PdfSourceDocument doc, PdfDictionary shading, PdfDictionary? resources, Matrix3x2 toCurrent,
        double alpha, RenderContext context, out string? why, out IReadOnlyList<PaintPoint>? region)
    {
        why = null;
        region = null;
        int type = doc.IntOf(shading.GetRaw("ShadingType"));
        if (type is not (2 or 3)) { why = "shading type " + type; return null; }

        var space = PdfColorSpace.Resolve(doc, shading.GetRaw("ColorSpace"), resources);
        if (space is null) { why = "shading colour space"; return null; }
        var function = PdfFunction.Load(doc, shading.GetRaw("Function"));
        if (function is null) { why = "shading function"; return null; }
        if (doc.ArrayOf(shading, "Coords") is not { } coords || coords.Elements.Count < (type == 2 ? 4 : 6)) { why = "shading coordinates"; return null; }

        double t0 = 0, t1 = 1;
        if (doc.ArrayOf(shading, "Domain") is { Elements.Count: >= 2 } dom) { t0 = doc.NumberOf(dom.Elements[0]); t1 = doc.NumberOf(dom.Elements[1]); }

        var stops = SampleStops(function, space, t0, t1, alpha);
        double C(int i) => doc.NumberOf(coords.Elements[i]);

        bool extend0 = false, extend1 = false;
        if (doc.ArrayOf(shading, "Extend") is { Elements.Count: >= 2 } extend)
        {
            extend0 = doc.Resolve(extend.Elements[0]) is PdfBoolean { Value: true };
            extend1 = doc.Resolve(extend.Elements[1]) is PdfBoolean { Value: true };
        }

        if (type == 2)
        {
            if (!extend0 || !extend1) region = AxialRegion(C(0), C(1), C(2), C(3), extend0, extend1, toCurrent);
            var start = Transform(C(0), C(1), toCurrent);
            var end = Transform(C(2), C(3), toCurrent);
            return context.GetLinearGradientBrush(start, end, stops);
        }

        // Radial: the end circle is the gradient's outer ellipse, the start circle its focus. The matrix carries the circles
        // (which a pair of radii cannot skew or rotate) into the current space.
        double r0 = C(2), r1 = C(5);
        if (r1 <= 0 && r0 <= 0) { why = "empty radial shading"; return null; }
        if (!extend0 || !extend1) region = RadialRegion(C(0), C(1), r0, C(3), C(4), r1, extend0, extend1, toCurrent);
        // The start circle can be the larger one: then the gradient runs inward, so swap the circles and reverse the stops.
        if (r0 > r1)
        {
            (r0, r1) = (r1, r0);
            var (x0, y0, x1, y1) = (C(0), C(1), C(3), C(4));
            var flipped = new (PaintColor, double)[stops.Length];
            for (int i = 0; i < stops.Length; i++) flipped[i] = (stops[stops.Length - 1 - i].PaintColor, 1 - stops[stops.Length - 1 - i].Position);
            return context.GetRadialGradientBrush(new PaintPoint(x0, y0), r1, r1, flipped, false, new PaintPoint(x1, y1), r0, toCurrent);
        }
        return context.GetRadialGradientBrush(new PaintPoint(C(3), C(4)), r1, r1, stops, false, new PaintPoint(C(0), C(1)), r0, toCurrent);
    }

    /// <summary>How far, in current-space units, a region reaches where the shading is not limited: past any page.</summary>
    const double Reach = 1e5;

    /// <summary>
    /// Where an axial shading paints: the band between the lines through its end points at right angles to the axis, open on the side
    /// that extends. The lines are right angles in shading space, so their directions are carried over by the matrix, not recomputed.
    /// </summary>
    static PaintPoint[]? AxialRegion(double x0, double y0, double x1, double y1, bool extend0, bool extend1, Matrix3x2 toCurrent)
    {
        double dx = x1 - x0, dy = y1 - y0;
        if (dx == 0 && dy == 0) return null;
        var a = Vector2.Transform(new Vector2((float)x0, (float)y0), toCurrent);
        var b = Vector2.Transform(new Vector2((float)x1, (float)y1), toCurrent);
        var across = Vector2.TransformNormal(new Vector2((float)-dy, (float)dx), toCurrent);
        var axis = b - a;
        if (across.LengthSquared() == 0 || axis.LengthSquared() == 0) return null;
        across = Vector2.Normalize(across) * (float)Reach;
        var along = Vector2.Normalize(axis) * (float)Reach;
        var start = extend0 ? a - along : a;
        var end = extend1 ? b + along : b;
        return [new(start.X - across.X, start.Y - across.Y), new(end.X - across.X, end.Y - across.Y), new(end.X + across.X, end.Y + across.Y), new(start.X + across.X, start.Y + across.Y)];
    }

    /// <summary>
    /// Where a radial shading paints: the circles for t from 0 to 1 sweep the convex hull of the first and last, and a side that extends
    /// adds the circles beyond it, which shrink to a point when the radius falls that way. An extension that grows without limit is not
    /// clipped (null), so the page gets the whole gradient there.
    /// </summary>
    static PaintPoint[]? RadialRegion(double x0, double y0, double r0, double x1, double y1, double r1, bool extend0, bool extend1, Matrix3x2 toCurrent)
    {
        double ta = 0, tb = 1;
        double dr = r1 - r0;
        if (extend0) { if (dr > 0) ta = -r0 / dr; else return null; }
        if (extend1) { if (dr < 0) tb = -r0 / dr; else return null; }
        var circles = new List<(double X, double Y, double R)>
        {
            (x0 + ta * (x1 - x0), y0 + ta * (y1 - y0), Math.Max(0, r0 + ta * dr)),
            (x0 + tb * (x1 - x0), y0 + tb * (y1 - y0), Math.Max(0, r0 + tb * dr)),
        };
        const int Steps = 128;
        var points = new List<Vector2>(circles.Count * Steps);
        foreach (var (cx, cy, r) in circles)
            for (int i = 0; i < Steps; i++)
            {
                double angle = 2 * Math.PI * i / Steps;
                points.Add(Vector2.Transform(new Vector2((float)(cx + r * Math.Cos(angle)), (float)(cy + r * Math.Sin(angle))), toCurrent));
            }

        return [.. System.Linq.Enumerable.Select(ConvexHull(points), p => new PaintPoint(p.X, p.Y))];
    }

    /// <summary>The convex hull of <paramref name="points"/> (Andrew's monotone chain), counter-clockwise.</summary>
    static List<Vector2> ConvexHull(List<Vector2> points)
    {
        points.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var hull = new List<Vector2>(points.Count * 2);
        foreach (var p in points)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], p) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }

        int lower = hull.Count + 1;
        for (int i = points.Count - 2; i >= 0; i--)
        {
            while (hull.Count >= lower && Cross(hull[^2], hull[^1], points[i]) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(points[i]);
        }

        hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    static PaintPoint Transform(double x, double y, Matrix3x2 m)
    {
        var p = Vector2.Transform(new Vector2((float)x, (float)y), m);
        return new PaintPoint(p.X, p.Y);
    }

    static (PaintColor PaintColor, double Position)[] SampleStops(PdfFunction function, PdfColorSpace space, double t0, double t1, double alpha)
    {
        var positions = new SortedSet<double>();
        for (int i = 0; i <= Samples; i++) positions.Add(i / (double)Samples);
        double span = t1 - t0;
        foreach (var bound in function.Bounds)
        {
            if (span == 0) break;
            double p = (bound - t0) / span;
            if (p <= 0 || p >= 1) continue;
            positions.Add(p);
            positions.Add(p - 1e-6); // a stitching function may jump at a bound: keep both sides
        }
        var stops = new List<(PaintColor, double)>(positions.Count);
        foreach (double p in positions)
        {
            var values = function.Evaluate(t0 + p * span);
            // A function may return more or fewer components than the space wants; pad or cut rather than fail.
            var comps = new double[space.Components];
            for (int i = 0; i < comps.Length; i++) comps[i] = i < values.Length ? values[i] : 0;
            stops.Add((space.ToColor(comps, alpha), p));
        }
        return [.. stops];
    }
}

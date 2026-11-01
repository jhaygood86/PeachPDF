using System;
using System.Collections.Generic;
using System.Numerics;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// Turns an axial (type 2) or radial (type 3) shading into a canvas gradient brush. The shading's colour function is
/// sampled into gradient stops; a shading that does not extend past its ends paints as if it did (the brush has no
/// notion of "stop painting here"), and the other shading types (function-based, meshes) are not read yet.
/// </summary>
internal static class PdfShading
{
    const int Samples = 48;

    /// <summary>
    /// Makes the brush for <paramref name="shading"/>. <paramref name="toCurrent"/> carries shading space into the
    /// canvas' current user space (the pattern matrix, the stream's base space and the inverse of the current transform).
    /// </summary>
    public static Brush? CreateBrush(PdfSourceDocument doc, PdfDictionary shading, PdfDictionary? resources, Matrix3x2 toCurrent,
        double alpha, RenderContext context, out string? why)
    {
        why = null;
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

        if (type == 2)
        {
            var start = Transform(C(0), C(1), toCurrent);
            var end = Transform(C(2), C(3), toCurrent);
            return context.GetLinearGradientBrush(start, end, stops);
        }

        // Radial: the end circle is the gradient's outer ellipse, the start circle its focus. The matrix carries the circles
        // (which a pair of radii cannot skew or rotate) into the current space.
        double r0 = C(2), r1 = C(5);
        if (r1 <= 0 && r0 <= 0) { why = "empty radial shading"; return null; }
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
        return stops.ToArray();
    }
}

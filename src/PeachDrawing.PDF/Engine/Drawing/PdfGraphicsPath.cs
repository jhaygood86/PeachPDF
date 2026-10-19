using PeachDrawing.Core;
using PeachDrawing.Core.Geometry;
using System;
using System.Collections.Generic;

namespace PeachDrawing.PDF.Engine.Drawing;

/// <summary>Path building the PDF layer needs beyond <see cref="GraphicsPath"/>'s own operations.</summary>
internal static class PdfPathExtensions
{
    /// <summary>Adds a closed rectangle, drawn from its top-left corner clockwise.</summary>
    public static void AddRectangle(this GraphicsPath path, Rect rect)
    {
        path.Start(rect.X, rect.Y);
        path.LineTo(rect.X + rect.Width, rect.Y);
        path.LineTo(rect.X + rect.Width, rect.Y + rect.Height);
        path.LineTo(rect.X, rect.Y + rect.Height);
        path.CloseFigure();
    }
}

/// <summary>
/// The PDF layer's <see cref="GraphicsPath"/>: the contours <see cref="GraphicsPath"/> already records, plus the fill rule
/// the PDF writer paints the path with and a rectangle clip that works on the flattened outline.
/// </summary>
internal sealed class PdfGraphicsPath : GraphicsPath
{
    /// <summary>
    /// The fill rule. Even-odd is the default, which is not <see cref="FillMode"/>'s own default value
    /// (<see cref="FillMode.Nonzero"/>).
    /// </summary>
    public override FillMode FillMode { get; set; } = FillMode.EvenOdd;

    /// <summary>
    /// The part of the path inside <paramref name="rect"/>, as straight-edged polygons. A rectangle with no area, or a
    /// path that only touches the rectangle's edge, leaves nothing rather than a sliver.
    /// </summary>
    public override GraphicsPath ClipToRect(Rect rect)
    {
        var result = new PdfGraphicsPath { FillMode = FillMode };

        foreach (var contour in GetCurveContours())
        {
            var clipped = PolygonClipper.ClipToRect(Flatten(contour), rect);

            // Fewer than 3 points can't enclose any area at all. That alone isn't sufficient, though: clipping against a
            // degenerate (zero-width or zero-height) rectangle - or a subject edge that runs exactly along a clip boundary -
            // can leave 3+ points that are all collinear (every vertex pinned to the same clip edge), which is still zero
            // area. The shoelace-formula check catches that case too, so a degenerate rectangle never fabricates a visible
            // sliver of "clipped" geometry from nothing.
            if (clipped.Count < 3 || IsNegligibleArea(clipped))
                continue;

            result.Start(clipped[0].X, clipped[0].Y);
            for (int i = 1; i < clipped.Count; i++)
                result.LineTo(clipped[i].X, clipped[i].Y);
            result.CloseFigure();
        }

        return result;
    }

    public override void Dispose()
    { }

    /// <summary>A contour's outline as line segments, curves subdivided to within a tenth of a point.</summary>
    private static List<PaintPoint> Flatten(CurveContour contour)
    {
        var points = new List<PaintPoint> { contour.Start };
        foreach (var command in contour.Commands)
        {
            if (command.Kind == PathCommandKind.Cubic)
                BezierFlattener.Flatten(points[^1], command.Control1, command.Control2, command.End, points);
            else
                points.Add(command.End);
        }

        return points;
    }

    private static bool IsNegligibleArea(List<PaintPoint> polygon)
    {
        double area = 0;
        int n = polygon.Count;
        for (int i = 0; i < n; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % n];
            area += a.X * b.Y - b.X * a.Y;
        }

        return Math.Abs(area) < 1e-6;
    }
}

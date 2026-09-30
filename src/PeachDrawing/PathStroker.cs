using PeachDrawing.Core;
using System;

namespace PeachDrawing;

/// <summary>
/// Turns the line a <see cref="Pen"/> would draw along a path into an ordinary outline, so the stroke can be filled, clipped to,
/// measured, or combined with other geometry like any other shape.
/// </summary>
public static class PathStroker
{
    /// <summary>
    /// Adds to <paramref name="destination"/> the area a stroke of <paramref name="path"/> with <paramref name="pen"/> covers:
    /// the pen's width, caps, joins, miter limit and dashes all apply. The outline is made of straight segments (round caps and
    /// joins are flattened) and is meant to be filled with the <see cref="FillMode.Nonzero"/> rule, which is set on
    /// <paramref name="destination"/>; where dashes, joins or overlapping parts of the path make pieces overlap, that rule
    /// fills their union.
    /// </summary>
    /// <param name="path">the path to stroke</param>
    /// <param name="pen">the pen to stroke it with; only its geometry (width, caps, joins, miter limit, dashes) is used, not its paint</param>
    /// <param name="destination">the path the outline is added to; create it with <see cref="Canvas.GetGraphicsPath"/> to draw it with that canvas</param>
    /// <param name="tolerance">how far the flattened outline may stray from the true curves, in the path's own units</param>
    /// <exception cref="ArgumentNullException">an argument is <see langword="null"/></exception>
    /// <remarks>
    /// A pen whose width is zero or less strokes nothing here: on a canvas that draws such a pen as a one-pixel hairline, the
    /// hairline's width depends on the device, and an outline has none.
    /// </remarks>
    public static void Stroke(GraphicsPath path, Pen pen, GraphicsPath destination, double tolerance = 0.1)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(pen);
        ArgumentNullException.ThrowIfNull(destination);

        destination.FillMode = FillMode.Nonzero;
        if (!(pen.Width > 0))
            return;

        if (tolerance <= 0 || double.IsNaN(tolerance))
            tolerance = 0.1;

        var style = new StrokeStyle(
            pen.Width,
            pen.LineCap switch { LineCap.Round => StrokeCap.Round, LineCap.Square => StrokeCap.Square, _ => StrokeCap.Butt },
            pen.LineJoin switch { LineJoin.Round => StrokeJoin.Round, LineJoin.Bevel => StrokeJoin.Bevel, _ => StrokeJoin.Miter },
            pen.MiterLimit,
            RasterCanvas.ResolveDashes(pen, pen.Width),
            pen.DashStyle == DashStyle.Custom ? pen.DashOffset : 0);

        var polygons = new PolygonSet();
        Stroker.Stroke(FlatPath.From(path, tolerance), style, Affine.Identity, polygons);
        polygons.NormalizeWinding();

        for (var i = 0; i < polygons.ContourCount; i++)
        {
            var (start, end) = polygons.GetContour(i);
            if (end - start < 3)
                continue;

            var (x, y) = polygons.GetPoint(start);
            destination.Start(x, y);
            for (var p = start + 1; p < end; p++)
            {
                (x, y) = polygons.GetPoint(p);
                destination.LineTo(x, y);
            }

            destination.CloseFigure();
        }
    }
}

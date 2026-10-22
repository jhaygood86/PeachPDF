using PeachDrawing.Core;
using PeachDrawing.Core.Geometry;
using System.Collections.Generic;

namespace PeachDrawing.Svg;

/// <summary>
/// The <see cref="GraphicsPath"/> <see cref="SvgCanvas.GetGraphicsPath"/> hands out. The base class already records the path's curves
/// (arcs converted to cubic Béziers), which is exactly what an SVG <c>d</c> attribute is written from, so this type only supplies the
/// members the base leaves abstract.
/// </summary>
internal sealed class SvgGraphicsPath : GraphicsPath
{
    public override FillMode FillMode { get; set; }

    /// <summary>Flattens the path, clips each contour's polygon to <paramref name="rect"/> and seeds a new path from the (line-only) result.</summary>
    public override GraphicsPath ClipToRect(Rect rect)
    {
        var clippedContours = new List<(IReadOnlyList<(double X, double Y)> Points, bool Closed)>();
        foreach (var contour in Flatten(0.1))
        {
            var clipped = PolygonClipper.ClipToRect(contour.Points, rect);
            if (clipped.Count > 0)
                clippedContours.Add((clipped.ConvertAll(p => (p.X, p.Y)), contour.Closed));
        }

        var result = new SvgGraphicsPath { FillMode = FillMode };
        result.SeedFlatContours(clippedContours);
        return result;
    }

    public override void Dispose()
    {
    }
}

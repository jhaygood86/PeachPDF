using PeachDrawing.Core;
using PeachDrawing.Core.Geometry;
using System.Collections.Generic;

namespace PeachDrawing.Skia;

/// <summary>
/// A path as the layout and paint code builds it: contours recorded by the <see cref="GraphicsPath"/> base class. It holds nothing native;
/// the canvas turns it into an <c>SKPath</c> for the length of one draw and releases that straight away.
/// </summary>
internal sealed class SkiaGraphicsPath : GraphicsPath
{
    public override FillMode FillMode { get; set; }

    public override GraphicsPath ClipToRect(Rect rect)
    {
        var clippedContours = new List<(IReadOnlyList<(double X, double Y)> Points, bool Closed)>();
        foreach (var contour in Flatten(0.1))
        {
            var clipped = PolygonClipper.ClipToRect(contour.Points, rect);
            if (clipped.Count > 0)
                clippedContours.Add((clipped.ConvertAll(p => (p.X, p.Y)), contour.Closed));
        }

        var result = new SkiaGraphicsPath { FillMode = FillMode };
        result.SeedFlatContours(clippedContours);
        return result;
    }

    public override void Dispose()
    {
    }
}

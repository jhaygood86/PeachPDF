using PeachDrawing.Core;
using System.Collections.Generic;

namespace PeachDrawing;

/// <summary>
/// The <see cref="GraphicsPath"/> <see cref="RasterCanvas.GetGraphicsPath"/> hands out. Every recorder
/// method (<see cref="GraphicsPath.Start"/>, <see cref="GraphicsPath.LineTo"/>, etc.) and
/// <see cref="GraphicsPath.Flatten"/> already have a complete, backend-agnostic base implementation -
/// this type exists only to supply the three genuinely abstract members, unlike the PDF backend's own
/// <c>GraphicsPathAdapter</c>, which also mirrors every call into a live <c>XGraphicsPath</c> for PDF
/// content-stream writing. A raster canvas has no content stream, so there is nothing to mirror.
/// </summary>
internal sealed class RasterGraphicsPath : GraphicsPath
{
    public override FillMode FillMode { get; set; }

    /// <summary>
    /// Flattens every curve (tolerance chosen for on-screen geometry, not a device-scale-derived one -
    /// the one caller of this, vertical-text cell clipping, needs correct shape rather than a specific
    /// pixel-perfect fit), clips each contour's polygon to <paramref name="rect"/> (Sutherland-Hodgman),
    /// and seeds a new path from the (now line-only) result.
    /// </summary>
    public override GraphicsPath ClipToRect(Rect rect)
    {
        var clippedContours = new List<(IReadOnlyList<(double X, double Y)> Points, bool Closed)>();
        foreach (var contour in Flatten(0.1))
        {
            var clipped = SutherlandHodgman.ClipToRect(contour.Points, rect);
            if (clipped.Count > 0)
                clippedContours.Add((clipped, contour.Closed));
        }

        var result = new RasterGraphicsPath { FillMode = FillMode };
        result.SeedFlatContours(clippedContours);
        return result;
    }

    public override void Dispose()
    {
    }
}

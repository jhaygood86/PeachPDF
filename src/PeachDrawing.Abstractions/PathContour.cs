// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
//
// - Sun Tsu,
// "The Art of War"

using System.Collections.Generic;

namespace PeachDrawing.Abstractions
{
    /// <summary>
    /// One flattened subpath: the polyline <see cref="GraphicsPath.Flatten"/> reduced a contour to, and
    /// whether that contour was explicitly closed (<see cref="GraphicsPath.CloseFigure"/>) - an open
    /// subpath gets caps where a closed one gets a join, so a stroker needs this distinction, not just
    /// the points.
    /// </summary>
    public sealed record PathContour(IReadOnlyList<PaintPoint> Points, bool Closed);
}

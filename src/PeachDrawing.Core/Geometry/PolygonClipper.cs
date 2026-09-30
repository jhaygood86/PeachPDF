using System;
using System.Collections.Generic;

namespace PeachDrawing.Core.Geometry
{
    /// <summary>
    /// Clips polygons made of straight edges against convex windows.
    /// </summary>
    public static class PolygonClipper
    {
        /// <summary>
        /// Clips a closed polygon to an axis-aligned rectangle, one edge of the rectangle at a time. The polygon may be convex,
        /// concave or self-intersecting: what makes each edge's pass exact is that the <em>window</em> is convex, because
        /// clipping against a convex window can only shorten an original edge or add a piece of the window's own boundary. So
        /// each contour of a multi-contour outline can be clipped on its own and the results filled together under the same fill
        /// rule, without a general polygon-against-polygon boolean.
        /// </summary>
        /// <param name="polygon">the polygon's vertices in order; the last vertex is joined back to the first</param>
        /// <param name="rect">the window to clip to</param>
        /// <returns>
        /// the clipped polygon's vertices. Fewer than three vertices means nothing with area is left inside the window
        /// (the polygon lies outside it, or was degenerate to begin with); a result of three or more vertices can still enclose
        /// no area when they are all collinear, as when an edge runs exactly along the window's boundary.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="polygon"/> is <see langword="null"/></exception>
        public static List<PaintPoint> ClipToRect(IReadOnlyList<PaintPoint> polygon, Rect rect)
        {
            ArgumentNullException.ThrowIfNull(polygon);

            var stage = new List<PaintPoint>(polygon);

            stage = ClipEdge(stage, p => p.X >= rect.Left, (a, b) => new PaintPoint(rect.Left, InterpolateY(a, b, rect.Left)));
            if (stage.Count < 3)
                return stage;

            stage = ClipEdge(stage, p => p.X <= rect.Right, (a, b) => new PaintPoint(rect.Right, InterpolateY(a, b, rect.Right)));
            if (stage.Count < 3)
                return stage;

            stage = ClipEdge(stage, p => p.Y >= rect.Top, (a, b) => new PaintPoint(InterpolateX(a, b, rect.Top), rect.Top));
            if (stage.Count < 3)
                return stage;

            return ClipEdge(stage, p => p.Y <= rect.Bottom, (a, b) => new PaintPoint(InterpolateX(a, b, rect.Bottom), rect.Bottom));
        }

        /// <summary>
        /// One pass against a single half-plane: walks the polygon's edges (including the implicit edge back to the first
        /// vertex) and keeps the part of each that is on the inside, adding a point wherever an edge crosses the boundary.
        /// </summary>
        private static List<PaintPoint> ClipEdge(List<PaintPoint> polygon, Func<PaintPoint, bool> inside, Func<PaintPoint, PaintPoint, PaintPoint> intersect)
        {
            var output = new List<PaintPoint>(polygon.Count);
            if (polygon.Count == 0)
                return output;

            var previous = polygon[^1];
            var previousInside = inside(previous);

            foreach (var current in polygon)
            {
                var currentInside = inside(current);

                if (currentInside)
                {
                    if (!previousInside)
                        output.Add(intersect(previous, current));

                    output.Add(current);
                }
                else if (previousInside)
                {
                    output.Add(intersect(previous, current));
                }

                previous = current;
                previousInside = currentInside;
            }

            return output;
        }

        private static double InterpolateY(PaintPoint a, PaintPoint b, double x)
        {
            var dx = b.X - a.X;
            if (dx == 0)
                return a.Y;

            return a.Y + (x - a.X) / dx * (b.Y - a.Y);
        }

        private static double InterpolateX(PaintPoint a, PaintPoint b, double y)
        {
            var dy = b.Y - a.Y;
            if (dy == 0)
                return a.X;

            return a.X + (y - a.Y) / dy * (b.X - a.X);
        }
    }
}

using System.Collections.Generic;

namespace PeachPDF.PdfSharpCore.Drawing
{
    /// <summary>
    /// Clips a closed, line-segment-only polygon to an axis-aligned rectangle - the geometry primitive
    /// <see cref="CoreGraphicsPath.ClipToRect"/> needs once a contour's curves have already been
    /// flattened by <see cref="BezierFlattener"/>. Sutherland-Hodgman is normally introduced for a convex
    /// *subject* polygon, but the property this class actually relies on is that the *clip* window is
    /// convex: clipping any polygon (convex, concave, or self-intersecting, as an unusual glyph contour
    /// could be) against a convex window can never introduce a new self-intersection the algorithm isn't
    /// already equipped to handle, because each output edge is either an original edge (possibly
    /// shortened) or a piece of the convex window's own boundary. That is what makes clipping each
    /// contour of a multi-contour, non-zero-wound glyph outline independently - then re-adding every
    /// clipped contour to the same output path under the same fill mode - a correct way to compute the
    /// outline's intersection with the rectangle, without a general two-arbitrary-polygon boolean.
    /// </summary>
    internal static class SutherlandHodgman
    {
        /// <summary>
        /// Returns <paramref name="polygon"/> clipped to [<paramref name="left"/>, <paramref name="top"/>]-
        /// [<paramref name="right"/>, <paramref name="bottom"/>], by clipping against each of the
        /// rectangle's four half-planes in turn. An empty or degenerate (fewer than 3 vertices) result
        /// means the polygon lies entirely outside the rectangle (or the input was already degenerate).
        /// </summary>
        public static List<XPoint> ClipToRect(List<XPoint> polygon, double left, double top, double right, double bottom)
        {
            var stage = polygon;
            stage = ClipEdge(stage, p => p.X >= left, (a, b) => new XPoint(left, LerpY(a, b, left)));
            if (stage.Count < 3) return stage;

            stage = ClipEdge(stage, p => p.X <= right, (a, b) => new XPoint(right, LerpY(a, b, right)));
            if (stage.Count < 3) return stage;

            stage = ClipEdge(stage, p => p.Y >= top, (a, b) => new XPoint(LerpX(a, b, top), top));
            if (stage.Count < 3) return stage;

            stage = ClipEdge(stage, p => p.Y <= bottom, (a, b) => new XPoint(LerpX(a, b, bottom), bottom));
            return stage;
        }

        /// <summary>
        /// One Sutherland-Hodgman pass against a single half-plane: walks <paramref name="polygon"/>'s
        /// edges (including the implicit closing edge back to the first vertex) and keeps, for each edge,
        /// the portion(s) of it that lie on the "inside" side of <paramref name="inside"/> - emitting an
        /// <paramref name="intersect"/> point whenever an edge crosses the boundary.
        /// </summary>
        private static List<XPoint> ClipEdge(List<XPoint> polygon, System.Func<XPoint, bool> inside, System.Func<XPoint, XPoint, XPoint> intersect)
        {
            var output = new List<XPoint>(polygon.Count);
            var count = polygon.Count;
            if (count == 0) return output;

            var previous = polygon[count - 1];
            var previousInside = inside(previous);

            foreach (var current in polygon)
            {
                var currentInside = inside(current);

                if (currentInside)
                {
                    if (!previousInside) output.Add(intersect(previous, current));
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

        private static double LerpY(XPoint a, XPoint b, double x)
        {
            var dx = b.X - a.X;
            if (dx == 0) return a.Y;
            var t = (x - a.X) / dx;
            return a.Y + t * (b.Y - a.Y);
        }

        private static double LerpX(XPoint a, XPoint b, double y)
        {
            var dy = b.Y - a.Y;
            if (dy == 0) return a.X;
            var t = (y - a.Y) / dy;
            return a.X + t * (b.X - a.X);
        }
    }
}

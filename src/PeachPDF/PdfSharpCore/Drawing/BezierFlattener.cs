using System.Collections.Generic;

namespace PeachPDF.PdfSharpCore.Drawing
{
    /// <summary>
    /// Subdivides a cubic Bézier curve into line segments - the step <see cref="CoreGraphicsPath.ClipToRect"/>
    /// needs before a curve-bearing contour (a glyph outline's own curved segments, in particular) can be
    /// clipped against an axis-aligned rectangle via <see cref="SutherlandHodgman"/>, since an arbitrary
    /// rectangle clip of a cubic Bézier is not itself expressible as a cubic Bézier in general.
    /// </summary>
    internal static class BezierFlattener
    {
        /// <summary>
        /// The maximum allowed deviation, in path units (PDF points, un-scaled by <c>PixelsPerPoint</c> -
        /// the same space <see cref="CoreGraphicsPath"/> already stores its points in), between the
        /// flattened line segments and the true curve. Sub-tenth-of-a-point deviation is invisible for a
        /// filled text-clip shape - the only consumer of this flattener - so this is chosen for cheap,
        /// good-enough fidelity rather than print-quality curve rendering.
        /// </summary>
        private const double ToleranceSquared = 0.01; // 0.1pt

        /// <summary>
        /// The recursion depth ceiling - 2^12 possible segments is far more than any glyph curve needs
        /// even under this tolerance, and bounds the cost of a pathological (near-cusp) control-point
        /// configuration that would otherwise keep bisecting without visibly flattening.
        /// </summary>
        private const int MaxDepth = 12;

        /// <summary>
        /// Appends the flattened line-segment approximation of the cubic Bézier from <paramref name="p0"/>
        /// (not itself re-added - the caller's contour already ends at <paramref name="p0"/>) through
        /// control points <paramref name="p1"/>/<paramref name="p2"/> to <paramref name="p3"/>, to
        /// <paramref name="output"/>.
        /// </summary>
        public static void Flatten(XPoint p0, XPoint p1, XPoint p2, XPoint p3, List<XPoint> output)
        {
            FlattenRecursive(p0, p1, p2, p3, output, 0);
        }

        private static void FlattenRecursive(XPoint p0, XPoint p1, XPoint p2, XPoint p3, List<XPoint> output, int depth)
        {
            if (depth >= MaxDepth || IsFlatEnough(p0, p1, p2, p3))
            {
                output.Add(p3);
                return;
            }

            // De Casteljau subdivision at t = 0.5.
            var p01 = Mid(p0, p1);
            var p12 = Mid(p1, p2);
            var p23 = Mid(p2, p3);
            var p012 = Mid(p01, p12);
            var p123 = Mid(p12, p23);
            var p0123 = Mid(p012, p123);

            FlattenRecursive(p0, p01, p012, p0123, output, depth + 1);
            FlattenRecursive(p0123, p123, p23, p3, output, depth + 1);
        }

        /// <summary>
        /// Whether both control points already lie close enough to the chord from <paramref name="p0"/>
        /// to <paramref name="p3"/> that a straight line is an adequate stand-in for the curve - the
        /// standard "distance of the control points from the chord" flatness test.
        /// </summary>
        private static bool IsFlatEnough(XPoint p0, XPoint p1, XPoint p2, XPoint p3)
        {
            var dx = p3.X - p0.X;
            var dy = p3.Y - p0.Y;

            var d1 = PerpendicularDistanceSquared(p1, p0, dx, dy);
            var d2 = PerpendicularDistanceSquared(p2, p0, dx, dy);

            return d1 <= ToleranceSquared && d2 <= ToleranceSquared;
        }

        /// <summary>
        /// The squared perpendicular distance from <paramref name="point"/> to the line through
        /// <paramref name="origin"/> with direction (<paramref name="dx"/>, <paramref name="dy"/>) - the
        /// cross-product-over-length-squared formula, kept squared throughout so the hot recursive path
        /// never needs a square root.
        /// </summary>
        private static double PerpendicularDistanceSquared(XPoint point, XPoint origin, double dx, double dy)
        {
            var lengthSquared = dx * dx + dy * dy;
            var px = point.X - origin.X;
            var py = point.Y - origin.Y;

            if (lengthSquared < 1e-12)
            {
                // A degenerate (near-zero-length) chord - fall back to the point's own distance from
                // the shared origin, since "distance from the line" is undefined without a direction.
                return px * px + py * py;
            }

            var cross = px * dy - py * dx;
            return cross * cross / lengthSquared;
        }

        private static XPoint Mid(XPoint a, XPoint b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    }
}

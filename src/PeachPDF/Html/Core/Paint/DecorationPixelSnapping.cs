using PeachDrawing.Core;
using PeachPDF.CSS;

namespace PeachPDF.Html.Core.Paint
{
    /// <summary>
    /// Snaps a box's decoration rectangle to the CSS pixel grid, the way a browser does before it paints a
    /// background or border: each edge is rounded to the nearest whole CSS pixel on its own, so a 1px
    /// border always covers exactly one device pixel of a viewer rendering at 100% zoom instead of
    /// straddling two (which reads as a wider, paler line), and the background meets it exactly instead
    /// of showing past it.
    /// </summary>
    /// <remarks>
    /// Layout stays fractional; only what is painted moves, by at most half a CSS pixel (0.375pt). The
    /// grid is anchored at the sheet's origin, which is where fragmentainer-local coordinates start.
    /// Nothing is snapped under a transform: there the rectangle's position on the device grid is not its
    /// position in layout space, so rounding it would only add error. An offscreen canvas that paints in
    /// the page's own coordinates (an <c>opacity</c>, blend-mode or filter layer, a raster region) snaps
    /// like the page does, so a box inside one agrees with its neighbours; one a translation positions has
    /// a non-identity transform and is skipped like any other.
    /// </remarks>
    internal static class DecorationPixelSnapping
    {
        /// <summary>
        /// Returns <paramref name="rect"/> with every edge rounded to a whole CSS pixel, or
        /// <paramref name="rect"/> itself when snapping does not apply or would collapse an axis. An edge
        /// whose flag is false is left where it is: it is not a real edge of the box but a cut across a
        /// page, column or line break, which belongs to the fragmentainer or line rather than to the box.
        /// </summary>
        internal static Rect Snap(Canvas g, Rect rect,
            bool left = true, bool top = true, bool right = true, bool bottom = true)
        {
            if (!g.CurrentTransform.IsIdentity) return rect;

            var pixel = Length.PointsPerPx * g.PixelsPerPoint;
            if (pixel <= 0) return rect;

            var snappedLeft = left ? SnapEdge(rect.Left, pixel) : rect.Left;
            var snappedTop = top ? SnapEdge(rect.Top, pixel) : rect.Top;
            var snappedRight = right ? SnapEdge(rect.Right, pixel) : rect.Right;
            var snappedBottom = bottom ? SnapEdge(rect.Bottom, pixel) : rect.Bottom;

            return CollapseGuarded(rect, snappedLeft, snappedTop, snappedRight, snappedBottom);
        }

        private static Rect CollapseGuarded(Rect rect, double left, double top, double right, double bottom)
        {
            // A box thinner than a pixel can round both edges onto the same line; keep it visible.
            if (right <= left) (left, right) = (rect.Left, rect.Right);
            if (bottom <= top) (top, bottom) = (rect.Top, rect.Bottom);

            return Rect.FromLTRB(left, top, right, bottom);
        }

        /// <summary>
        /// Returns <paramref name="geometry"/> with its decoration rectangle snapped, so the background,
        /// border, outline, shadows and everything else resolved against it share the same edges. Only the
        /// edges the box really owns are snapped: a cut across a page, column or line break stays where it
        /// is. A sliced box (<see cref="BoxDecorationGeometry.NeedsClip"/>) has two rectangles: the unbroken
        /// strip its decorations are resolved against, every edge of which is the box's own, and the slice
        /// that clips it, whose owned edges are the very same lines and so snap to the same place.
        /// </summary>
        internal static BoxDecorationGeometry Snap(Canvas g, BoxDecorationGeometry geometry)
        {
            if (geometry.NeedsClip)
            {
                return geometry with
                {
                    DecorationRect = Snap(g, geometry.DecorationRect),
                    ClipRect = Snap(g, geometry.ClipRect,
                        geometry.HasLeftEdge, geometry.HasTopEdge, geometry.HasRightEdge, geometry.HasBottomEdge),
                };
            }

            var rect = Snap(g, geometry.DecorationRect,
                geometry.HasLeftEdge, geometry.HasTopEdge, geometry.HasRightEdge, geometry.HasBottomEdge);
            return geometry with { DecorationRect = rect, ClipRect = rect };
        }

        // Rounds half up, with a bias far above float noise (~1e-13) and far below anything layout can mean:
        // two edges that are the same line computed two ways (left + width, a running sum) sit on the same
        // side of a half-pixel boundary instead of splitting into a 1px gap or a doubled border.
        private const double HalfPixelBias = 1e-6;

        private static double SnapEdge(double edge, double pixel) =>
            System.Math.Floor(edge / pixel + 0.5 + HalfPixelBias) * pixel;
    }
}

using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Handlers
{
    /// <summary>
    /// Paints CSS <c>outline</c> - a ring drawn outside the border edge, offset by
    /// <c>outline-offset</c>, that never affects box sizing (CSS Basic User Interface 4 §4). Structured
    /// as a uniform border around the rectangle inflated by <c>outline-offset + outline-width</c>, so
    /// every ordinary outline style delegates to the same neutral <see cref="BoxEdgesDrawHandler"/>
    /// used by uniform borders. This handler only resolves outline-specific behavior before handing it off:
    /// <c>outline-style: auto</c> is
    /// drawn as <c>solid</c> (CSS-UI-4 leaves <c>auto</c>'s actual appearance UA-defined) at the UA's
    /// own width rather than the declared one, which the same section says is ignored - see
    /// <see cref="AutoRingWidth"/>. When the box has <c>border-radius</c>, each outline contour expands
    /// that radius by the same distance as its rectangle, matching Chromium's rounded-outline geometry.
    /// </summary>
    internal static class OutlineDrawHandler
    {
        /// <summary>
        /// The farthest this box's outline can paint outside its border edge. Used by the page painter
        /// to give outlines at a content-area edge room to enter the page margin before any narrower
        /// overflow clips are established.
        /// </summary>
        internal static double OutwardReach(RGraphics g, CssBox box)
        {
            var style = box.OutlineStyle.Value;
            if (style is OutlineStyle.None or OutlineStyle.Hidden) return 0;

            if (style == OutlineStyle.Auto)
                return Math.Max(0, box.ActualOutlineOffset + AutoRingWidth(g) / 2);

            return box.ActualOutlineWidth > 0
                ? Math.Max(0, box.ActualOutlineOffset + box.ActualOutlineWidth)
                : 0;
        }

        /// <summary>
        /// Draws the box's outline around every one of <paramref name="rects"/> at once, as a single
        /// connected shape wherever they touch.
        /// </summary>
        /// <remarks>
        /// <para>
        /// CSS Basic User Interface 4 §4 says a fragmented box's outline "should" be drawn as one
        /// connected shape rather than left open, or closed separately, at each fragment. Chromium
        /// achieves that by unioning the fragments' rectangles and outlining the boundary of the
        /// result, which is what this reproduces: where consecutive fragments touch - which a border on
        /// a wrapped inline is usually enough to cause, since it makes each line's rectangle tall
        /// enough to reach the next - the edges between them are interior to the region and so are not
        /// outlined at all.
        /// </para>
        /// <para>
        /// Every style takes this path. The patterned and bevelled ones are defined per side on an
        /// ordinary box - a dash pattern fitted to a side's length, a bevel lit from a side's
        /// direction - which a unioned contour has no equivalent of, so they are resolved against the
        /// boundary itself instead: see <see cref="OutlineRegionPainter"/>.
        /// </para>
        /// </remarks>
        /// <param name="g">the device to draw into</param>
        /// <param name="box">the box to draw the outline for</param>
        /// <param name="rects">
        /// the border-box rectangles of every fragment of <paramref name="box"/> on this page
        /// </param>
        public static void DrawRegionOutline(RGraphics g, CssBox box, IReadOnlyList<RRect> rects)
        {
            if (rects.Count == 0) return;
            if (!TryResolveRing(g, box, out var ring)) return;

            var inflated = new List<RRect>(rects.Count);
            foreach (var rect in rects)
            {
                if (rect is not { Width: > 0, Height: > 0 }) continue;

                // outline-offset is applied per rectangle, before the union, so a negative one is
                // clamped against the rectangle it actually shrinks - and a positive one can pull two
                // otherwise separate fragments into a single contour.
                var horizontalReach = ClampReach(ring.Reach, rect.Width, ring.Width, true, true);
                var verticalReach = ClampReach(ring.Reach, rect.Height, ring.Width, true, true);

                inflated.Add(RRect.FromLTRB(
                    rect.Left - horizontalReach,
                    rect.Top - verticalReach,
                    rect.Right + horizontalReach,
                    rect.Bottom + verticalReach));
            }

            if (inflated.Count == 0) return;

            var contours = RectilinearRegion.Union(inflated);
            if (contours.Count == 0) return;

            // The box's radii resolve against its first fragment, the same rectangle the per-fragment
            // path resolves them against. Growing them to each contour is OutlineRegionPainter's job,
            // since how much a corner grows depends on which way that contour turns through it.
            BorderRadii? radii = null;
            var declared = box.ComputeRadii(rects[0]);
            if (declared.IsRounded) radii = declared;

            if (ring.IsInvert) g.PushBlendMode(RBlendMode.Difference);

            OutlineRegionPainter.DrawRegionOutline(
                g, contours, ToLineStyle(ring.Style), ring.Color, ring.Width, radii, ring.Offset);

            if (ring.IsInvert) g.PopBlendMode();
        }

        /// <summary>
        /// Whether <paramref name="box"/>'s outline style is one <see cref="DrawRegionOutline"/> can
        /// draw. Every style that paints at all can be; the rest paint nothing either way.
        /// </summary>
        internal static bool SupportsRegionOutline(CssBox box) =>
            box.OutlineStyle.Value is not (OutlineStyle.None or OutlineStyle.Hidden);

        /// <summary>
        /// Draws the box's outline, if any, around <paramref name="rect"/> (the same border-box
        /// rectangle <see cref="BordersDrawHandler.DrawBoxBorders"/> receives).
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="box">the box to draw the outline for</param>
        /// <param name="rect">the border-box rectangle the outline surrounds</param>
        /// <param name="hasLeftEdge">
        /// whether the box's leading edge belongs to this rectangle - see
        /// <see cref="BordersDrawHandler.DrawBoxBorders"/>'s own parameter doc. Gates the outline the
        /// same way it gates border, which is what keeps an inline element wrapped across multiple
        /// lines from drawing its left/right outline edges on every line.
        /// </param>
        /// <param name="hasRightEdge">whether the box's trailing edge belongs to this rectangle</param>
        /// <param name="hasTopEdge">whether the box's own top edge belongs to this rectangle</param>
        /// <param name="hasBottomEdge">whether the box's own bottom edge belongs to this rectangle</param>
        public static void DrawOutline(
            RGraphics g, CssBox box, RRect rect,
            bool hasLeftEdge, bool hasRightEdge, bool hasTopEdge, bool hasBottomEdge)
        {
            if (rect is not { Width: > 0, Height: > 0 }) return;
            if (!TryResolveRing(g, box, out var ring)) return;

            var horizontalReach = ClampReach(
                ring.Reach, rect.Width, ring.Width, hasLeftEdge, hasRightEdge);
            var verticalReach = ClampReach(
                ring.Reach, rect.Height, ring.Width, hasTopEdge, hasBottomEdge);
            var outerRect = RRect.FromLTRB(
                hasLeftEdge ? rect.Left - horizontalReach : rect.Left,
                hasTopEdge ? rect.Top - verticalReach : rect.Top,
                hasRightEdge ? rect.Right + horizontalReach : rect.Right,
                hasBottomEdge ? rect.Bottom + verticalReach : rect.Bottom);

            BorderRadii? outerRadii = null;
            var borderRadii = box.ComputeRadii(rect);
            if (borderRadii.IsRounded)
            {
                var (tlx, tly) = ExpandCorner(
                    borderRadii.TLX, borderRadii.TLY, horizontalReach, verticalReach);
                var (trx, try_) = ExpandCorner(
                    borderRadii.TRX, borderRadii.TRY, horizontalReach, verticalReach);
                var (brx, bry) = ExpandCorner(
                    borderRadii.BRX, borderRadii.BRY, horizontalReach, verticalReach);
                var (blx, bly) = ExpandCorner(
                    borderRadii.BLX, borderRadii.BLY, horizontalReach, verticalReach);
                outerRadii = DerivedStyle.ApplyCornerOverlap(
                    outerRect,
                    tlx, tly, trx, try_, brx, bry, blx, bly);
            }

            if (ring.IsInvert) g.PushBlendMode(RBlendMode.Difference);

            BoxEdgesDrawHandler.DrawBoxEdges(
                g, outerRect, ToLineStyle(ring.Style), ring.Color, ring.Width,
                hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge, outerRadii);

            if (ring.IsInvert) g.PopBlendMode();
        }

        #region Private methods

        /// <summary>
        /// Resolves everything about the ring that does not depend on the rectangle it surrounds:
        /// colour, width, offset and the style actually painted.
        /// </summary>
        /// <returns>whether there is a ring to paint at all</returns>
        private static bool TryResolveRing(RGraphics g, CssBox box, out Ring ring)
        {
            ring = default;

            var style = box.OutlineStyle.Value;
            if (style is OutlineStyle.None or OutlineStyle.Hidden) return false;

            var isInvert = string.Equals(box.OutlineColor, Keywords.Invert, StringComparison.OrdinalIgnoreCase);
            var color = isInvert ? RColor.White : box.ActualOutlineColor;
            var offset = box.ActualOutlineOffset;

            double width;
            if (style == OutlineStyle.Auto)
            {
                // CSS-UI-4 §4: "The outline-width property is ignored when outline-style is auto."
                // That sentence is normative and unconditional - the neighbouring "User agents may treat
                // auto as solid" licenses the *style*, not the width - so the declared width never
                // reaches the ring, not even a declared zero (Chrome paints `outline: 0 auto` too).
                width = AutoRingWidth(g);
                offset -= width / 2;
            }
            else
            {
                width = box.ActualOutlineWidth;
                if (width <= 0) return false;
            }

            ring = new Ring(
                style == OutlineStyle.Auto ? OutlineStyle.Solid : style,
                color, width, offset, isInvert);
            return true;
        }

        /// <summary>
        /// A resolved outline ring - everything about it that is independent of the rectangle or
        /// region it surrounds.
        /// </summary>
        /// <param name="Style">
        /// the style actually painted, with <c>auto</c> already resolved to <c>solid</c> (CSS-UI-4
        /// leaves <c>auto</c>'s appearance UA-defined)
        /// </param>
        /// <param name="Color">
        /// the colour to paint, with <c>invert</c> resolved - see <paramref name="IsInvert"/>
        /// </param>
        /// <param name="Width">the ring's thickness</param>
        /// <param name="Offset">how far outside the border edge the ring's inner edge sits</param>
        /// <param name="IsInvert">
        /// whether <c>outline-color: invert</c> was declared, in which case the ring paints white
        /// through a difference blend rather than in its own colour
        /// </param>
        private readonly record struct Ring(
            OutlineStyle Style, RColor Color, double Width, double Offset, bool IsInvert)
        {
            /// <summary>How far outside the border edge the ring's outer edge sits.</summary>
            internal double Reach => Offset + Width;
        }

        /// <summary>
        /// The width of the <c>auto</c> ring, in the caller's raw layout-space units. CSS-UI-4 leaves
        /// <c>auto</c>'s appearance entirely to the UA once it has said the author's own
        /// <c>outline-width</c> does not apply, so this is Chrome's focus ring measured off its
        /// rasterization: 2 CSS px, unchanged by the declared width (<c>0</c>, <c>1px</c>, <c>8px</c>
        /// and <c>20px</c> all render the same ring) and unchanged by device scale (4 device px at a
        /// 2x device scale factor, i.e. still 2 CSS px).
        /// </summary>
        /// <remarks>
        /// Chrome additionally centres this ring on the rectangle <c>outline-offset</c> inflates the
        /// border box to, rather than seating it wholly outside that rectangle the way every other
        /// style sits - so with the default zero offset the ring straddles the border edge, one CSS px
        /// either side. <see cref="DrawOutline"/> reproduces that by pulling the offset back half a
        /// width, which turns the ordinary outward-facing band into a centred one without any of the
        /// ring geometry below needing to know about it.
        /// </remarks>
        private static double AutoRingWidth(RGraphics g) =>
            AutoRingWidthInCssPixels * Length.PointsPerPx * g.PixelsPerPoint;

        /// <summary>Chrome's own focus-ring thickness - see <see cref="AutoRingWidth"/>.</summary>
        private const double AutoRingWidthInCssPixels = 2;

        private static double ClampReach(
            double reach, double size, double width, bool hasStartEdge, bool hasEndEdge)
        {
            var adjustableEdges = (hasStartEdge ? 1 : 0) + (hasEndEdge ? 1 : 0);
            return adjustableEdges == 0
                ? reach
                : Math.Max(reach, (2 * width - size) / adjustableEdges);
        }

        private static (double X, double Y) ExpandCorner(
            double radiusX, double radiusY, double horizontalReach, double verticalReach)
        {
            if (radiusX <= 0 || radiusY <= 0) return (0, 0);

            var expandedX = radiusX + horizontalReach;
            var expandedY = radiusY + verticalReach;
            return expandedX > 0 && expandedY > 0 ? (expandedX, expandedY) : (0, 0);
        }

        private static LineStyle ToLineStyle(OutlineStyle style) => style switch
        {
            OutlineStyle.Solid or OutlineStyle.Auto => LineStyle.Solid,
            OutlineStyle.Double => LineStyle.Double,
            OutlineStyle.Dotted => LineStyle.Dotted,
            OutlineStyle.Dashed => LineStyle.Dashed,
            OutlineStyle.Inset => LineStyle.Inset,
            OutlineStyle.Outset => LineStyle.Outset,
            OutlineStyle.Groove => LineStyle.Groove,
            OutlineStyle.Ridge => LineStyle.Ridge,
            OutlineStyle.Hidden => LineStyle.Hidden,
            _ => LineStyle.None
        };

        #endregion
    }
}

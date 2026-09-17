using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using System;

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

            var style = box.OutlineStyle.Value;
            if (style is OutlineStyle.None or OutlineStyle.Hidden) return;

            var isInvert = string.Equals(box.OutlineColor, Keywords.Invert, StringComparison.OrdinalIgnoreCase);
            var color = isInvert ? RColor.White : box.ActualOutlineColor;
            var offset = box.ActualOutlineOffset;
            var effectiveStyle = style == OutlineStyle.Auto ? OutlineStyle.Solid : style;

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
                if (width <= 0) return;
            }

            var reach = offset + width;
            var horizontalReach = ClampReach(
                reach, rect.Width, width, hasLeftEdge, hasRightEdge);
            var verticalReach = ClampReach(
                reach, rect.Height, width, hasTopEdge, hasBottomEdge);
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

            if (isInvert) g.PushBlendMode(RBlendMode.Difference);

            BoxEdgesDrawHandler.DrawBoxEdges(
                g, outerRect, ToLineStyle(effectiveStyle), color, width,
                hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge, outerRadii);

            if (isInvert) g.PopBlendMode();
        }

        #region Private methods

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

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
    /// in parallel with <see cref="BordersDrawHandler"/> but simplified: outline has a single
    /// width/color/style for all four sides (no per-side values to independently miter, unlike border),
    /// so each side's ring quad is built directly from the box's own two nested "reach" rectangles
    /// (one at <c>outline-offset</c>, one at <c>outline-offset + outline-width</c>) rather than
    /// <see cref="BordersDrawHandler"/>'s per-side width bookkeeping. <c>outline-style: auto</c> is
    /// rendered identically to <c>solid</c> (CSS-UI-4 leaves <c>auto</c>'s actual appearance UA-defined).
    /// Never follows <c>border-radius</c> - CSS-UI-4 only says a UA <i>may</i> do so.
    /// </summary>
    internal static class OutlineDrawHandler
    {
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

            var width = box.ActualOutlineWidth;
            if (width <= 0) return;

            var isInvert = string.Equals(box.OutlineColor, Keywords.Invert, StringComparison.OrdinalIgnoreCase);
            var color = isInvert ? RColor.White : box.ActualOutlineColor;
            var offset = box.ActualOutlineOffset;
            var effectiveStyle = style == OutlineStyle.Auto ? OutlineStyle.Solid : style;

            if (isInvert) g.PushBlendMode(RBlendMode.Difference);

            if (hasTopEdge) DrawSide(Border.Top, g, rect, effectiveStyle, color, width, offset, hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge);
            if (hasLeftEdge) DrawSide(Border.Left, g, rect, effectiveStyle, color, width, offset, hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge);
            if (hasBottomEdge) DrawSide(Border.Bottom, g, rect, effectiveStyle, color, width, offset, hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge);
            if (hasRightEdge) DrawSide(Border.Right, g, rect, effectiveStyle, color, width, offset, hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge);

            if (isInvert) g.PopBlendMode();
        }

        #region Private methods

        private static void DrawSide(
            Border side, RGraphics g, RRect rect, OutlineStyle style, RColor color, double width, double offset,
            bool hasLeftEdge, bool hasRightEdge, bool hasTopEdge, bool hasBottomEdge)
        {
            switch (style)
            {
                case OutlineStyle.Solid:
                    DrawRing(side, g, rect, g.GetSolidBrush(color), offset, offset + width, hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge);
                    break;
                case OutlineStyle.Inset:
                case OutlineStyle.Outset:
                    DrawRing(side, g, rect, g.GetSolidBrush(BorderBevelColors.ForSide(color, side, inset: style == OutlineStyle.Inset)), offset, offset + width, hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge);
                    break;
                case OutlineStyle.Double:
                case OutlineStyle.Groove:
                case OutlineStyle.Ridge:
                    DrawDoubleOrGrooveRidge(side, g, rect, style, color, width, offset, hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge);
                    break;
                default:
                    // Dotted/dashed draw as a single mid-band line - the corner join itself isn't
                    // mitred (representing dash/dot patterns as a mitred ring fill is far more involved
                    // than this repo's scope needs, the same simplification BordersDrawHandler's own
                    // dotted/dashed branch already accepts), but each line's span still has to reach the
                    // ring's outer corner point the way DrawRing's does - unlike border, whose bands
                    // face inward and so always stay within [rect.Left, rect.Right]/[rect.Top,
                    // rect.Bottom] regardless, outline's bands face outward, so a span left at the bare
                    // rect edges leaves the whole outward-facing corner square uncovered by any line.
                    DrawDottedOrDashedLine(side, g, rect, style, color, width, offset, hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge);
                    break;
            }
        }

        /// <summary>
        /// Builds and fills the ring quad for one side, between the two nested "reach" rectangles at
        /// distance <paramref name="offset"/> (inner, nearest the box) and <paramref name="reach"/>
        /// (outer, farthest from the box) from the box's own edge. Adjacent sides' quads share an exact
        /// edge at each corner - see this class's own doc comment - so long as both sides are actually
        /// drawn (<paramref name="hasLeftEdge"/> etc.); when the perpendicular side isn't drawn (an open
        /// fragment edge, e.g. mid-wrap on an inline element), the quad collapses flush to the box's own
        /// edge on that end instead of bulging outward with nothing to miter into.
        /// </summary>
        /// <remarks>
        /// The two distances are passed in rather than derived from one width so a <c>double</c>/
        /// <c>groove</c>/<c>ridge</c> outline can fill a sub-band of the ring through this same mitred
        /// builder - every corner point sits on the box corner's own 45° diagonal at whatever distance
        /// it is given, so a band mitres into its neighbours exactly as the full ring does.
        /// </remarks>
        private static void DrawRing(
            Border side, RGraphics g, RRect rect, RBrush brush, double offset, double reach,
            bool hasLeftEdge, bool hasRightEdge, bool hasTopEdge, bool hasBottomEdge)
        {
            RPoint[] points;

            switch (side)
            {
                case Border.Top:
                    points =
                    [
                        new RPoint(hasLeftEdge ? rect.Left - offset : rect.Left, rect.Top - offset),
                        new RPoint(hasRightEdge ? rect.Right + offset : rect.Right, rect.Top - offset),
                        new RPoint(hasRightEdge ? rect.Right + reach : rect.Right, rect.Top - reach),
                        new RPoint(hasLeftEdge ? rect.Left - reach : rect.Left, rect.Top - reach)
                    ];
                    break;
                case Border.Right:
                    points =
                    [
                        new RPoint(rect.Right + offset, hasTopEdge ? rect.Top - offset : rect.Top),
                        new RPoint(rect.Right + reach, hasTopEdge ? rect.Top - reach : rect.Top),
                        new RPoint(rect.Right + reach, hasBottomEdge ? rect.Bottom + reach : rect.Bottom),
                        new RPoint(rect.Right + offset, hasBottomEdge ? rect.Bottom + offset : rect.Bottom)
                    ];
                    break;
                case Border.Bottom:
                    points =
                    [
                        new RPoint(hasLeftEdge ? rect.Left - offset : rect.Left, rect.Bottom + offset),
                        new RPoint(hasRightEdge ? rect.Right + offset : rect.Right, rect.Bottom + offset),
                        new RPoint(hasRightEdge ? rect.Right + reach : rect.Right, rect.Bottom + reach),
                        new RPoint(hasLeftEdge ? rect.Left - reach : rect.Left, rect.Bottom + reach)
                    ];
                    break;
                default: // Border.Left
                    points =
                    [
                        new RPoint(rect.Left - offset, hasBottomEdge ? rect.Bottom + offset : rect.Bottom),
                        new RPoint(rect.Left - reach, hasBottomEdge ? rect.Bottom + reach : rect.Bottom),
                        new RPoint(rect.Left - reach, hasTopEdge ? rect.Top - reach : rect.Top),
                        new RPoint(rect.Left - offset, hasTopEdge ? rect.Top - offset : rect.Top)
                    ];
                    break;
            }

            g.DrawPolygon(brush, points);
        }

        /// <summary>
        /// Draws a "double", "groove", or "ridge" outline as two mitred bands of the ring, mirroring
        /// <c>BordersDrawHandler.DrawDoubleOrGrooveRidgeBorder</c> but banded outward from the border
        /// edge (within <c>[offset, offset + width]</c>) instead of inward from it, and using one
        /// uniform width/color for all four sides rather than border's independent per-side values.
        /// "Outer" therefore means the band farthest from the box, the reverse of border's own sense.
        /// </summary>
        private static void DrawDoubleOrGrooveRidge(
            Border side, RGraphics g, RRect rect, OutlineStyle style, RColor color, double width, double offset,
            bool hasLeftEdge, bool hasRightEdge, bool hasTopEdge, bool hasBottomEdge)
        {
            double innerEnd;
            double outerStart;
            RColor outerColor;
            RColor innerColor;

            if (style == OutlineStyle.Double)
            {
                innerEnd = width / 3;
                outerStart = 2 * width / 3;
                outerColor = innerColor = color;
            }
            else
            {
                innerEnd = outerStart = width / 2;
                var outerIsInset = style == OutlineStyle.Groove;
                outerColor = BorderBevelColors.ForSide(color, side, outerIsInset);
                innerColor = BorderBevelColors.ForSide(color, side, !outerIsInset);
            }

            DrawRing(side, g, rect, g.GetSolidBrush(innerColor), offset, offset + innerEnd, hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge);
            DrawRing(side, g, rect, g.GetSolidBrush(outerColor), offset + outerStart, offset + width, hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge);
        }

        private static void DrawDottedOrDashedLine(
            Border side, RGraphics g, RRect rect, OutlineStyle style, RColor color, double width, double offset,
            bool hasLeftEdge, bool hasRightEdge, bool hasTopEdge, bool hasBottomEdge)
        {
            var mid = offset + width / 2;

            var pen = g.GetPen(color);
            // width is the caller's raw, un-divided layout-space (PixelsPerInch-inflated) outline
            // width - every outline *position* is already built from correctly-scaled coordinates (see
            // DrawRing/DrawDottedOrDashedLine), but a pen's own stroke width bypasses those and needs
            // the same PixelsPerPoint correction here (mirrors BordersDrawHandler.GetPen, issue #851).
            pen.Width = width / g.PixelsPerPoint;
            pen.LineJoin = RLineJoin.Miter;

            bool isHorizontal;
            double acrossAxis;
            double start;
            double end;

            switch (side)
            {
                case Border.Top:
                    isHorizontal = true;
                    acrossAxis = rect.Top - mid;
                    start = hasLeftEdge ? rect.Left - mid : rect.Left;
                    end = hasRightEdge ? rect.Right + mid : rect.Right;
                    break;
                case Border.Bottom:
                    isHorizontal = true;
                    acrossAxis = rect.Bottom + mid;
                    start = hasLeftEdge ? rect.Left - mid : rect.Left;
                    end = hasRightEdge ? rect.Right + mid : rect.Right;
                    break;
                case Border.Left:
                    isHorizontal = false;
                    acrossAxis = rect.Left - mid;
                    start = hasTopEdge ? rect.Top - mid : rect.Top;
                    end = hasBottomEdge ? rect.Bottom + mid : rect.Bottom;
                    break;
                default:
                    isHorizontal = false;
                    acrossAxis = rect.Right + mid;
                    start = hasTopEdge ? rect.Top - mid : rect.Top;
                    end = hasBottomEdge ? rect.Bottom + mid : rect.Bottom;
                    break;
            }

            var span = StyledStrokeFitting.Apply(pen, style == OutlineStyle.Dotted, width, start, end, g.PixelsPerPoint);
            if (span is { } fitted)
            {
                start = fitted.Start;
                end = fitted.End;
            }
            else
            {
                pen.LineCap = RLineCap.Butt;
                pen.DashStyle = RDashStyle.Solid;
            }

            if (isHorizontal)
                g.DrawLine(pen, start, acrossAxis, end, acrossAxis);
            else
                g.DrawLine(pen, acrossAxis, start, acrossAxis, end);
        }

        #endregion
    }
}

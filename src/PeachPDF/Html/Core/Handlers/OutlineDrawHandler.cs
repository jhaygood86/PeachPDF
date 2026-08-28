using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
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
                    DrawRing(side, g, rect, g.GetSolidBrush(color), width, offset, hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge);
                    break;
                case OutlineStyle.Inset:
                    DrawRing(side, g, rect, g.GetSolidBrush(side is Border.Top or Border.Left ? BordersDrawHandler.Darken(color) : color), width, offset, hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge);
                    break;
                case OutlineStyle.Outset:
                    DrawRing(side, g, rect, g.GetSolidBrush(side is Border.Right or Border.Bottom ? BordersDrawHandler.Darken(color) : color), width, offset, hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge);
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
                    var pen = GetPen(g, style, color, width);
                    DrawDottedOrDashedLine(side, g, rect, pen, width, offset, hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge);
                    break;
            }
        }

        /// <summary>
        /// Builds and fills the ring quad for one side, between the two nested "reach" rectangles at
        /// distance <paramref name="offset"/> (inner, nearest the box) and
        /// <paramref name="offset"/> + <paramref name="width"/> (outer, farthest from the box) from the
        /// box's own edge. Adjacent sides' quads share an exact edge at each corner - see this class's
        /// own doc comment - so long as both sides are actually drawn (<paramref name="hasLeftEdge"/>
        /// etc.); when the perpendicular side isn't drawn (an open fragment edge, e.g. mid-wrap on an
        /// inline element), the quad collapses flush to the box's own edge on that end instead of
        /// bulging outward with nothing to miter into.
        /// </summary>
        private static void DrawRing(
            Border side, RGraphics g, RRect rect, RBrush brush, double width, double offset,
            bool hasLeftEdge, bool hasRightEdge, bool hasTopEdge, bool hasBottomEdge)
        {
            var reach = offset + width;
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
        /// Draws a "double", "groove", or "ridge" outline as two solid stripes spanning the full side,
        /// mirroring <c>BordersDrawHandler.DrawDoubleOrGrooveRidgeBorder</c>'s technique but banded
        /// outward from the border edge (within <c>[offset, offset + width]</c>) instead of inward from
        /// it, and using one uniform width/color for all four sides rather than border's independent
        /// per-side values.
        /// </summary>
        private static void DrawDoubleOrGrooveRidge(
            Border side, RGraphics g, RRect rect, OutlineStyle style, RColor color, double width, double offset,
            bool hasLeftEdge, bool hasRightEdge, bool hasTopEdge, bool hasBottomEdge)
        {
            double outerWidth;
            double innerWidth;
            RColor outerColor;
            RColor innerColor;

            if (style == OutlineStyle.Double)
            {
                outerWidth = innerWidth = Math.Max(1, Math.Floor(width / 3));
                outerColor = innerColor = color;
            }
            else
            {
                outerWidth = innerWidth = width / 2;
                outerColor = style == OutlineStyle.Groove ? BordersDrawHandler.Darken(color) : color;
                innerColor = style == OutlineStyle.Groove ? color : BordersDrawHandler.Darken(color);
            }

            // outerWidth/innerWidth stay in the caller's raw, un-divided layout-space units below (used
            // for nearBand/farBand position math, paired with the still-raw rect) - only the pen's own
            // stroke width needs the PixelsPerPoint correction, same reason as GetPen's.
            var outerPen = g.GetPen(outerColor);
            outerPen.Width = outerWidth / g.PixelsPerPoint;
            outerPen.DashStyle = RDashStyle.Solid;

            var innerPen = g.GetPen(innerColor);
            innerPen.Width = innerWidth / g.PixelsPerPoint;
            innerPen.DashStyle = RDashStyle.Solid;

            // "outer"/"inner" name the stripe's position within the ring band (nearest the box edge vs.
            // farthest from it), matching BordersDrawHandler's own naming for the analogous stripes.
            var nearBand = offset + outerWidth / 2;
            var farBand = offset + width - innerWidth / 2;

            switch (side)
            {
                case Border.Top:
                    g.DrawLine(outerPen, hasLeftEdge ? rect.Left - nearBand : rect.Left, rect.Top - nearBand, hasRightEdge ? rect.Right + nearBand : rect.Right, rect.Top - nearBand);
                    g.DrawLine(innerPen, hasLeftEdge ? rect.Left - farBand : rect.Left, rect.Top - farBand, hasRightEdge ? rect.Right + farBand : rect.Right, rect.Top - farBand);
                    break;
                case Border.Right:
                    g.DrawLine(outerPen, rect.Right + nearBand, hasTopEdge ? rect.Top - nearBand : rect.Top, rect.Right + nearBand, hasBottomEdge ? rect.Bottom + nearBand : rect.Bottom);
                    g.DrawLine(innerPen, rect.Right + farBand, hasTopEdge ? rect.Top - farBand : rect.Top, rect.Right + farBand, hasBottomEdge ? rect.Bottom + farBand : rect.Bottom);
                    break;
                case Border.Bottom:
                    g.DrawLine(outerPen, hasLeftEdge ? rect.Left - nearBand : rect.Left, rect.Bottom + nearBand, hasRightEdge ? rect.Right + nearBand : rect.Right, rect.Bottom + nearBand);
                    g.DrawLine(innerPen, hasLeftEdge ? rect.Left - farBand : rect.Left, rect.Bottom + farBand, hasRightEdge ? rect.Right + farBand : rect.Right, rect.Bottom + farBand);
                    break;
                case Border.Left:
                    g.DrawLine(outerPen, rect.Left - nearBand, hasTopEdge ? rect.Top - nearBand : rect.Top, rect.Left - nearBand, hasBottomEdge ? rect.Bottom + nearBand : rect.Bottom);
                    g.DrawLine(innerPen, rect.Left - farBand, hasTopEdge ? rect.Top - farBand : rect.Top, rect.Left - farBand, hasBottomEdge ? rect.Bottom + farBand : rect.Bottom);
                    break;
            }
        }

        private static void DrawDottedOrDashedLine(
            Border side, RGraphics g, RRect rect, RPen pen, double width, double offset,
            bool hasLeftEdge, bool hasRightEdge, bool hasTopEdge, bool hasBottomEdge)
        {
            var mid = offset + width / 2;

            switch (side)
            {
                case Border.Top:
                    g.DrawLine(pen, hasLeftEdge ? rect.Left - mid : rect.Left, rect.Top - mid, hasRightEdge ? rect.Right + mid : rect.Right, rect.Top - mid);
                    break;
                case Border.Right:
                    g.DrawLine(pen, rect.Right + mid, hasTopEdge ? rect.Top - mid : rect.Top, rect.Right + mid, hasBottomEdge ? rect.Bottom + mid : rect.Bottom);
                    break;
                case Border.Bottom:
                    g.DrawLine(pen, hasLeftEdge ? rect.Left - mid : rect.Left, rect.Bottom + mid, hasRightEdge ? rect.Right + mid : rect.Right, rect.Bottom + mid);
                    break;
                case Border.Left:
                    g.DrawLine(pen, rect.Left - mid, hasTopEdge ? rect.Top - mid : rect.Top, rect.Left - mid, hasBottomEdge ? rect.Bottom + mid : rect.Bottom);
                    break;
            }
        }

        private static RPen GetPen(RGraphics g, OutlineStyle style, RColor color, double width)
        {
            var p = g.GetPen(color);
            // width is the caller's raw, un-divided layout-space (PixelsPerInch-inflated) outline
            // width - every outline *position* is already built from correctly-scaled coordinates (see
            // DrawRing/DrawDottedOrDashedLine), but a pen's own stroke width bypasses those and needs
            // the same PixelsPerPoint correction here (mirrors BordersDrawHandler.GetPen, issue #851).
            p.Width = width / g.PixelsPerPoint;
            p.DashStyle = style switch
            {
                OutlineStyle.Dotted => RDashStyle.Dot,
                OutlineStyle.Dashed => RDashStyle.Dash,
                _ => RDashStyle.Solid
            };

            return p;
        }

        #endregion
    }
}

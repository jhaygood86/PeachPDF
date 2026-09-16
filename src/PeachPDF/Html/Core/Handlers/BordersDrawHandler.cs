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
    /// Contains all the complex paint code to paint different style borders.
    /// </summary>
    internal static class BordersDrawHandler
    {
        #region Fields and Consts

        /// <summary>
        /// How close two border widths have to be to count as the same one. Widths reach paint through
        /// percentage resolution and unit conversion, so two sides declared identically can differ in
        /// the last bits; a tolerance far below one device pixel at any sane resolution avoids treating
        /// that as a genuine per-side difference.
        /// </summary>
        private const double Epsilon = 1e-6;

        #endregion


        /// <summary>
        /// Draws all the border of the box with respect to style, width, etc.
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="box">the box to draw borders for</param>
        /// <param name="rect">the bounding rectangle to draw in</param>
        /// <param name="hasLeftEdge">
        /// whether the box's leading edge belongs to this rectangle. False on a fragment a break starts, so
        /// no border is inserted there (css-break-3 §6.2's <c>slice</c>) — which also means the adjacent
        /// top/bottom edges must not take their 45° mitre cut on that side, or a notch appears.
        /// </param>
        /// <param name="hasRightEdge">whether the box's trailing edge belongs to this rectangle</param>
        /// <param name="hasTopEdge">
        /// whether the box's own top edge belongs to this rectangle. False on a fragment that resumes an
        /// earlier fragmentainer — the block-axis twin of <paramref name="hasLeftEdge"/>, and what keeps a box
        /// split at a <i>column</i> boundary from closing itself off there (no clip cuts it: two columns share
        /// one page band).
        /// </param>
        /// <param name="hasBottomEdge">whether the box's own bottom edge belongs to this rectangle</param>
        public static void DrawBoxBorders(
            RGraphics g, CssBox box, RRect rect,
            bool hasLeftEdge, bool hasRightEdge, bool hasTopEdge = true, bool hasBottomEdge = true)
        {
            if (rect is not { Width: > 0, Height: > 0 }) return;

            // A collapse participant's own stroke on an edge CollapsedBorderModel resolved is drawn once,
            // later, from CssBox.CollapsedBorderSegments instead (issue #735) - see BorderEdges' own remarks.
            var suppressed = box.SuppressedBorderEdges;

            if (TryDrawUniformBorder(g, box, rect, suppressed, hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge))
                return;

            if (TryDrawRoundedBorder(
                    g, box, rect, suppressed,
                    hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge))
                return;

            if (hasTopEdge && !suppressed.HasFlag(BorderEdges.Top) && box.BorderTopStyle.Value is not (LineStyle.None or LineStyle.Hidden) && box.ActualBorderTopWidth > 0)
            {
                DrawBorder(Border.Top, box, g, rect, hasLeftEdge, hasRightEdge, true, hasBottomEdge);
            }
            if (hasLeftEdge && !suppressed.HasFlag(BorderEdges.Left) && box.BorderLeftStyle.Value is not (LineStyle.None or LineStyle.Hidden) && box.ActualBorderLeftWidth > 0)
            {
                DrawBorder(Border.Left, box, g, rect, true, hasRightEdge, hasTopEdge, hasBottomEdge);
            }
            if (hasBottomEdge && !suppressed.HasFlag(BorderEdges.Bottom) && box.BorderBottomStyle.Value is not (LineStyle.None or LineStyle.Hidden) && box.ActualBorderBottomWidth > 0)
            {
                DrawBorder(Border.Bottom, box, g, rect, hasLeftEdge, hasRightEdge, hasTopEdge, true);
            }
            if (hasRightEdge && !suppressed.HasFlag(BorderEdges.Right) && box.BorderRightStyle.Value is not (LineStyle.None or LineStyle.Hidden) && box.ActualBorderRightWidth > 0)
            {
                DrawBorder(Border.Right, box, g, rect, hasLeftEdge, true, hasTopEdge, hasBottomEdge);
            }
        }

        /// <summary>
        /// Draws one CSS 2.1 §17.6.2 collapsed-border segment - a plain axis-aligned stripe with no
        /// mitre, since a collapsed segment butts against its neighbors at grid intersections rather than
        /// mitring into them the way one box's own four edges do (<see cref="GetInOutsetRectanglePoints"/>
        /// is what a real box's corner needs; a segment has no corner of its own to cut).
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="isHorizontal">Whether this is a horizontal (row) grid-line segment rather than a vertical (column) one - decides stripe orientation for double/groove/ridge and dash direction.</param>
        /// <param name="rect">the segment's own bounding rectangle</param>
        /// <param name="style">the resolved border style</param>
        /// <param name="color">the resolved border color</param>
        /// <param name="width">the resolved border width</param>
        internal static void DrawCollapsedSegment(RGraphics g, bool isHorizontal, RRect rect, LineStyle style, RColor color, double width)
        {
            if (rect is not { Width: > 0, Height: > 0 } || width <= 0 || style is LineStyle.None or LineStyle.Hidden) return;

            switch (style)
            {
                case LineStyle.Double or LineStyle.Groove or LineStyle.Ridge:
                    DrawDoubleOrGrooveRidgeSegment(g, isHorizontal, rect, style, color, width);
                    break;

                case LineStyle.Dotted or LineStyle.Dashed:
                {
                    var pen = g.GetPen(color);
                    pen.Width = width / g.PixelsPerPoint;
                    pen.LineJoin = RLineJoin.Miter;

                    var dotted = style == LineStyle.Dotted;
                    var start = isHorizontal ? rect.Left : rect.Top;
                    var end = isHorizontal ? rect.Right : rect.Bottom;

                    var span = StyledStrokeFitting.Apply(pen, dotted, width, start, end, g.PixelsPerPoint);
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
                    {
                        var y = rect.Top + width / 2;
                        g.DrawLine(pen, start, y, end, y);
                    }
                    else
                    {
                        var x = rect.Left + width / 2;
                        g.DrawLine(pen, x, start, x, end);
                    }
                    break;
                }

                default:
                {
                    // Solid, Inset, Outset - a collapsed segment has no owning box to shade a bevel
                    // "into" the way DrawBorder's per-edge convention does, so this approximates with
                    // the same asymmetry BorderBevelColors.ForSide uses for a box's own top/left edges
                    // (a horizontal segment behaves like a top edge, a vertical one like a left edge).
                    var resolvedColor = style is LineStyle.Inset or LineStyle.Outset
                        ? BorderBevelColors.ForSegment(color, isHorizontal, inset: style == LineStyle.Inset)
                        : color;
                    g.DrawPolygon(g.GetSolidBrush(resolvedColor),
                    [
                        new RPoint(rect.Left, rect.Top),
                        new RPoint(rect.Right, rect.Top),
                        new RPoint(rect.Right, rect.Bottom),
                        new RPoint(rect.Left, rect.Bottom)
                    ]);
                    break;
                }
            }
        }

        /// <summary>
        /// Value-based twin of <see cref="DrawDoubleOrGrooveRidgeBorder"/> for one collapsed-border
        /// segment - see <see cref="DrawCollapsedSegment"/>. A segment has no corner of its own, so the
        /// two rings are plain sub-rectangles rather than mitred bands, but the thirds and the per-side
        /// bevel shading are the same.
        /// </summary>
        private static void DrawDoubleOrGrooveRidgeSegment(RGraphics g, bool isHorizontal, RRect rect, LineStyle style, RColor color, double width)
        {
            double bandWidth;
            RColor outerColor;
            RColor innerColor;

            if (style == LineStyle.Double)
            {
                bandWidth = width / 3;
                outerColor = innerColor = color;
            }
            else
            {
                bandWidth = width / 2;
                var outerIsInset = style == LineStyle.Groove;
                outerColor = BorderBevelColors.ForSegment(color, isHorizontal, outerIsInset);
                innerColor = BorderBevelColors.ForSegment(color, isHorizontal, !outerIsInset);
            }

            DrawSegmentBand(g, isHorizontal, rect, outerColor, 0, bandWidth);
            DrawSegmentBand(g, isHorizontal, rect, innerColor, width - bandWidth, width);
        }

        /// <summary>Fills the sub-rectangle of a collapsed segment between two offsets from its outer edge.</summary>
        private static void DrawSegmentBand(RGraphics g, bool isHorizontal, RRect rect, RColor color, double from, double to)
        {
            var band = isHorizontal
                ? RRect.FromLTRB(rect.Left, rect.Top + from, rect.Right, rect.Top + to)
                : RRect.FromLTRB(rect.Left + from, rect.Top, rect.Left + to, rect.Bottom);

            g.DrawPolygon(g.GetSolidBrush(color),
            [
                new RPoint(band.Left, band.Top),
                new RPoint(band.Right, band.Top),
                new RPoint(band.Right, band.Bottom),
                new RPoint(band.Left, band.Bottom)
            ]);
        }

        /// <summary>
        /// Draw simple border.
        /// </summary>
        /// <param name="border">Desired border</param>
        /// <param name="g">the device to draw to</param>
        /// <param name="box">Box which the border corresponds</param>
        /// <param name="brush">the brush to use</param>
        /// <param name="rectangle">the bounding rectangle to draw in</param>
        /// <returns>Beveled border path, null if there is no rounded corners</returns>
        public static void DrawBorder(Border border, RGraphics g, CssBox box, RBrush brush, RRect rectangle)
        {
            g.DrawPolygon(
                brush,
                GetInOutsetRectanglePoints(border, box, rectangle, true, true, true, true));
        }


        #region Private methods

        /// <summary>
        /// Draw specific border (top/bottom/left/right) with the box data (style/width/rounded).<br/>
        /// </summary>
        /// <param name="border">desired border to draw</param>
        /// <param name="box">the box to draw its borders, contain the borders data</param>
        /// <param name="g">the device to draw into</param>
        /// <param name="rect">the rectangle the border is enclosing</param>
        /// <param name="isLineStart">Specifies if the border is for a starting line (no bevel on left)</param>
        /// <param name="isLineEnd">Specifies if the border is for an ending line (no bevel on right)</param>
        /// <param name="isBlockStart">Specifies if the box's own top edge is here (no bevel at the top)</param>
        /// <param name="isBlockEnd">Specifies if the box's own bottom edge is here (no bevel at the bottom)</param>
        private static void DrawBorder(
            Border border, CssBox box, RGraphics g, RRect rect,
            bool isLineStart, bool isLineEnd, bool isBlockStart, bool isBlockEnd)
        {
            var style = GetStyle(border, box);
            var baseColor = GetColor(border, box);
            var color = style switch
            {
                LineStyle.Inset => BorderBevelColors.ForSide(baseColor, border, inset: true),
                LineStyle.Outset => BorderBevelColors.ForSide(baseColor, border, inset: false),
                _ => baseColor
            };

            if (style is LineStyle.Inset or LineStyle.Outset or LineStyle.Solid)
            {
                // Solid (like inset/outset) needs the mitered trapezoid, not a thick straight line
                // spanning the box's full width/height: CSS2.1 8.5.3 draws each border edge as a
                // trapezoid whose non-parallel sides cut diagonally into the corner at 45°, meeting
                // exactly where the adjacent edge's own diagonal cut meets it. A simple thick line
                // has no such cut - it just overlaps/overwrites whichever adjacent-edge line painted
                // before it (DrawBoxBorders' fixed Top/Left/Bottom/Right paint order), which is
                // visually indistinguishable from mitering ONLY when every border shares the same
                // width and color (the common case) - it silently breaks the classic CSS
                // zero-content-width "border triangle" technique (mismatched adjacent border colors
                // on a box with no content) into flat overlapping rectangles instead of a triangle.
                // Acid2's own ".nose div div:before"/":after" (the nose's diamond, "border-style:
                // none solid solid"/"solid solid none" with red/yellow/black/yellow colors) is
                // exactly this technique.
                g.DrawPolygon(
                    g.GetSolidBrush(color),
                    GetInOutsetRectanglePoints(
                        border, box, rect,
                        isLineStart, isLineEnd, isBlockStart, isBlockEnd));
            }
            else if (style is LineStyle.Double or LineStyle.Groove or LineStyle.Ridge)
            {
                DrawDoubleOrGrooveRidgeBorder(border, box, g, rect, style, baseColor, isLineStart, isLineEnd, isBlockStart, isBlockEnd);
            }
            else
            {
                // Dotted/dashed draw as a stroked line rather than a mitered fill. Matching adjacent
                // strokes overlap in the corner square, putting one dot or an L-shaped dash there;
                // when the adjacent edge differs, the stroke is clipped to the corner transition
                // diagonal so it cannot show through that edge's gaps.
                DrawDottedOrDashedBorder(
                    border, box, g, rect, style, color,
                    isLineStart, isLineEnd, isBlockStart, isBlockEnd);
            }
        }

        /// <summary>
        /// Paints a border whose four edges share one style and one color as closed rings, and reports
        /// whether it did. Returns false for anything else, which then paints per edge as usual.
        /// </summary>
        /// <remarks>
        /// Two antialiased polygons that share an exact edge do not composite to full coverage: each
        /// contributes partial alpha along the seam, so the mitre diagonal at every corner shows up as a
        /// pale hairline over the background. It is invisible when the adjacent edges differ in color
        /// (the diagonal is meant to be seen there) and glaring when they do not - which is the common
        /// case, a plain <c>border: 4px solid</c>.
        ///
        /// A ring has no interior seam to show. Each band is one path of two rectangular subpaths -
        /// outer and inner - filled even-odd, so every pixel of the border is painted exactly once.
        /// Painting once rather than overlapping matters beyond the seam: a translucent border color or
        /// an active blend mode would show any doubly-painted corner.
        ///
        /// Only <c>solid</c> and <c>double</c> qualify. <c>inset</c>/<c>outset</c>/<c>groove</c>/
        /// <c>ridge</c> deliberately shade each side differently (see <see cref="BorderBevelColors"/>),
        /// so they have no uniform color to fill a ring with - and being multi-colored, they are exactly
        /// the cases where the seam does not show anyway.
        /// </remarks>
        private static bool TryDrawUniformBorder(
            RGraphics g, CssBox box, RRect rect, BorderEdges suppressed,
            bool hasLeftEdge, bool hasRightEdge, bool hasTopEdge, bool hasBottomEdge)
        {
            if (!hasLeftEdge || !hasRightEdge || !hasTopEdge || !hasBottomEdge) return false;
            if (suppressed != BorderEdges.None) return false;

            var style = box.BorderTopStyle.Value;
            if (box.BorderRightStyle.Value != style ||
                box.BorderBottomStyle.Value != style ||
                box.BorderLeftStyle.Value != style) return false;

            var color = box.ActualBorderTopColor;
            if (box.ActualBorderRightColor != color ||
                box.ActualBorderBottomColor != color ||
                box.ActualBorderLeftColor != color) return false;

            if (box.ActualBorderTopWidth <= 0 || box.ActualBorderRightWidth <= 0 ||
                box.ActualBorderBottomWidth <= 0 || box.ActualBorderLeftWidth <= 0) return false;

            var radii = box.ComputeRadii(rect);
            if (radii.IsRounded)
                return TryDrawUniformRoundedOutline(g, box, rect, radii, style, color);

            if (style is not (LineStyle.Solid or LineStyle.Double)) return false;

            var brush = g.GetSolidBrush(color);
            if (style == LineStyle.Solid)
            {
                DrawUniformRing(g, box, rect, 0, 1, brush);
            }
            else
            {
                DrawUniformRing(g, box, rect, 0, 1 / 3d, brush);
                DrawUniformRing(g, box, rect, 2 / 3d, 1, brush);
            }

            return true;
        }

        /// <summary>
        /// Strokes a uniform rounded border as one continuous outline - with a dotted/dashed pattern
        /// fitted to its whole perimeter - and reports whether it did.
        /// </summary>
        /// <remarks>
        /// Building a separate stroked path per edge costs twice over. Four strokes that butt end-to-end
        /// leave the same antialiasing seam two abutting fills do, so
        /// a plain rounded <c>solid</c> border showed a pale mark where each arc met its straight run.
        /// And a dash pattern restarts its phase at every one of those paths - the top edge's path
        /// already carries both corner arcs - so dots piled up two and three deep at the corners, the
        /// more visibly the rounder the corner. One closed path has one continuous stroke and one phase.
        ///
        /// This needs a single width and color to stroke with, so it only takes the case where all four
        /// sides agree. <c>double</c> is two of these outlines, one per line, at the thirds CSS 2.1
        /// §8.5.3 gives it. <c>groove</c>/<c>ridge</c> use the per-edge rounded band builder instead.
        /// </remarks>
        private static bool TryDrawUniformRoundedOutline(
            RGraphics g, CssBox box, RRect rect, BorderRadii radii, LineStyle style, RColor color)
        {
            if (style is not (LineStyle.Solid or LineStyle.Dotted or LineStyle.Dashed or LineStyle.Double)) return false;

            var width = box.ActualBorderTopWidth;
            if (Math.Abs(box.ActualBorderRightWidth - width) > Epsilon ||
                Math.Abs(box.ActualBorderBottomWidth - width) > Epsilon ||
                Math.Abs(box.ActualBorderLeftWidth - width) > Epsilon) return false;

            if (style != LineStyle.Double)
                return TryStrokeRoundedOutline(g, rect, radii, style, color, width, width / 2);

            // The two lines and the gap between them are equal thirds, so each line is centered a sixth
            // of the way in from the edge it hugs. Two separate rings with a gap between them share no
            // edge, so neither seams against the other.
            //
            // The outer line is drawn first for the sake of the failure path: it sits closest to the
            // edge, so it is the last to run out of room. Bailing on it means nothing has been drawn yet
            // and the caller can safely fall back to the per-edge paths; if only the inner one cannot
            // fit, one line is better than painting the whole border again over what is already there.
            var lineWidth = width / 3;
            if (!TryStrokeRoundedOutline(g, rect, radii, LineStyle.Solid, color, lineWidth, lineWidth / 2))
                return false;

            TryStrokeRoundedOutline(g, rect, radii, LineStyle.Solid, color, lineWidth, width - lineWidth / 2);
            return true;
        }

        /// <summary>
        /// Paints a non-uniform rounded border through one shared corner-transition model.
        /// </summary>
        private static bool TryDrawRoundedBorder(
            RGraphics g, CssBox box, RRect rect, BorderEdges suppressed,
            bool hasLeftEdge, bool hasRightEdge, bool hasTopEdge, bool hasBottomEdge)
        {
            var radii = box.ComputeRadii(rect);
            if (!radii.IsRounded) return false;

            var physical = new RoundedBorderSides(hasTopEdge, hasRightEdge, hasBottomEdge, hasLeftEdge);
            var active = new RoundedBorderSides(
                IsActiveBorder(box, Border.Top, hasTopEdge && !suppressed.HasFlag(BorderEdges.Top)),
                IsActiveBorder(box, Border.Right, hasRightEdge && !suppressed.HasFlag(BorderEdges.Right)),
                IsActiveBorder(box, Border.Bottom, hasBottomEdge && !suppressed.HasFlag(BorderEdges.Bottom)),
                IsActiveBorder(box, Border.Left, hasLeftEdge && !suppressed.HasFlag(BorderEdges.Left)));

            if (!active.Any) return false;

            var widths = new RoundedBorderWidths(
                active.Top ? box.ActualBorderTopWidth : 0,
                active.Right ? box.ActualBorderRightWidth : 0,
                active.Bottom ? box.ActualBorderBottomWidth : 0,
                active.Left ? box.ActualBorderLeftWidth : 0);

            Object? previousMode = null;
            if (box.HtmlContainer is { AvoidGeometryAntialias: false })
                previousMode = g.SetAntiAliasSmoothingMode();

            try
            {
                DrawRoundedFilledBorderLayer(g, box, rect, radii, physical, active, widths, innerLayer: false);
                DrawRoundedFilledBorderLayer(g, box, rect, radii, physical, active, widths, innerLayer: true);

                foreach (var side in BorderPaintOrder)
                {
                    if (active.Is(side) && GetStyle(side, box) is LineStyle.Dotted or LineStyle.Dashed)
                        DrawRoundedPatternedSide(g, box, rect, radii, physical, active, widths, side);
                }
            }
            finally
            {
                g.ReturnPreviousSmoothingMode(previousMode);
            }

            return true;
        }

        /// <summary>
        /// Fills one layer of every non-patterned rounded side, grouping same-colored pieces into one
        /// path so abutting pieces composite once and cannot leave an antialiasing seam.
        /// </summary>
        private static void DrawRoundedFilledBorderLayer(
            RGraphics g, CssBox box, RRect rect, BorderRadii radii,
            RoundedBorderSides physical, RoundedBorderSides active, RoundedBorderWidths widths,
            bool innerLayer)
        {
            var angles = RoundedCornerAngles.From(widths);
            var groups = new List<(RColor Color, RGraphicsPath Path)>();

            try
            {
                foreach (var side in BorderPaintOrder)
                {
                    if (!active.Is(side)) continue;
                    if (!TryGetRoundedFillBand(box, side, innerLayer, out var from, out var to, out var color))
                        continue;

                    var groupIndex = groups.FindIndex(group => group.Color == color);
                    if (groupIndex < 0)
                    {
                        groupIndex = groups.Count;
                        groups.Add((color, g.GetGraphicsPath()));
                    }

                    var outer = CreateRoundedContour(rect, radii, widths, from, g.PixelsPerPoint);
                    var inner = CreateRoundedContour(rect, radii, widths, to, g.PixelsPerPoint);
                    AddRoundedBandSide(groups[groupIndex].Path, side, outer, inner, physical, active, angles);
                }

                foreach (var (color, path) in groups)
                    g.DrawPath(g.GetSolidBrush(color), path);
            }
            finally
            {
                foreach (var (_, path) in groups)
                    path.Dispose();
            }
        }

        private static bool TryGetRoundedFillBand(
            CssBox box, Border side, bool innerLayer,
            out double from, out double to, out RColor color)
        {
            var style = GetStyle(side, box);
            var baseColor = GetColor(side, box);

            switch (style)
            {
                case LineStyle.Solid:
                    if (innerLayer) break;
                    from = 0;
                    to = 1;
                    color = baseColor;
                    return true;

                case LineStyle.Inset or LineStyle.Outset:
                    if (innerLayer) break;
                    from = 0;
                    to = 1;
                    color = BorderBevelColors.ForSide(baseColor, side, inset: style == LineStyle.Inset);
                    return true;

                case LineStyle.Double:
                    from = innerLayer ? 2 / 3d : 0;
                    to = innerLayer ? 1 : 1 / 3d;
                    color = baseColor;
                    return true;

                case LineStyle.Groove or LineStyle.Ridge:
                    from = innerLayer ? 0.5 : 0;
                    to = innerLayer ? 1 : 0.5;
                    var isInset = innerLayer != (style == LineStyle.Groove);
                    color = BorderBevelColors.ForSide(baseColor, side, isInset);
                    return true;
            }

            from = to = 0;
            color = RColor.Empty;
            return false;
        }

        private static void DrawRoundedPatternedSide(
            RGraphics g, CssBox box, RRect rect, BorderRadii radii,
            RoundedBorderSides physical, RoundedBorderSides active, RoundedBorderWidths widths,
            Border side)
        {
            var angles = RoundedCornerAngles.From(widths);
            var outer = CreateRoundedContour(rect, radii, widths, 0, g.PixelsPerPoint);
            var center = CreateRoundedContour(rect, radii, widths, 0.5, g.PixelsPerPoint);
            var inner = CreateRoundedContour(rect, radii, widths, 1, g.PixelsPerPoint);

            using var path = g.GetGraphicsPath();
            AddRoundedSideCenterline(path, side, center, physical, active, angles);

            using var clip = g.GetGraphicsPath();
            AddRoundedBandSide(clip, side, outer, inner, physical, active, angles);

            g.PushClip(clip);
            try
            {
                var pen = GetPen(g, GetStyle(side, box), GetColor(side, box), GetWidth(side, box));
                g.DrawPath(pen, path);
            }
            finally
            {
                g.PopClip();
            }
        }

        private static RoundedContour CreateRoundedContour(
            RRect rect, BorderRadii radii, RoundedBorderWidths widths,
            double fraction, double pixelsPerPoint)
        {
            var leftInset = widths.Left * fraction;
            var topInset = widths.Top * fraction;
            var rightInset = widths.Right * fraction;
            var bottomInset = widths.Bottom * fraction;
            var contourRect = RRect.FromLTRB(
                (rect.Left + leftInset) / pixelsPerPoint,
                (rect.Top + topInset) / pixelsPerPoint,
                (rect.Right - rightInset) / pixelsPerPoint,
                (rect.Bottom - bottomInset) / pixelsPerPoint);

            static (double X, double Y) Corner(double x, double y) =>
                x > Epsilon && y > Epsilon ? (x, y) : (0, 0);

            var (tlx, tly) = Corner((radii.TLX - leftInset) / pixelsPerPoint, (radii.TLY - topInset) / pixelsPerPoint);
            var (trx, try_) = Corner((radii.TRX - rightInset) / pixelsPerPoint, (radii.TRY - topInset) / pixelsPerPoint);
            var (brx, bry) = Corner((radii.BRX - rightInset) / pixelsPerPoint, (radii.BRY - bottomInset) / pixelsPerPoint);
            var (blx, bly) = Corner((radii.BLX - leftInset) / pixelsPerPoint, (radii.BLY - bottomInset) / pixelsPerPoint);

            var factor = Math.Min(
                1,
                Math.Min(
                    Ratio(contourRect.Width, tlx + trx),
                    Math.Min(
                        Ratio(contourRect.Width, blx + brx),
                        Math.Min(
                            Ratio(contourRect.Height, tly + bly),
                            Ratio(contourRect.Height, try_ + bry)))));
            if (factor < 1)
            {
                tlx *= factor; tly *= factor;
                trx *= factor; try_ *= factor;
                brx *= factor; bry *= factor;
                blx *= factor; bly *= factor;
            }

            return new RoundedContour(contourRect, tlx, tly, trx, try_, brx, bry, blx, bly);
        }

        private static double Ratio(double available, double required) =>
            required > Epsilon ? Math.Max(0, available) / required : 1;

        /// <summary>
        /// Adds one side of a rounded ring band. A shared corner is divided in proportion to the two
        /// adjoining widths; a zero-width neighbor yields the whole arc; an omitted fragment edge yields
        /// a square end at the break.
        /// </summary>
        private static void AddRoundedBandSide(
            RGraphicsPath path, Border side, RoundedContour outer, RoundedContour inner,
            RoundedBorderSides physical, RoundedBorderSides active, RoundedCornerAngles angles)
        {
            const double QuarterTurn = Math.PI / 2;
            const double HalfTurn = Math.PI;
            const double ThreeQuarterTurn = 3 * Math.PI / 2;
            const double FullTurn = 2 * Math.PI;

            switch (side)
            {
                case Border.Top:
                {
                    var startAngle = active.Left ? angles.TopLeft : HalfTurn;
                    var endAngle = active.Right ? angles.TopRight : FullTurn;
                    if (physical.Left)
                    {
                        AddMove(path, outer, RGraphicsPath.Corner.TopLeft, startAngle);
                        AddCornerArc(path, outer, RGraphicsPath.Corner.TopLeft, startAngle, ThreeQuarterTurn);
                    }
                    else
                    {
                        path.AddMove(outer.Rect.Left, outer.Rect.Top);
                    }

                    if (physical.Right)
                    {
                        LineTo(path, outer, RGraphicsPath.Corner.TopRight, ThreeQuarterTurn);
                        AddCornerArc(path, outer, RGraphicsPath.Corner.TopRight, ThreeQuarterTurn, endAngle);
                        LineTo(path, inner, RGraphicsPath.Corner.TopRight, endAngle);
                        AddCornerArc(path, inner, RGraphicsPath.Corner.TopRight, endAngle, ThreeQuarterTurn);
                    }
                    else
                    {
                        path.LineTo(outer.Rect.Right, outer.Rect.Top);
                        path.LineTo(inner.Rect.Right, inner.Rect.Top);
                    }

                    if (physical.Left)
                    {
                        LineTo(path, inner, RGraphicsPath.Corner.TopLeft, ThreeQuarterTurn);
                        AddCornerArc(path, inner, RGraphicsPath.Corner.TopLeft, ThreeQuarterTurn, startAngle);
                    }
                    else
                    {
                        path.LineTo(inner.Rect.Left, inner.Rect.Top);
                    }
                    break;
                }

                case Border.Right:
                {
                    var startAngle = active.Top ? angles.TopRight : ThreeQuarterTurn;
                    var endAngle = active.Bottom ? angles.BottomRight : QuarterTurn;
                    if (physical.Top)
                    {
                        AddMove(path, outer, RGraphicsPath.Corner.TopRight, startAngle);
                        AddCornerArc(path, outer, RGraphicsPath.Corner.TopRight, startAngle, FullTurn);
                    }
                    else
                    {
                        path.AddMove(outer.Rect.Right, outer.Rect.Top);
                    }

                    if (physical.Bottom)
                    {
                        LineTo(path, outer, RGraphicsPath.Corner.BottomRight, 0);
                        AddCornerArc(path, outer, RGraphicsPath.Corner.BottomRight, 0, endAngle);
                        LineTo(path, inner, RGraphicsPath.Corner.BottomRight, endAngle);
                        AddCornerArc(path, inner, RGraphicsPath.Corner.BottomRight, endAngle, 0);
                    }
                    else
                    {
                        path.LineTo(outer.Rect.Right, outer.Rect.Bottom);
                        path.LineTo(inner.Rect.Right, inner.Rect.Bottom);
                    }

                    if (physical.Top)
                    {
                        LineTo(path, inner, RGraphicsPath.Corner.TopRight, FullTurn);
                        AddCornerArc(path, inner, RGraphicsPath.Corner.TopRight, FullTurn, startAngle);
                    }
                    else
                    {
                        path.LineTo(inner.Rect.Right, inner.Rect.Top);
                    }
                    break;
                }

                case Border.Bottom:
                {
                    var startAngle = active.Right ? angles.BottomRight : 0;
                    var endAngle = active.Left ? angles.BottomLeft : HalfTurn;
                    if (physical.Right)
                    {
                        AddMove(path, outer, RGraphicsPath.Corner.BottomRight, startAngle);
                        AddCornerArc(path, outer, RGraphicsPath.Corner.BottomRight, startAngle, QuarterTurn);
                    }
                    else
                    {
                        path.AddMove(outer.Rect.Right, outer.Rect.Bottom);
                    }

                    if (physical.Left)
                    {
                        LineTo(path, outer, RGraphicsPath.Corner.BottomLeft, QuarterTurn);
                        AddCornerArc(path, outer, RGraphicsPath.Corner.BottomLeft, QuarterTurn, endAngle);
                        LineTo(path, inner, RGraphicsPath.Corner.BottomLeft, endAngle);
                        AddCornerArc(path, inner, RGraphicsPath.Corner.BottomLeft, endAngle, QuarterTurn);
                    }
                    else
                    {
                        path.LineTo(outer.Rect.Left, outer.Rect.Bottom);
                        path.LineTo(inner.Rect.Left, inner.Rect.Bottom);
                    }

                    if (physical.Right)
                    {
                        LineTo(path, inner, RGraphicsPath.Corner.BottomRight, QuarterTurn);
                        AddCornerArc(path, inner, RGraphicsPath.Corner.BottomRight, QuarterTurn, startAngle);
                    }
                    else
                    {
                        path.LineTo(inner.Rect.Right, inner.Rect.Bottom);
                    }
                    break;
                }

                case Border.Left:
                {
                    var startAngle = active.Bottom ? angles.BottomLeft : QuarterTurn;
                    var endAngle = active.Top ? angles.TopLeft : ThreeQuarterTurn;
                    if (physical.Bottom)
                    {
                        AddMove(path, outer, RGraphicsPath.Corner.BottomLeft, startAngle);
                        AddCornerArc(path, outer, RGraphicsPath.Corner.BottomLeft, startAngle, HalfTurn);
                    }
                    else
                    {
                        path.AddMove(outer.Rect.Left, outer.Rect.Bottom);
                    }

                    if (physical.Top)
                    {
                        LineTo(path, outer, RGraphicsPath.Corner.TopLeft, HalfTurn);
                        AddCornerArc(path, outer, RGraphicsPath.Corner.TopLeft, HalfTurn, endAngle);
                        LineTo(path, inner, RGraphicsPath.Corner.TopLeft, endAngle);
                        AddCornerArc(path, inner, RGraphicsPath.Corner.TopLeft, endAngle, HalfTurn);
                    }
                    else
                    {
                        path.LineTo(outer.Rect.Left, outer.Rect.Top);
                        path.LineTo(inner.Rect.Left, inner.Rect.Top);
                    }

                    if (physical.Bottom)
                    {
                        LineTo(path, inner, RGraphicsPath.Corner.BottomLeft, HalfTurn);
                        AddCornerArc(path, inner, RGraphicsPath.Corner.BottomLeft, HalfTurn, startAngle);
                    }
                    else
                    {
                        path.LineTo(inner.Rect.Left, inner.Rect.Bottom);
                    }
                    break;
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(side));
            }

            path.CloseFigure();
        }

        /// <summary>
        /// Adds a patterned side from left to right or top to bottom. Unlike the clockwise fill-band
        /// contours, this direction is observable because it determines which end receives dash phase 0.
        /// </summary>
        private static void AddRoundedSideCenterline(
            RGraphicsPath path, Border side, RoundedContour center,
            RoundedBorderSides physical, RoundedBorderSides active, RoundedCornerAngles angles)
        {
            const double QuarterTurn = Math.PI / 2;
            const double HalfTurn = Math.PI;
            const double ThreeQuarterTurn = 3 * Math.PI / 2;
            const double FullTurn = 2 * Math.PI;

            switch (side)
            {
                case Border.Top:
                    if (physical.Left)
                    {
                        var startAngle = active.Left ? angles.TopLeft : HalfTurn;
                        AddMove(path, center, RGraphicsPath.Corner.TopLeft, startAngle);
                        AddCornerArc(path, center, RGraphicsPath.Corner.TopLeft, startAngle, ThreeQuarterTurn);
                    }
                    else
                    {
                        path.AddMove(center.Rect.Left, center.Rect.Top);
                    }

                    if (physical.Right)
                    {
                        var endAngle = active.Right ? angles.TopRight : FullTurn;
                        LineTo(path, center, RGraphicsPath.Corner.TopRight, ThreeQuarterTurn);
                        AddCornerArc(path, center, RGraphicsPath.Corner.TopRight, ThreeQuarterTurn, endAngle);
                    }
                    else
                    {
                        path.LineTo(center.Rect.Right, center.Rect.Top);
                    }
                    break;

                case Border.Right:
                    if (physical.Top)
                    {
                        var startAngle = active.Top ? angles.TopRight : ThreeQuarterTurn;
                        AddMove(path, center, RGraphicsPath.Corner.TopRight, startAngle);
                        AddCornerArc(path, center, RGraphicsPath.Corner.TopRight, startAngle, FullTurn);
                    }
                    else
                    {
                        path.AddMove(center.Rect.Right, center.Rect.Top);
                    }

                    if (physical.Bottom)
                    {
                        var endAngle = active.Bottom ? angles.BottomRight : QuarterTurn;
                        LineTo(path, center, RGraphicsPath.Corner.BottomRight, 0);
                        AddCornerArc(path, center, RGraphicsPath.Corner.BottomRight, 0, endAngle);
                    }
                    else
                    {
                        path.LineTo(center.Rect.Right, center.Rect.Bottom);
                    }
                    break;

                case Border.Bottom:
                    if (physical.Left)
                    {
                        var startAngle = active.Left ? angles.BottomLeft : HalfTurn;
                        AddMove(path, center, RGraphicsPath.Corner.BottomLeft, startAngle);
                        AddCornerArc(path, center, RGraphicsPath.Corner.BottomLeft, startAngle, QuarterTurn);
                    }
                    else
                    {
                        path.AddMove(center.Rect.Left, center.Rect.Bottom);
                    }

                    if (physical.Right)
                    {
                        var endAngle = active.Right ? angles.BottomRight : 0;
                        LineTo(path, center, RGraphicsPath.Corner.BottomRight, QuarterTurn);
                        AddCornerArc(path, center, RGraphicsPath.Corner.BottomRight, QuarterTurn, endAngle);
                    }
                    else
                    {
                        path.LineTo(center.Rect.Right, center.Rect.Bottom);
                    }
                    break;

                case Border.Left:
                    if (physical.Top)
                    {
                        var startAngle = active.Top ? angles.TopLeft : ThreeQuarterTurn;
                        AddMove(path, center, RGraphicsPath.Corner.TopLeft, startAngle);
                        AddCornerArc(path, center, RGraphicsPath.Corner.TopLeft, startAngle, HalfTurn);
                    }
                    else
                    {
                        path.AddMove(center.Rect.Left, center.Rect.Top);
                    }

                    if (physical.Bottom)
                    {
                        var endAngle = active.Bottom ? angles.BottomLeft : QuarterTurn;
                        LineTo(path, center, RGraphicsPath.Corner.BottomLeft, HalfTurn);
                        AddCornerArc(path, center, RGraphicsPath.Corner.BottomLeft, HalfTurn, endAngle);
                    }
                    else
                    {
                        path.LineTo(center.Rect.Left, center.Rect.Bottom);
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(side));
            }
        }

        private static void AddMove(
            RGraphicsPath path, RoundedContour contour, RGraphicsPath.Corner corner, double angle)
        {
            var point = PointOnCorner(contour, corner, angle);
            path.AddMove(point.X, point.Y);
        }

        private static void LineTo(
            RGraphicsPath path, RoundedContour contour, RGraphicsPath.Corner corner, double angle)
        {
            var point = PointOnCorner(contour, corner, angle);
            path.LineTo(point.X, point.Y);
        }

        /// <summary>Adds an elliptical arc of at most 90 degrees as a cubic Bézier segment.</summary>
        private static void AddCornerArc(
            RGraphicsPath path, RoundedContour contour, RGraphicsPath.Corner corner,
            double startAngle, double endAngle)
        {
            var (_, _, radiusX, radiusY) = CornerGeometry(contour, corner);
            var end = PointOnCorner(contour, corner, endAngle);
            if (radiusX <= Epsilon || radiusY <= Epsilon)
            {
                path.LineTo(end.X, end.Y);
                return;
            }

            var start = PointOnCorner(contour, corner, startAngle);
            var factor = 4d / 3 * Math.Tan((endAngle - startAngle) / 4);
            var control1 = new RPoint(
                start.X - factor * radiusX * Math.Sin(startAngle),
                start.Y + factor * radiusY * Math.Cos(startAngle));
            var control2 = new RPoint(
                end.X + factor * radiusX * Math.Sin(endAngle),
                end.Y - factor * radiusY * Math.Cos(endAngle));

            path.AddBezierTo(control1.X, control1.Y, control2.X, control2.Y, end.X, end.Y);
        }

        private static RPoint PointOnCorner(
            RoundedContour contour, RGraphicsPath.Corner corner, double angle)
        {
            var (centerX, centerY, radiusX, radiusY) = CornerGeometry(contour, corner);
            return radiusX <= Epsilon || radiusY <= Epsilon
                ? new RPoint(centerX, centerY)
                : new RPoint(
                    centerX + radiusX * Math.Cos(angle),
                    centerY + radiusY * Math.Sin(angle));
        }

        private static (double CenterX, double CenterY, double RadiusX, double RadiusY) CornerGeometry(
            RoundedContour contour, RGraphicsPath.Corner corner) =>
            corner switch
            {
                RGraphicsPath.Corner.TopLeft =>
                    (contour.Rect.Left + contour.TLX, contour.Rect.Top + contour.TLY, contour.TLX, contour.TLY),
                RGraphicsPath.Corner.TopRight =>
                    (contour.Rect.Right - contour.TRX, contour.Rect.Top + contour.TRY, contour.TRX, contour.TRY),
                RGraphicsPath.Corner.BottomRight =>
                    (contour.Rect.Right - contour.BRX, contour.Rect.Bottom - contour.BRY, contour.BRX, contour.BRY),
                RGraphicsPath.Corner.BottomLeft =>
                    (contour.Rect.Left + contour.BLX, contour.Rect.Bottom - contour.BLY, contour.BLX, contour.BLY),
                _ => throw new ArgumentOutOfRangeException(nameof(corner))
            };

        private readonly record struct RoundedContour(
            RRect Rect,
            double TLX, double TLY,
            double TRX, double TRY,
            double BRX, double BRY,
            double BLX, double BLY);

        private readonly record struct RoundedBorderSides(bool Top, bool Right, bool Bottom, bool Left)
        {
            internal bool Any => Top || Right || Bottom || Left;

            internal bool Is(Border side) =>
                side switch
                {
                    Border.Top => Top,
                    Border.Right => Right,
                    Border.Bottom => Bottom,
                    Border.Left => Left,
                    _ => throw new ArgumentOutOfRangeException(nameof(side))
                };
        }

        private readonly record struct RoundedBorderWidths(
            double Top, double Right, double Bottom, double Left);

        private readonly record struct RoundedCornerAngles(
            double TopLeft, double TopRight, double BottomRight, double BottomLeft)
        {
            internal static RoundedCornerAngles From(RoundedBorderWidths widths)
            {
                const double quarterTurn = Math.PI / 2;

                static double Split(double horizontal, double vertical) =>
                    horizontal + vertical > Epsilon
                        ? quarterTurn * vertical / (horizontal + vertical)
                        : quarterTurn / 2;

                return new RoundedCornerAngles(
                    Math.PI + Split(widths.Top, widths.Left),
                    2 * Math.PI - Split(widths.Top, widths.Right),
                    Split(widths.Bottom, widths.Right),
                    Math.PI - Split(widths.Bottom, widths.Left));
            }
        }

        private static readonly Border[] BorderPaintOrder =
            [Border.Top, Border.Left, Border.Bottom, Border.Right];

        private static bool IsActiveBorder(CssBox box, Border side, bool physical) =>
            physical &&
            GetStyle(side, box) is not (LineStyle.None or LineStyle.Hidden) &&
            GetWidth(side, box) > 0;

        /// <summary>
        /// Strokes one closed rounded outline of <paramref name="strokeWidth"/>, centered
        /// <paramref name="inset"/> in from the border box, and reports whether it fitted.
        /// </summary>
        private static bool TryStrokeRoundedOutline(
            RGraphics g, RRect rect, BorderRadii radii, LineStyle style, RColor color,
            double strokeWidth, double inset)
        {
            // A stroke is centered on its path, so the path is the border box pulled in by the inset -
            // and every corner radius shrinks by that same inset, never past zero.
            var centerRect = RRect.FromLTRB(rect.Left + inset, rect.Top + inset, rect.Right - inset, rect.Bottom - inset);
            if (centerRect is not { Width: > 0, Height: > 0 } || strokeWidth <= 0) return false;

            var tlx = Math.Max(0, radii.TLX - inset); var tly = Math.Max(0, radii.TLY - inset);
            var trx = Math.Max(0, radii.TRX - inset); var try_ = Math.Max(0, radii.TRY - inset);
            var brx = Math.Max(0, radii.BRX - inset); var bry = Math.Max(0, radii.BRY - inset);
            var blx = Math.Max(0, radii.BLX - inset); var bly = Math.Max(0, radii.BLY - inset);

            var pen = g.GetPen(color);
            pen.Width = strokeWidth / g.PixelsPerPoint;
            pen.LineJoin = RLineJoin.Miter;

            if (style == LineStyle.Solid)
            {
                pen.LineCap = RLineCap.Butt;
                pen.DashStyle = RDashStyle.Solid;
            }
            else
            {
                var perimeter =
                    Math.Max(0, centerRect.Width - tlx - trx) + Math.Max(0, centerRect.Width - blx - brx) +
                    Math.Max(0, centerRect.Height - tly - bly) + Math.Max(0, centerRect.Height - try_ - bry) +
                    (StyledStrokeFitting.EllipsePerimeter(tlx, tly) + StyledStrokeFitting.EllipsePerimeter(trx, try_) +
                     StyledStrokeFitting.EllipsePerimeter(brx, bry) + StyledStrokeFitting.EllipsePerimeter(blx, bly)) / 4;

                var dotted = style == LineStyle.Dotted;
                if (StyledStrokeFitting.FitClosed(dotted, strokeWidth, perimeter) is not { } pattern) return false;

                pen.LineCap = dotted ? RLineCap.Round : RLineCap.Butt;
                pen.SetDashPattern(
                    dotted
                        ? [0, pattern.Period / g.PixelsPerPoint]
                        : [pattern.DashLength / g.PixelsPerPoint, pattern.GapLength / g.PixelsPerPoint],
                    0);
            }

            using var path = RenderUtils.GetRoundRect(g, centerRect, tlx, tly, trx, try_, brx, bry, blx, bly);
            g.DrawPath(pen, path);
            return true;
        }

        /// <summary>
        /// Fills the closed ring between two <see cref="GetBandPoints"/> band boundaries - see
        /// <see cref="TryDrawUniformBorder"/>.
        /// </summary>
        private static void DrawUniformRing(RGraphics g, CssBox box, RRect rect, double from, double to, RBrush brush)
        {
            // Unlike DrawPolygon/DrawLine, whose coordinates the adapter divides on the way out, a path's
            // coordinates reach the backend as given, so they are divided here (issue #812).
            var ppp = g.PixelsPerPoint;

            using var path = g.GetGraphicsPath();
            path.FillMode = RFillMode.EvenOdd;
            AddBandRectangle(path, box, rect, from, ppp);
            AddBandRectangle(path, box, rect, to, ppp);

            g.DrawPath(brush, path);
        }

        /// <summary>
        /// Adds one closed rectangular subpath at <paramref name="fraction"/> of the way through each
        /// side's own border width - the boundary a band of that fraction sits on.
        /// </summary>
        private static void AddBandRectangle(RGraphicsPath path, CssBox box, RRect rect, double fraction, double pixelsPerPoint)
        {
            var left = (rect.Left + fraction * box.ActualBorderLeftWidth) / pixelsPerPoint;
            var top = (rect.Top + fraction * box.ActualBorderTopWidth) / pixelsPerPoint;
            var right = (rect.Right - fraction * box.ActualBorderRightWidth) / pixelsPerPoint;
            var bottom = (rect.Bottom - fraction * box.ActualBorderBottomWidth) / pixelsPerPoint;

            path.AddMove(left, top);
            path.LineTo(right, top);
            path.LineTo(right, bottom);
            path.LineTo(left, bottom);
            path.CloseFigure();
        }

        /// <summary>
        /// Strokes one dotted/dashed edge, with the pattern fitted to the edge so it starts and ends
        /// flush with the corners - see <see cref="StyledStrokeFitting"/> for why that matters and how
        /// the period is chosen. Matching adjacent strokes share the corner square, which puts one dot
        /// or an L-shaped dash there. If style, color, or width differs at a corner, the stroke is clipped
        /// to that side's transition diagonal instead, matching the same mitre used by filled borders.
        /// </summary>
        private static void DrawDottedOrDashedBorder(
            Border border, CssBox box, RGraphics g, RRect rect, LineStyle style, RColor color,
            bool isLineStart, bool isLineEnd, bool isBlockStart, bool isBlockEnd)
        {
            var width = GetWidth(border, box);
            var pen = g.GetPen(color);
            // width is the caller's raw, un-divided layout-space width - see GetPen's own remark for
            // why only the pen's stroke width (and, below, its dash array) needs this correction.
            pen.Width = width / g.PixelsPerPoint;
            pen.LineJoin = RLineJoin.Miter;

            bool isHorizontal;
            double acrossAxis;
            double start;
            double end;

            switch (border)
            {
                case Border.Top:
                    isHorizontal = true;
                    acrossAxis = rect.Top + box.ActualBorderTopWidth / 2;
                    start = rect.Left;
                    end = rect.Right;
                    break;
                case Border.Bottom:
                    isHorizontal = true;
                    acrossAxis = rect.Bottom - box.ActualBorderBottomWidth / 2;
                    start = rect.Left;
                    end = rect.Right;
                    break;
                case Border.Left:
                    isHorizontal = false;
                    acrossAxis = rect.Left + box.ActualBorderLeftWidth / 2;
                    start = rect.Top;
                    end = rect.Bottom;
                    break;
                default:
                    isHorizontal = false;
                    acrossAxis = rect.Right - box.ActualBorderRightWidth / 2;
                    start = rect.Top;
                    end = rect.Bottom;
                    break;
            }

            var span = StyledStrokeFitting.Apply(pen, style == LineStyle.Dotted, width, start, end, g.PixelsPerPoint);
            if (span is { } fitted)
            {
                start = fitted.Start;
                end = fitted.End;
            }
            else
            {
                // Too short to carry even two dashes - a single dash spanning the whole edge is just a
                // solid stripe, which is also what a browser degenerates to here.
                pen.LineCap = RLineCap.Butt;
                pen.DashStyle = RDashStyle.Solid;
            }

            var (startAdjacent, startPresent, endAdjacent, endPresent) = border switch
            {
                Border.Top or Border.Bottom => (Border.Left, isLineStart, Border.Right, isLineEnd),
                Border.Left or Border.Right => (Border.Top, isBlockStart, Border.Bottom, isBlockEnd),
                _ => throw new ArgumentOutOfRangeException(nameof(border))
            };
            var clipStart = NeedsPatternCornerClip(box, border, startAdjacent, startPresent);
            var clipEnd = NeedsPatternCornerClip(box, border, endAdjacent, endPresent);

            void DrawStroke()
            {
                if (isHorizontal)
                    g.DrawLine(pen, start, acrossAxis, end, acrossAxis);
                else
                    g.DrawLine(pen, acrossAxis, start, acrossAxis, end);
            }

            if (!clipStart && !clipEnd)
            {
                DrawStroke();
                return;
            }

            using var clip = CreatePatternCornerClip(
                g, border, box, rect, width, clipStart, clipEnd,
                isLineStart, isLineEnd, isBlockStart, isBlockEnd);
            g.PushClip(clip);
            try
            {
                DrawStroke();
            }
            finally
            {
                g.PopClip();
            }
        }

        private static bool NeedsPatternCornerClip(
            CssBox box, Border border, Border adjacent, bool adjacentPresent)
        {
            if (!adjacentPresent || box.SuppressedBorderEdges.HasFlag(ToBorderEdge(adjacent)))
                return false;

            var adjacentStyle = GetStyle(adjacent, box);
            if (adjacentStyle is LineStyle.None or LineStyle.Hidden || GetWidth(adjacent, box) <= 0)
                return false;

            return adjacentStyle != GetStyle(border, box) ||
                   GetColor(adjacent, box) != GetColor(border, box) ||
                   Math.Abs(GetWidth(adjacent, box) - GetWidth(border, box)) > Epsilon;
        }

        /// <summary>
        /// Builds a clip that constrains only the corner-transition diagonals. Its outer and inner
        /// boundaries extend beyond the stroke so clipping cannot thin the visible border edges.
        /// </summary>
        private static RGraphicsPath CreatePatternCornerClip(
            RGraphics g, Border border, CssBox box, RRect rect, double width,
            bool clipStart, bool clipEnd,
            bool isLineStart, bool isLineEnd, bool isBlockStart, bool isBlockEnd)
        {
            var points = GetBandPoints(
                border, box, rect, 0, 1,
                isLineStart, isLineEnd, isBlockStart, isBlockEnd);

            var edgeDirection = border is Border.Top or Border.Bottom
                ? new RPoint(1, 0)
                : new RPoint(0, 1);
            var outerDirection = border switch
            {
                Border.Top => new RPoint(0, -1),
                Border.Right => new RPoint(1, 0),
                Border.Bottom => new RPoint(0, 1),
                Border.Left => new RPoint(-1, 0),
                _ => throw new ArgumentOutOfRangeException(nameof(border))
            };

            if (!clipStart)
            {
                points[0] = Offset(points[0], edgeDirection, -width);
                points[3] = Offset(points[3], edgeDirection, -width);
            }
            if (!clipEnd)
            {
                points[1] = Offset(points[1], edgeDirection, width);
                points[2] = Offset(points[2], edgeDirection, width);
            }

            var ppp = g.PixelsPerPoint;
            using var unscaled = g.GetGraphicsPath();
            unscaled.AddMove(points[0].X, points[0].Y);
            var outerStart = Offset(points[0], outerDirection, width);
            var outerEnd = Offset(points[1], outerDirection, width);
            unscaled.LineTo(outerStart.X, outerStart.Y);
            unscaled.LineTo(outerEnd.X, outerEnd.Y);
            unscaled.LineTo(points[1].X, points[1].Y);
            unscaled.LineTo(points[2].X, points[2].Y);
            var innerEnd = Offset(points[2], outerDirection, -width);
            var innerStart = Offset(points[3], outerDirection, -width);
            unscaled.LineTo(innerEnd.X, innerEnd.Y);
            unscaled.LineTo(innerStart.X, innerStart.Y);
            unscaled.LineTo(points[3].X, points[3].Y);
            unscaled.CloseFigure();

            var clip = g.GetGraphicsPath();
            unscaled.Transform(new RMatrix(1 / ppp, 0, 0, 1 / ppp, 0, 0));
            clip.AddPath(unscaled);
            return clip;
        }

        private static RPoint Offset(RPoint point, RPoint direction, double distance) =>
            new(point.X + direction.X * distance, point.Y + direction.Y * distance);

        private static BorderEdges ToBorderEdge(Border border) =>
            border switch
            {
                Border.Top => BorderEdges.Top,
                Border.Right => BorderEdges.Right,
                Border.Bottom => BorderEdges.Bottom,
                Border.Left => BorderEdges.Left,
                _ => throw new ArgumentOutOfRangeException(nameof(border))
            };

        /// <summary>
        /// Set rectangle for inset/outset border as it need diagonal connection to other borders.
        /// </summary>
        /// <param name="border">Desired border</param>
        /// <param name="b">Box which the border corresponds</param>
        /// <param name="r">the rectangle the border is enclosing</param>
        /// <param name="isLineStart">Specifies if the border is for a starting line (no bevel on left)</param>
        /// <param name="isLineEnd">Specifies if the border is for an ending line (no bevel on right)</param>
        /// <param name="isBlockStart">Specifies if the box's own top edge is here (no bevel at the top)</param>
        /// <param name="isBlockEnd">Specifies if the box's own bottom edge is here (no bevel at the bottom)</param>
        /// <returns>Beveled border path, null if there is no rounded corners</returns>
        /// <remarks>
        /// The four flags are one rule read on two axes: a side border mitres into the adjacent edge only
        /// where that edge is really there. At a fragmentation break there is no adjacent edge to meet, so
        /// the side runs square to the break — the same reason <paramref name="isLineStart"/> already
        /// suppresses the top/bottom edges' 45° cut.
        /// </remarks>
        private static RPoint[] GetInOutsetRectanglePoints(
            Border border, CssBox b, RRect r,
            bool isLineStart, bool isLineEnd, bool isBlockStart, bool isBlockEnd) =>
            GetBandPoints(border, b, r, 0, 1, isLineStart, isLineEnd, isBlockStart, isBlockEnd);

        /// <summary>
        /// The generalization of <see cref="GetInOutsetRectanglePoints"/> to a <i>sub-band</i> of one
        /// edge, from <paramref name="from"/> to <paramref name="to"/> as fractions of that edge's
        /// width (0 = the border box's outer edge, 1 = its inner edge). This is what lets
        /// <c>double</c>/<c>groove</c>/<c>ridge</c> paint as properly mitred nested rings rather than
        /// four full-length stripes that cross each other's gaps at every corner.
        /// </summary>
        /// <remarks>
        /// A corner's mitre runs from the border box's outer corner to its inner corner, so a point at
        /// fraction <c>f</c> through one edge's width sits <c>f</c> of the *adjacent* edge's width in
        /// from the box side - which is exactly the parametrization below, and stays correct for
        /// unequal per-side widths (where the mitre is not 45°). The four flags suppress the cut on a
        /// side that isn't really there, per <see cref="GetInOutsetRectanglePoints"/>'s own remarks.
        /// </remarks>
        private static RPoint[] GetBandPoints(
            Border border, CssBox b, RRect r, double from, double to,
            bool isLineStart, bool isLineEnd, bool isBlockStart, bool isBlockEnd)
        {
            var points = new RPoint[4];
            var left = isLineStart ? b.ActualBorderLeftWidth : 0;
            var right = isLineEnd ? b.ActualBorderRightWidth : 0;
            var top = isBlockStart ? b.ActualBorderTopWidth : 0;
            var bottom = isBlockEnd ? b.ActualBorderBottomWidth : 0;

            switch (border)
            {
                case Border.Top:
                {
                    var near = r.Top + from * b.ActualBorderTopWidth;
                    var far = r.Top + to * b.ActualBorderTopWidth;
                    points[0] = new RPoint(r.Left + from * left, near);
                    points[1] = new RPoint(r.Right - from * right, near);
                    points[2] = new RPoint(r.Right - to * right, far);
                    points[3] = new RPoint(r.Left + to * left, far);
                    break;
                }
                case Border.Right:
                {
                    var near = r.Right - from * b.ActualBorderRightWidth;
                    var far = r.Right - to * b.ActualBorderRightWidth;
                    points[0] = new RPoint(near, r.Top + from * top);
                    points[1] = new RPoint(near, r.Bottom - from * bottom);
                    points[2] = new RPoint(far, r.Bottom - to * bottom);
                    points[3] = new RPoint(far, r.Top + to * top);
                    break;
                }
                case Border.Bottom:
                {
                    var near = r.Bottom - from * b.ActualBorderBottomWidth;
                    var far = r.Bottom - to * b.ActualBorderBottomWidth;
                    points[0] = new RPoint(r.Left + from * left, near);
                    points[1] = new RPoint(r.Right - from * right, near);
                    points[2] = new RPoint(r.Right - to * right, far);
                    points[3] = new RPoint(r.Left + to * left, far);
                    break;
                }
                case Border.Left:
                {
                    var near = r.Left + from * b.ActualBorderLeftWidth;
                    var far = r.Left + to * b.ActualBorderLeftWidth;
                    points[0] = new RPoint(near, r.Top + from * top);
                    points[1] = new RPoint(near, r.Bottom - from * bottom);
                    points[2] = new RPoint(far, r.Bottom - to * bottom);
                    points[3] = new RPoint(far, r.Top + to * top);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(border));
            }

            return points;
        }

        /// <summary>
        /// Draws a "double", "groove", or "ridge" border as two mitred bands of the edge - the outer
        /// and inner rings - rather than two full-length stripes.
        /// </summary>
        /// <remarks>
        /// Stripes spanning the whole edge are what this used to do, and they cross the adjacent edges'
        /// gaps at every corner: the top edge's inner stripe runs straight through the left and right
        /// borders' gap, turning all four corners into a visible ladder. Painting each ring as a
        /// <see cref="GetBandPoints"/> band mitres it into its neighbours instead, which is what a
        /// browser draws.
        ///
        /// <c>double</c> is three exact thirds (CSS 2.1 §8.5.3's "two lines ... the sum of the two
        /// lines and the space equals border-width"); rounding a third down to a whole unit, as this
        /// used to, makes the gap wider than either line at most widths. <c>groove</c> paints its outer
        /// half as <c>inset</c> and its inner half as <c>outset</c>, and <c>ridge</c> the reverse - and
        /// since inset/outset shade per *side* (see <see cref="BorderBevelColors.ForSide"/>), that is
        /// what gives the ring its carved/raised look instead of a flat two-tone frame.
        /// </remarks>
        private static void DrawDoubleOrGrooveRidgeBorder(
            Border border, CssBox box, RGraphics g, RRect rect, LineStyle style, RColor color,
            bool isLineStart, bool isLineEnd, bool isBlockStart, bool isBlockEnd)
        {
            double outerEnd, innerStart;
            RColor outerColor, innerColor;

            if (style == LineStyle.Double)
            {
                outerEnd = 1 / 3d;
                innerStart = 2 / 3d;
                outerColor = innerColor = color;
            }
            else
            {
                outerEnd = innerStart = 0.5;
                var outerIsInset = style == LineStyle.Groove;
                outerColor = BorderBevelColors.ForSide(color, border, outerIsInset);
                innerColor = BorderBevelColors.ForSide(color, border, !outerIsInset);
            }

            DrawBand(border, box, g, rect, 0, outerEnd, outerColor, isLineStart, isLineEnd, isBlockStart, isBlockEnd);
            DrawBand(border, box, g, rect, innerStart, 1, innerColor, isLineStart, isLineEnd, isBlockStart, isBlockEnd);
        }

        /// <summary>Fills one <see cref="GetBandPoints"/> band of an edge in a single flat color.</summary>
        private static void DrawBand(
            Border border, CssBox box, RGraphics g, RRect rect, double from, double to, RColor color,
            bool isLineStart, bool isLineEnd, bool isBlockStart, bool isBlockEnd)
        {
            g.DrawPolygon(
                g.GetSolidBrush(color),
                GetBandPoints(
                    border, box, rect, from, to,
                    isLineStart, isLineEnd, isBlockStart, isBlockEnd));
        }

        /// <summary>
        /// Get pen to be used for border draw respecting its style.
        /// </summary>
        private static RPen GetPen(RGraphics g, LineStyle style, RColor color, double width)
        {
            var p = g.GetPen(color);
            // Width is the caller's raw, un-divided layout-space (PixelsPerInch-inflated) value.
            // Path coordinates are divided while they are built, but a pen width bypasses that path
            // conversion and needs the same correction here.
            p.Width = width / g.PixelsPerPoint;
            p.LineJoin = RLineJoin.Miter;

            // A mixed rounded border has one path per patterned edge, so there is no single outline to
            // fit a pattern to. The period stays at its ideal and falls where it falls along that edge.
            if (style is LineStyle.Dotted)
            {
                p.LineCap = RLineCap.Round;
                p.SetDashPattern([0, 2 * width / g.PixelsPerPoint], 0);
            }
            else if (style is LineStyle.Dashed)
            {
                p.LineCap = RLineCap.Butt;
                p.SetDashPattern([2 * width / g.PixelsPerPoint, width / g.PixelsPerPoint], 0);
            }
            else
            {
                // Multi-band rounded styles are handled by TryDrawRoundedBorder and never reach here.
                // Any other unexpected style degrades to solid rather than crashing.
                p.LineCap = RLineCap.Butt;
                p.DashStyle = RDashStyle.Solid;
            }

            return p;
        }

        /// <summary>
        /// Get the declared border color for the given box border, before any bevel shading - see
        /// <see cref="BorderBevelColors"/>, which the caller applies per style.
        /// </summary>
        private static RColor GetColor(Border border, CssBox box)
        {
            return border switch
            {
                Border.Top => box.ActualBorderTopColor,
                Border.Right => box.ActualBorderRightColor,
                Border.Bottom => box.ActualBorderBottomColor,
                Border.Left => box.ActualBorderLeftColor,
                _ => throw new ArgumentOutOfRangeException(nameof(border))
            };
        }

        /// <summary>
        /// Get the border width for the given box border.
        /// </summary>
        private static double GetWidth(Border border, CssBox box)
        {
            return border switch
            {
                Border.Top => box.ActualBorderTopWidth,
                Border.Right => box.ActualBorderRightWidth,
                Border.Bottom => box.ActualBorderBottomWidth,
                Border.Left => box.ActualBorderLeftWidth,
                _ => throw new ArgumentOutOfRangeException(nameof(border))
            };
        }

        /// <summary>
        /// Get the border style for the given box border.
        /// </summary>
        private static LineStyle GetStyle(Border border, CssBox box)
        {
            return border switch
            {
                Border.Top => box.BorderTopStyle.Value,
                Border.Right => box.BorderRightStyle.Value,
                Border.Bottom => box.BorderBottomStyle.Value,
                Border.Left => box.BorderLeftStyle.Value,
                _ => throw new ArgumentOutOfRangeException(nameof(border))
            };
        }

        #endregion
    }
}
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

namespace PeachPDF.Html.Core.Handlers
{
    /// <summary>
    /// Contains all the complex paint code to paint different style borders.
    /// </summary>
    internal static class BordersDrawHandler
    {
        #region Fields and Consts

        /// <summary>
        /// used for all border paint to use the same points and not create new array each time.
        /// </summary>
        private static readonly RPoint[] _borderPts = new RPoint[4];

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
        /// mitring into them the way one box's own four edges do (<see cref="SetInOutsetRectanglePoints"/>
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
            SetInOutsetRectanglePoints(border, box, rectangle, true, true, true, true);
            g.DrawPolygon(brush, _borderPts);
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

            var borderPath = GetRoundedBorderPath(g, border, box, rect);
            if (borderPath != null)
            {
                // rounded border need special path
                Object? prevMode = null;
                if (box is { HtmlContainer: { AvoidGeometryAntialias: false }, IsRounded: true })
                    prevMode = g.SetAntiAliasSmoothingMode();

                var pen = GetPen(g, style, color, GetWidth(border, box));
                using (borderPath)
                    g.DrawPath(pen, borderPath);

                g.ReturnPreviousSmoothingMode(prevMode);
            }
            else
            {
                // non rounded border
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
                    SetInOutsetRectanglePoints(border, box, rect, isLineStart, isLineEnd, isBlockStart, isBlockEnd);
                    g.DrawPolygon(g.GetSolidBrush(color), _borderPts);
                }
                else if (style is LineStyle.Double or LineStyle.Groove or LineStyle.Ridge)
                {
                    DrawDoubleOrGrooveRidgeBorder(border, box, g, rect, style, baseColor, isLineStart, isLineEnd, isBlockStart, isBlockEnd);
                }
                else
                {
                    // Dotted/dashed draw as a stroked line rather than a mitered fill - unlike solid,
                    // real UAs don't mitre a dash pattern into the corner either. Each edge's stroke
                    // spans its full outer length, so two adjacent edges overlap in the corner square,
                    // which is what puts a dot exactly on each corner and makes two dashed edges meet
                    // in an L, the same as a browser.
                    DrawDottedOrDashedBorder(border, box, g, rect, style, color);
                }
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
        /// <see cref="GetRoundedBorderPath"/> builds a separate path per edge, which costs twice over.
        /// Four strokes that butt end-to-end leave the same antialiasing seam two abutting fills do, so
        /// a plain rounded <c>solid</c> border showed a pale mark where each arc met its straight run.
        /// And a dash pattern restarts its phase at every one of those paths - the top edge's path
        /// already carries both corner arcs - so dots piled up two and three deep at the corners, the
        /// more visibly the rounder the corner. One closed path has one continuous stroke and one phase.
        ///
        /// This needs a single width and color to stroke with, so it only takes the case where all four
        /// sides agree. Anything else falls back to the per-edge paths, as do <c>double</c>/
        /// <c>groove</c>/<c>ridge</c>, which a single centered stroke cannot represent at all.
        /// </remarks>
        private static bool TryDrawUniformRoundedOutline(
            RGraphics g, CssBox box, RRect rect, BorderRadii radii, LineStyle style, RColor color)
        {
            if (style is not (LineStyle.Solid or LineStyle.Dotted or LineStyle.Dashed)) return false;

            var width = box.ActualBorderTopWidth;
            if (Math.Abs(box.ActualBorderRightWidth - width) > Epsilon ||
                Math.Abs(box.ActualBorderBottomWidth - width) > Epsilon ||
                Math.Abs(box.ActualBorderLeftWidth - width) > Epsilon) return false;

            // The stroke is centered, so its outline is the border box pulled in by half the width -
            // and every corner radius shrinks by the same half, never past zero.
            var inset = width / 2;
            var centerRect = RRect.FromLTRB(rect.Left + inset, rect.Top + inset, rect.Right - inset, rect.Bottom - inset);
            if (centerRect is not { Width: > 0, Height: > 0 }) return false;

            var tlx = Math.Max(0, radii.TLX - inset); var tly = Math.Max(0, radii.TLY - inset);
            var trx = Math.Max(0, radii.TRX - inset); var try_ = Math.Max(0, radii.TRY - inset);
            var brx = Math.Max(0, radii.BRX - inset); var bry = Math.Max(0, radii.BRY - inset);
            var blx = Math.Max(0, radii.BLX - inset); var bly = Math.Max(0, radii.BLY - inset);

            var pen = g.GetPen(color);
            pen.Width = width / g.PixelsPerPoint;
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
                if (StyledStrokeFitting.FitClosed(dotted, width, perimeter) is not { } pattern) return false;

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
        /// Fills the closed ring between two <see cref="SetBandPoints"/> band boundaries - see
        /// <see cref="TryDrawUniformBorder"/>.
        /// </summary>
        private static void DrawUniformRing(RGraphics g, CssBox box, RRect rect, double from, double to, RBrush brush)
        {
            // Unlike DrawPolygon/DrawLine, whose coordinates the adapter divides on the way out, a path's
            // coordinates reach the backend as given - so they are divided here, the same correction
            // GetRoundedBorderPath makes for the same reason (issue #812).
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
        /// the period is chosen. The stroke spans the edge's full outer length (corner square included),
        /// which is what puts a dot exactly on each corner and makes two adjacent dashed edges meet in
        /// an L, matching a browser.
        /// </summary>
        private static void DrawDottedOrDashedBorder(Border border, CssBox box, RGraphics g, RRect rect, LineStyle style, RColor color)
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

            if (isHorizontal)
                g.DrawLine(pen, start, acrossAxis, end, acrossAxis);
            else
                g.DrawLine(pen, acrossAxis, start, acrossAxis, end);
        }

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
        private static void SetInOutsetRectanglePoints(
            Border border, CssBox b, RRect r,
            bool isLineStart, bool isLineEnd, bool isBlockStart, bool isBlockEnd) =>
            SetBandPoints(border, b, r, 0, 1, isLineStart, isLineEnd, isBlockStart, isBlockEnd);

        /// <summary>
        /// The generalization of <see cref="SetInOutsetRectanglePoints"/> to a <i>sub-band</i> of one
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
        /// side that isn't really there, per <see cref="SetInOutsetRectanglePoints"/>'s own remarks.
        /// </remarks>
        private static void SetBandPoints(
            Border border, CssBox b, RRect r, double from, double to,
            bool isLineStart, bool isLineEnd, bool isBlockStart, bool isBlockEnd)
        {
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
                    _borderPts[0] = new RPoint(r.Left + from * left, near);
                    _borderPts[1] = new RPoint(r.Right - from * right, near);
                    _borderPts[2] = new RPoint(r.Right - to * right, far);
                    _borderPts[3] = new RPoint(r.Left + to * left, far);
                    break;
                }
                case Border.Right:
                {
                    var near = r.Right - from * b.ActualBorderRightWidth;
                    var far = r.Right - to * b.ActualBorderRightWidth;
                    _borderPts[0] = new RPoint(near, r.Top + from * top);
                    _borderPts[1] = new RPoint(near, r.Bottom - from * bottom);
                    _borderPts[2] = new RPoint(far, r.Bottom - to * bottom);
                    _borderPts[3] = new RPoint(far, r.Top + to * top);
                    break;
                }
                case Border.Bottom:
                {
                    var near = r.Bottom - from * b.ActualBorderBottomWidth;
                    var far = r.Bottom - to * b.ActualBorderBottomWidth;
                    _borderPts[0] = new RPoint(r.Left + from * left, near);
                    _borderPts[1] = new RPoint(r.Right - from * right, near);
                    _borderPts[2] = new RPoint(r.Right - to * right, far);
                    _borderPts[3] = new RPoint(r.Left + to * left, far);
                    break;
                }
                case Border.Left:
                {
                    var near = r.Left + from * b.ActualBorderLeftWidth;
                    var far = r.Left + to * b.ActualBorderLeftWidth;
                    _borderPts[0] = new RPoint(near, r.Top + from * top);
                    _borderPts[1] = new RPoint(near, r.Bottom - from * bottom);
                    _borderPts[2] = new RPoint(far, r.Bottom - to * bottom);
                    _borderPts[3] = new RPoint(far, r.Top + to * top);
                    break;
                }
            }
        }

        /// <summary>
        /// Draws a "double", "groove", or "ridge" border as two mitred bands of the edge - the outer
        /// and inner rings - rather than two full-length stripes.
        /// </summary>
        /// <remarks>
        /// Stripes spanning the whole edge are what this used to do, and they cross the adjacent edges'
        /// gaps at every corner: the top edge's inner stripe runs straight through the left and right
        /// borders' gap, turning all four corners into a visible ladder. Painting each ring as a
        /// <see cref="SetBandPoints"/> band mitres it into its neighbours instead, which is what a
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

        /// <summary>Fills one <see cref="SetBandPoints"/> band of an edge in a single flat color.</summary>
        private static void DrawBand(
            Border border, CssBox box, RGraphics g, RRect rect, double from, double to, RColor color,
            bool isLineStart, bool isLineEnd, bool isBlockStart, bool isBlockEnd)
        {
            SetBandPoints(border, box, rect, from, to, isLineStart, isLineEnd, isBlockStart, isBlockEnd);
            g.DrawPolygon(g.GetSolidBrush(color), _borderPts);
        }

        /// <summary>
        /// Makes a border path for rounded borders.<br/>
        /// To support rounded dotted/dashed borders we need to use arc in the border path.<br/>
        /// Return null if the border is not rounded.<br/>
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="border">Desired border</param>
        /// <param name="b">Box which the border corresponds</param>
        /// <param name="r">the rectangle the border is enclosing</param>
        /// <returns>Beveled border path, null if there is no rounded corners</returns>
        private static RGraphicsPath? GetRoundedBorderPath(RGraphics g, Border border, CssBox b, RRect r)
        {
            var rad = b.ComputeRadii(r);
            if (!rad.IsRounded) return null;

            // r, b's ActualBorder*Width fields, and rad's eight components are all in the caller's
            // layout-space units (the same PixelsPerInch-inflated space as CssBox geometry). This path
            // builder is independent of RenderUtils.GetRoundRect (no shared code) and needs the identical
            // divide-by-PixelsPerPoint correction for the same reason - see that method's remarks (#812).
            var ppp = g.PixelsPerPoint;
            var left = r.Left / ppp;
            var top = r.Top / ppp;
            var right = r.Right / ppp;
            var bottom = r.Bottom / ppp;
            var blw = b.ActualBorderLeftWidth / ppp;
            var btw = b.ActualBorderTopWidth / ppp;
            var brw = b.ActualBorderRightWidth / ppp;
            var bbw = b.ActualBorderBottomWidth / ppp;
            // The stroke is centered, so this path runs half a border width inside the border box - and
            // a corner's radius on that centerline is smaller than the border box's by exactly that
            // half, on each axis independently (the X radii follow the left/right border, the Y radii
            // the top/bottom). Using the border box's own radii here, as this used to, pushes each
            // straight run's start point half a border width too far along: harmless when the radius is
            // large compared to the border, but once a radius reaches half the box - a pill - the two
            // arcs claim more than the centerline has, the run between them comes out REVERSED, and the
            // pen paints it as a stub sticking out of the middle of the end cap.
            var radTLX = Math.Max(0, rad.TLX / ppp - blw / 2);
            var radTLY = Math.Max(0, rad.TLY / ppp - btw / 2);
            var radTRX = Math.Max(0, rad.TRX / ppp - brw / 2);
            var radTRY = Math.Max(0, rad.TRY / ppp - btw / 2);
            var radBRX = Math.Max(0, rad.BRX / ppp - brw / 2);
            var radBRY = Math.Max(0, rad.BRY / ppp - bbw / 2);
            var radBLX = Math.Max(0, rad.BLX / ppp - blw / 2);
            var radBLY = Math.Max(0, rad.BLY / ppp - bbw / 2);

            // Whether this edge gets a path at all is decided by the box's OWN radii, not the reduced
            // ones: a border thicker than twice its radius reduces to a square centerline, but it is
            // still a rounded box and must keep painting as one continuous stroke rather than falling
            // back to four mitred quads, which would both lose the rounding and reintroduce the corner
            // seams the quads have.
            var roundedTL = rad.TLX > 0 || rad.TLY > 0;
            var roundedTR = rad.TRX > 0 || rad.TRY > 0;
            var roundedBR = rad.BRX > 0 || rad.BRY > 0;
            var roundedBL = rad.BLX > 0 || rad.BLY > 0;

            RGraphicsPath? path = null;
            switch (border)
            {
                case Border.Top:
                    if (roundedTL || roundedTR)
                    {
                        path = g.GetGraphicsPath();
                        path.Start(left + blw / 2, top + btw / 2 + radTLY);
                        if (radTLX > 0 || radTLY > 0)
                            path.ArcTo(left + blw / 2 + radTLX, top + btw / 2, radTLX, radTLY, RGraphicsPath.Corner.TopLeft);
                        path.LineTo(right - brw / 2 - radTRX, top + btw / 2);
                        if (radTRX > 0 || radTRY > 0)
                            path.ArcTo(right - brw / 2, top + btw / 2 + radTRY, radTRX, radTRY, RGraphicsPath.Corner.TopRight);
                    }
                    break;
                case Border.Bottom:
                    if (roundedBL || roundedBR)
                    {
                        path = g.GetGraphicsPath();
                        path.Start(right - brw / 2, bottom - bbw / 2 - radBRY);
                        if (radBRX > 0 || radBRY > 0)
                            path.ArcTo(right - brw / 2 - radBRX, bottom - bbw / 2, radBRX, radBRY, RGraphicsPath.Corner.BottomRight);
                        path.LineTo(left + blw / 2 + radBLX, bottom - bbw / 2);
                        if (radBLX > 0 || radBLY > 0)
                            path.ArcTo(left + blw / 2, bottom - bbw / 2 - radBLY, radBLX, radBLY, RGraphicsPath.Corner.BottomLeft);
                    }
                    break;
                case Border.Right:
                    if (roundedTR || roundedBR)
                    {
                        path = g.GetGraphicsPath();
                        bool noTop = b.BorderTopStyle.Value is LineStyle.None or LineStyle.Hidden;
                        bool noBottom = b.BorderBottomStyle.Value is LineStyle.None or LineStyle.Hidden;
                        path.Start(right - brw / 2 - (noTop ? radTRX : 0), top + btw / 2 + (noTop ? 0 : radTRY));
                        if ((radTRX > 0 || radTRY > 0) && noTop)
                            path.ArcTo(right - brw / 2, top + btw / 2 + radTRY, radTRX, radTRY, RGraphicsPath.Corner.TopRight);
                        path.LineTo(right - brw / 2, bottom - bbw / 2 - radBRY);
                        if ((radBRX > 0 || radBRY > 0) && noBottom)
                            path.ArcTo(right - brw / 2 - radBRX, bottom - bbw / 2, radBRX, radBRY, RGraphicsPath.Corner.BottomRight);
                    }
                    break;
                case Border.Left:
                    if (roundedTL || roundedBL)
                    {
                        path = g.GetGraphicsPath();
                        bool noTop = b.BorderTopStyle.Value is LineStyle.None or LineStyle.Hidden;
                        bool noBottom = b.BorderBottomStyle.Value is LineStyle.None or LineStyle.Hidden;
                        path.Start(left + blw / 2 + (noBottom ? radBLX : 0), bottom - bbw / 2 - (noBottom ? 0 : radBLY));
                        if ((radBLX > 0 || radBLY > 0) && noBottom)
                            path.ArcTo(left + blw / 2, bottom - bbw / 2 - radBLY, radBLX, radBLY, RGraphicsPath.Corner.BottomLeft);
                        path.LineTo(left + blw / 2, top + btw / 2 + radTLY);
                        if ((radTLX > 0 || radTLY > 0) && noTop)
                            path.ArcTo(left + blw / 2 + radTLX, top + btw / 2, radTLX, radTLY, RGraphicsPath.Corner.TopLeft);
                    }
                    break;
            }

            return path;
        }

        /// <summary>
        /// Get pen to be used for border draw respecting its style.
        /// </summary>
        private static RPen GetPen(RGraphics g, LineStyle style, RColor color, double width)
        {
            var p = g.GetPen(color);
            // width is the caller's raw, un-divided layout-space (PixelsPerInch-inflated) border
            // width - every border *position* is already divided by PixelsPerPoint before reaching
            // the backend (GraphicsAdapter.DrawLine/DrawPolygon, GetRoundedBorderPath's own ppp
            // setup), but a pen's own stroke width bypasses those and needs the same correction here.
            p.Width = width / g.PixelsPerPoint;
            p.LineJoin = RLineJoin.Miter;

            // This is the rounded border whose sides do NOT all agree - a uniform one is stroked as a
            // single fitted outline by TryDrawUniformRoundedOutline and never reaches here. With one
            // path per edge there is no single outline to fit a pattern to, so the period stays at its
            // ideal (a dot every 2x the width) and falls where it falls along each arc. Dotted still
            // gets its round cap and zero-length dash, so a dot is a dot either way.
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
                // double/groove/ridge are handled by DrawDoubleOrGrooveRidgeBorder and never reach
                // here for non-rounded borders; a rounded border with one of these styles falls back
                // to a single solid-colored stroke here (GetRoundedBorderPath has no double/groove/
                // ridge concept - border-radius is CSS2/3 territory, out of scope for CSS1
                // compliance). Any other unexpected style also degrades to solid rather than crashing.
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
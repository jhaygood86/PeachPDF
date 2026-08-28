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
                    var pen = GetPen(g, style, color, width);
                    if (isHorizontal)
                    {
                        var y = rect.Top + width / 2;
                        g.DrawLine(pen, rect.Left, y, rect.Right, y);
                    }
                    else
                    {
                        var x = rect.Left + width / 2;
                        g.DrawLine(pen, x, rect.Top, x, rect.Bottom);
                    }
                    break;
                }

                default:
                {
                    // Solid, Inset, Outset - a collapsed segment has no owning box to shade a bevel
                    // "into" the way DrawBorder's per-edge convention does, so this approximates with
                    // the same asymmetry GetColor already uses for a box's own top/left edges (a
                    // horizontal segment behaves like a top edge, a vertical one like a left edge -
                    // both darken for Inset, stay normal for Outset).
                    var resolvedColor = style == LineStyle.Inset ? Darken(color) : color;
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

        /// <summary>Value-based twin of <see cref="DrawDoubleOrGrooveRidgeBorder"/> for one collapsed-border segment - see <see cref="DrawCollapsedSegment"/>.</summary>
        private static void DrawDoubleOrGrooveRidgeSegment(RGraphics g, bool isHorizontal, RRect rect, LineStyle style, RColor color, double width)
        {
            double outerWidth;
            double innerWidth;
            RColor outerColor;
            RColor innerColor;

            if (style == LineStyle.Double)
            {
                outerWidth = innerWidth = Math.Max(1, Math.Floor(width / 3));
                outerColor = innerColor = color;
            }
            else
            {
                outerWidth = innerWidth = width / 2;
                outerColor = style == LineStyle.Groove ? Darken(color) : color;
                innerColor = style == LineStyle.Groove ? color : Darken(color);
            }

            var outerPen = g.GetPen(outerColor);
            outerPen.Width = outerWidth;
            outerPen.DashStyle = RDashStyle.Solid;

            var innerPen = g.GetPen(innerColor);
            innerPen.Width = innerWidth;
            innerPen.DashStyle = RDashStyle.Solid;

            if (isHorizontal)
            {
                g.DrawLine(outerPen, rect.Left, rect.Top + outerWidth / 2, rect.Right, rect.Top + outerWidth / 2);
                g.DrawLine(innerPen, rect.Left, rect.Top + width - innerWidth / 2, rect.Right, rect.Top + width - innerWidth / 2);
            }
            else
            {
                g.DrawLine(outerPen, rect.Left + outerWidth / 2, rect.Top, rect.Left + outerWidth / 2, rect.Bottom);
                g.DrawLine(innerPen, rect.Left + width - innerWidth / 2, rect.Top, rect.Left + width - innerWidth / 2, rect.Bottom);
            }
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
            var color = GetColor(border, box, style);

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
                    DrawDoubleOrGrooveRidgeBorder(border, box, g, rect, style, color);
                }
                else
                {
                    // dotted/dashed border draw as simple line - representing dash/dot patterns as a
                    // mitered trapezoid fill is far more involved than this repo's scope needs, and
                    // (unlike solid) real UAs commonly render dotted/dashed corners as simple joins too.
                    var pen = GetPen(g, style, color, GetWidth(border, box));

                    switch (border)
                    {
                        case Border.Top:
                            g.DrawLine(pen, rect.Left, rect.Top + box.ActualBorderTopWidth / 2, rect.Right, rect.Top + box.ActualBorderTopWidth / 2);
                            break;
                        case Border.Left:
                            g.DrawLine(pen, rect.Left + box.ActualBorderLeftWidth / 2, rect.Top, rect.Left + box.ActualBorderLeftWidth / 2, rect.Bottom);
                            break;
                        case Border.Bottom:
                            g.DrawLine(pen, rect.Left, rect.Bottom - box.ActualBorderBottomWidth / 2, rect.Right, rect.Bottom - box.ActualBorderBottomWidth / 2);
                            break;
                        case Border.Right:
                            g.DrawLine(pen, rect.Right - box.ActualBorderRightWidth / 2, rect.Top, rect.Right - box.ActualBorderRightWidth / 2, rect.Bottom);
                            break;
                    }
                }
            }
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
            bool isLineStart, bool isLineEnd, bool isBlockStart, bool isBlockEnd)
        {
            var top = isBlockStart ? b.ActualBorderTopWidth : 0;
            var bottom = isBlockEnd ? b.ActualBorderBottomWidth : 0;

            switch (border)
            {
                case Border.Top:
                    _borderPts[0] = new RPoint(r.Left, r.Top);
                    _borderPts[1] = new RPoint(r.Right, r.Top);
                    _borderPts[2] = new RPoint(r.Right, r.Top + b.ActualBorderTopWidth);
                    _borderPts[3] = new RPoint(r.Left, r.Top + b.ActualBorderTopWidth);
                    if (isLineEnd)
                        _borderPts[2].X -= b.ActualBorderRightWidth;
                    if (isLineStart)
                        _borderPts[3].X += b.ActualBorderLeftWidth;
                    break;
                case Border.Right:
                    _borderPts[0] = new RPoint(r.Right - b.ActualBorderRightWidth, r.Top + top);
                    _borderPts[1] = new RPoint(r.Right, r.Top);
                    _borderPts[2] = new RPoint(r.Right, r.Bottom);
                    _borderPts[3] = new RPoint(r.Right - b.ActualBorderRightWidth, r.Bottom - bottom);
                    break;
                case Border.Bottom:
                    _borderPts[0] = new RPoint(r.Left, r.Bottom - b.ActualBorderBottomWidth);
                    _borderPts[1] = new RPoint(r.Right, r.Bottom - b.ActualBorderBottomWidth);
                    _borderPts[2] = new RPoint(r.Right, r.Bottom);
                    _borderPts[3] = new RPoint(r.Left, r.Bottom);
                    if (isLineStart)
                        _borderPts[0].X += b.ActualBorderLeftWidth;
                    if (isLineEnd)
                        _borderPts[1].X -= b.ActualBorderRightWidth;
                    break;
                case Border.Left:
                    _borderPts[0] = new RPoint(r.Left, r.Top);
                    _borderPts[1] = new RPoint(r.Left + b.ActualBorderLeftWidth, r.Top + top);
                    _borderPts[2] = new RPoint(r.Left + b.ActualBorderLeftWidth, r.Bottom - bottom);
                    _borderPts[3] = new RPoint(r.Left, r.Bottom);
                    break;
            }
        }

        /// <summary>
        /// Draws a "double", "groove", or "ridge" border as two solid stripes. A <see cref="RDashStyle"/>
        /// pen can't represent two parallel strokes with a gap (double) or a two-tone bevel
        /// (groove/ridge), so this paints the two stripes directly with their own pens instead of
        /// going through <see cref="GetPen"/>.
        /// </summary>
        private static void DrawDoubleOrGrooveRidgeBorder(Border border, CssBox box, RGraphics g, RRect rect, LineStyle style, RColor color)
        {
            var width = GetWidth(border, box);

            double outerWidth;
            double innerWidth;
            RColor outerColor;
            RColor innerColor;

            if (style == LineStyle.Double)
            {
                outerWidth = innerWidth = Math.Max(1, Math.Floor(width / 3));
                outerColor = innerColor = color;
            }
            else
            {
                // groove looks carved in (dark outer stripe, light inner stripe); ridge is its
                // mirror image (light outer, dark inner). CSS2.1 leaves the exact shading direction
                // UA-defined - the only spec-relevant property is that groove/ridge are visually
                // distinct from each other and from solid/double/inset/outset.
                outerWidth = innerWidth = width / 2;
                outerColor = style == LineStyle.Groove ? Darken(color) : color;
                innerColor = style == LineStyle.Groove ? color : Darken(color);
            }

            var outerPen = g.GetPen(outerColor);
            outerPen.Width = outerWidth;
            outerPen.DashStyle = RDashStyle.Solid;

            var innerPen = g.GetPen(innerColor);
            innerPen.Width = innerWidth;
            innerPen.DashStyle = RDashStyle.Solid;

            switch (border)
            {
                case Border.Top:
                    g.DrawLine(outerPen, rect.Left, rect.Top + outerWidth / 2, rect.Right, rect.Top + outerWidth / 2);
                    g.DrawLine(innerPen, rect.Left, rect.Top + width - innerWidth / 2, rect.Right, rect.Top + width - innerWidth / 2);
                    break;
                case Border.Left:
                    g.DrawLine(outerPen, rect.Left + outerWidth / 2, rect.Top, rect.Left + outerWidth / 2, rect.Bottom);
                    g.DrawLine(innerPen, rect.Left + width - innerWidth / 2, rect.Top, rect.Left + width - innerWidth / 2, rect.Bottom);
                    break;
                case Border.Bottom:
                    g.DrawLine(outerPen, rect.Left, rect.Bottom - outerWidth / 2, rect.Right, rect.Bottom - outerWidth / 2);
                    g.DrawLine(innerPen, rect.Left, rect.Bottom - width + innerWidth / 2, rect.Right, rect.Bottom - width + innerWidth / 2);
                    break;
                case Border.Right:
                    g.DrawLine(outerPen, rect.Right - outerWidth / 2, rect.Top, rect.Right - outerWidth / 2, rect.Bottom);
                    g.DrawLine(innerPen, rect.Right - width + innerWidth / 2, rect.Top, rect.Right - width + innerWidth / 2, rect.Bottom);
                    break;
            }
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
            var radTLX = rad.TLX / ppp;
            var radTLY = rad.TLY / ppp;
            var radTRX = rad.TRX / ppp;
            var radTRY = rad.TRY / ppp;
            var radBRX = rad.BRX / ppp;
            var radBRY = rad.BRY / ppp;
            var radBLX = rad.BLX / ppp;
            var radBLY = rad.BLY / ppp;

            RGraphicsPath? path = null;
            switch (border)
            {
                case Border.Top:
                    if (radTLX > 0 || radTLY > 0 || radTRX > 0 || radTRY > 0)
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
                    if (radBLX > 0 || radBLY > 0 || radBRX > 0 || radBRY > 0)
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
                    if (radTRX > 0 || radTRY > 0 || radBRX > 0 || radBRY > 0)
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
                    if (radTLX > 0 || radTLY > 0 || radBLX > 0 || radBLY > 0)
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
            p.Width = width;
            p.DashStyle = style switch
            {
                LineStyle.Solid => RDashStyle.Solid,
                LineStyle.Dotted => RDashStyle.Dot,
                LineStyle.Dashed => RDashStyle.Dash,
                // double/groove/ridge are handled by DrawDoubleOrGrooveRidgeBorder and never reach
                // here for non-rounded borders; a rounded border with one of these styles falls back
                // to a single solid-colored stroke here (GetRoundedBorderPath has no double/groove/
                // ridge concept - border-radius is CSS2/3 territory, out of scope for CSS1
                // compliance). Any other unexpected style also degrades to solid rather than crashing.
                _ => RDashStyle.Solid
            };

            return p;
        }

        /// <summary>
        /// Get the border color for the given box border.
        /// </summary>
        private static RColor GetColor(Border border, CssBox box, LineStyle style)
        {
            return border switch
            {
                Border.Top => style == LineStyle.Inset ? Darken(box.ActualBorderTopColor) : box.ActualBorderTopColor,
                Border.Right => style == LineStyle.Outset
                    ? Darken(box.ActualBorderRightColor)
                    : box.ActualBorderRightColor,
                Border.Bottom => style == LineStyle.Outset
                    ? Darken(box.ActualBorderBottomColor)
                    : box.ActualBorderBottomColor,
                Border.Left => style == LineStyle.Inset
                    ? Darken(box.ActualBorderLeftColor)
                    : box.ActualBorderLeftColor,
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

        /// <summary>
        /// Makes the specified color darker for inset/outset borders - also shared by
        /// <see cref="OutlineDrawHandler"/> for its own inset/outset/groove/ridge shading, since
        /// darkening a color for a beveled line style isn't a border-specific operation.
        /// </summary>
        internal static RColor Darken(RColor c)
        {
            return RColor.FromArgb(c.R / 2, c.G / 2, c.B / 2);
        }

        #endregion
    }
}
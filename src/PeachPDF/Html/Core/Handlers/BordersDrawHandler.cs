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
        /// <param name="isFirst">is it the first rectangle of the element</param>
        /// <param name="isLast">is it the last rectangle of the element</param>
        public static void DrawBoxBorders(RGraphics g, CssBox box, RRect rect, bool isFirst, bool isLast)
        {
            if (rect is not { Width: > 0, Height: > 0 }) return;

            if (!(string.IsNullOrEmpty(box.BorderTopStyle) || box.BorderTopStyle == CssConstants.None || box.BorderTopStyle == CssConstants.Hidden) && box.ActualBorderTopWidth > 0)
            {
                DrawBorder(Border.Top, box, g, rect, isFirst, isLast);
            }
            if (isFirst && !(string.IsNullOrEmpty(box.BorderLeftStyle) || box.BorderLeftStyle == CssConstants.None || box.BorderLeftStyle == CssConstants.Hidden) && box.ActualBorderLeftWidth > 0)
            {
                DrawBorder(Border.Left, box, g, rect, true, isLast);
            }
            if (!(string.IsNullOrEmpty(box.BorderBottomStyle) || box.BorderBottomStyle == CssConstants.None || box.BorderBottomStyle == CssConstants.Hidden) && box.ActualBorderBottomWidth > 0)
            {
                DrawBorder(Border.Bottom, box, g, rect, isFirst, isLast);
            }
            if (isLast && !(string.IsNullOrEmpty(box.BorderRightStyle) || box.BorderRightStyle == CssConstants.None || box.BorderRightStyle == CssConstants.Hidden) && box.ActualBorderRightWidth > 0)
            {
                DrawBorder(Border.Right, box, g, rect, isFirst, true);
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
            SetInOutsetRectanglePoints(border, box, rectangle, true, true);
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
        private static void DrawBorder(Border border, CssBox box, RGraphics g, RRect rect, bool isLineStart, bool isLineEnd)
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
                if (style is CssConstants.Inset or CssConstants.Outset or CssConstants.Solid)
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
                    SetInOutsetRectanglePoints(border, box, rect, isLineStart, isLineEnd);
                    g.DrawPolygon(g.GetSolidBrush(color), _borderPts);
                }
                else if (style is CssConstants.Double or CssConstants.Groove or CssConstants.Ridge)
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
        /// <returns>Beveled border path, null if there is no rounded corners</returns>
        private static void SetInOutsetRectanglePoints(Border border, CssBox b, RRect r, bool isLineStart, bool isLineEnd)
        {
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
                    _borderPts[0] = new RPoint(r.Right - b.ActualBorderRightWidth, r.Top + b.ActualBorderTopWidth);
                    _borderPts[1] = new RPoint(r.Right, r.Top);
                    _borderPts[2] = new RPoint(r.Right, r.Bottom);
                    _borderPts[3] = new RPoint(r.Right - b.ActualBorderRightWidth, r.Bottom - b.ActualBorderBottomWidth);
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
                    _borderPts[1] = new RPoint(r.Left + b.ActualBorderLeftWidth, r.Top + b.ActualBorderTopWidth);
                    _borderPts[2] = new RPoint(r.Left + b.ActualBorderLeftWidth, r.Bottom - b.ActualBorderBottomWidth);
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
        private static void DrawDoubleOrGrooveRidgeBorder(Border border, CssBox box, RGraphics g, RRect rect, string style, RColor color)
        {
            var width = GetWidth(border, box);

            double outerWidth;
            double innerWidth;
            RColor outerColor;
            RColor innerColor;

            if (style == CssConstants.Double)
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
                outerColor = style == CssConstants.Groove ? Darken(color) : color;
                innerColor = style == CssConstants.Groove ? color : Darken(color);
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

            RGraphicsPath? path = null;
            switch (border)
            {
                case Border.Top:
                    if (rad.TLX > 0 || rad.TLY > 0 || rad.TRX > 0 || rad.TRY > 0)
                    {
                        path = g.GetGraphicsPath();
                        path.Start(r.Left + b.ActualBorderLeftWidth / 2, r.Top + b.ActualBorderTopWidth / 2 + rad.TLY);
                        if (rad.TLX > 0 || rad.TLY > 0)
                            path.ArcTo(r.Left + b.ActualBorderLeftWidth / 2 + rad.TLX, r.Top + b.ActualBorderTopWidth / 2, rad.TLX, rad.TLY, RGraphicsPath.Corner.TopLeft);
                        path.LineTo(r.Right - b.ActualBorderRightWidth / 2 - rad.TRX, r.Top + b.ActualBorderTopWidth / 2);
                        if (rad.TRX > 0 || rad.TRY > 0)
                            path.ArcTo(r.Right - b.ActualBorderRightWidth / 2, r.Top + b.ActualBorderTopWidth / 2 + rad.TRY, rad.TRX, rad.TRY, RGraphicsPath.Corner.TopRight);
                    }
                    break;
                case Border.Bottom:
                    if (rad.BLX > 0 || rad.BLY > 0 || rad.BRX > 0 || rad.BRY > 0)
                    {
                        path = g.GetGraphicsPath();
                        path.Start(r.Right - b.ActualBorderRightWidth / 2, r.Bottom - b.ActualBorderBottomWidth / 2 - rad.BRY);
                        if (rad.BRX > 0 || rad.BRY > 0)
                            path.ArcTo(r.Right - b.ActualBorderRightWidth / 2 - rad.BRX, r.Bottom - b.ActualBorderBottomWidth / 2, rad.BRX, rad.BRY, RGraphicsPath.Corner.BottomRight);
                        path.LineTo(r.Left + b.ActualBorderLeftWidth / 2 + rad.BLX, r.Bottom - b.ActualBorderBottomWidth / 2);
                        if (rad.BLX > 0 || rad.BLY > 0)
                            path.ArcTo(r.Left + b.ActualBorderLeftWidth / 2, r.Bottom - b.ActualBorderBottomWidth / 2 - rad.BLY, rad.BLX, rad.BLY, RGraphicsPath.Corner.BottomLeft);
                    }
                    break;
                case Border.Right:
                    if (rad.TRX > 0 || rad.TRY > 0 || rad.BRX > 0 || rad.BRY > 0)
                    {
                        path = g.GetGraphicsPath();
                        bool noTop = b.BorderTopStyle == CssConstants.None || b.BorderTopStyle == CssConstants.Hidden;
                        bool noBottom = b.BorderBottomStyle == CssConstants.None || b.BorderBottomStyle == CssConstants.Hidden;
                        path.Start(r.Right - b.ActualBorderRightWidth / 2 - (noTop ? rad.TRX : 0), r.Top + b.ActualBorderTopWidth / 2 + (noTop ? 0 : rad.TRY));
                        if ((rad.TRX > 0 || rad.TRY > 0) && noTop)
                            path.ArcTo(r.Right - b.ActualBorderLeftWidth / 2, r.Top + b.ActualBorderTopWidth / 2 + rad.TRY, rad.TRX, rad.TRY, RGraphicsPath.Corner.TopRight);
                        path.LineTo(r.Right - b.ActualBorderRightWidth / 2, r.Bottom - b.ActualBorderBottomWidth / 2 - rad.BRY);
                        if ((rad.BRX > 0 || rad.BRY > 0) && noBottom)
                            path.ArcTo(r.Right - b.ActualBorderRightWidth / 2 - rad.BRX, r.Bottom - b.ActualBorderBottomWidth / 2, rad.BRX, rad.BRY, RGraphicsPath.Corner.BottomRight);
                    }
                    break;
                case Border.Left:
                    if (rad.TLX > 0 || rad.TLY > 0 || rad.BLX > 0 || rad.BLY > 0)
                    {
                        path = g.GetGraphicsPath();
                        bool noTop = b.BorderTopStyle == CssConstants.None || b.BorderTopStyle == CssConstants.Hidden;
                        bool noBottom = b.BorderBottomStyle == CssConstants.None || b.BorderBottomStyle == CssConstants.Hidden;
                        path.Start(r.Left + b.ActualBorderLeftWidth / 2 + (noBottom ? rad.BLX : 0), r.Bottom - b.ActualBorderBottomWidth / 2 - (noBottom ? 0 : rad.BLY));
                        if ((rad.BLX > 0 || rad.BLY > 0) && noBottom)
                            path.ArcTo(r.Left + b.ActualBorderLeftWidth / 2, r.Bottom - b.ActualBorderBottomWidth / 2 - rad.BLY, rad.BLX, rad.BLY, RGraphicsPath.Corner.BottomLeft);
                        path.LineTo(r.Left + b.ActualBorderLeftWidth / 2, r.Top + b.ActualBorderTopWidth / 2 + rad.TLY);
                        if ((rad.TLX > 0 || rad.TLY > 0) && noTop)
                            path.ArcTo(r.Left + b.ActualBorderLeftWidth / 2 + rad.TLX, r.Top + b.ActualBorderTopWidth / 2, rad.TLX, rad.TLY, RGraphicsPath.Corner.TopLeft);
                    }
                    break;
            }

            return path;
        }

        /// <summary>
        /// Get pen to be used for border draw respecting its style.
        /// </summary>
        private static RPen GetPen(RGraphics g, string style, RColor color, double width)
        {
            var p = g.GetPen(color);
            p.Width = width;
            p.DashStyle = style switch
            {
                "solid" => RDashStyle.Solid,
                "dotted" => RDashStyle.Dot,
                "dashed" => RDashStyle.Dash,
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
        private static RColor GetColor(Border border, CssBoxProperties box, string style)
        {
            return border switch
            {
                Border.Top => style == CssConstants.Inset ? Darken(box.ActualBorderTopColor) : box.ActualBorderTopColor,
                Border.Right => style == CssConstants.Outset
                    ? Darken(box.ActualBorderRightColor)
                    : box.ActualBorderRightColor,
                Border.Bottom => style == CssConstants.Outset
                    ? Darken(box.ActualBorderBottomColor)
                    : box.ActualBorderBottomColor,
                Border.Left => style == CssConstants.Inset
                    ? Darken(box.ActualBorderLeftColor)
                    : box.ActualBorderLeftColor,
                _ => throw new ArgumentOutOfRangeException(nameof(border))
            };
        }

        /// <summary>
        /// Get the border width for the given box border.
        /// </summary>
        private static double GetWidth(Border border, CssBoxProperties box)
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
        private static string GetStyle(Border border, CssBoxProperties box)
        {
            return border switch
            {
                Border.Top => box.BorderTopStyle,
                Border.Right => box.BorderRightStyle,
                Border.Bottom => box.BorderBottomStyle,
                Border.Left => box.BorderLeftStyle,
                _ => throw new ArgumentOutOfRangeException(nameof(border))
            };
        }

        /// <summary>
        /// Makes the specified color darker for inset/outset borders.
        /// </summary>
        private static RColor Darken(RColor c)
        {
            return RColor.FromArgb(c.R / 2, c.G / 2, c.B / 2);
        }

        #endregion
    }
}
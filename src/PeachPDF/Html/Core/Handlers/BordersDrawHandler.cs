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

namespace PeachPDF.Html.Core.Handlers
{
    /// <summary>
    /// Resolves CSS border values and fragment participation, then delegates ordinary box-edge
    /// painting to <see cref="BoxEdgesDrawHandler"/>. Collapsed table segments remain here because
    /// they are grid-line stripes rather than the four connected edges of a box.
    /// </summary>
    internal static class BordersDrawHandler
    {
        /// <summary>
        /// Draws all borders of the box with respect to style, width, fragment edges, and collapsed
        /// border suppression.
        /// </summary>
        public static void DrawBoxBorders(
            RGraphics g, CssBox box, RRect rect,
            bool hasLeftEdge, bool hasRightEdge, bool hasTopEdge = true, bool hasBottomEdge = true)
        {
            if (rect is not { Width: > 0, Height: > 0 }) return;

            // A collapse participant's own stroke on an edge CollapsedBorderModel resolved is drawn
            // once later from CssBox.CollapsedBorderSegments (issue #735).
            var suppressed = box.SuppressedBorderEdges;
            var edges = new BoxEdgesDrawHandler.EdgeSet(
                new BoxEdgesDrawHandler.Edge(
                    box.ActualBorderTopWidth, box.BorderTopStyle.Value, box.ActualBorderTopColor,
                    hasTopEdge, hasTopEdge && !suppressed.HasFlag(BorderEdges.Top)),
                new BoxEdgesDrawHandler.Edge(
                    box.ActualBorderRightWidth, box.BorderRightStyle.Value, box.ActualBorderRightColor,
                    hasRightEdge, hasRightEdge && !suppressed.HasFlag(BorderEdges.Right)),
                new BoxEdgesDrawHandler.Edge(
                    box.ActualBorderBottomWidth, box.BorderBottomStyle.Value, box.ActualBorderBottomColor,
                    hasBottomEdge, hasBottomEdge && !suppressed.HasFlag(BorderEdges.Bottom)),
                new BoxEdgesDrawHandler.Edge(
                    box.ActualBorderLeftWidth, box.BorderLeftStyle.Value, box.ActualBorderLeftColor,
                    hasLeftEdge, hasLeftEdge && !suppressed.HasFlag(BorderEdges.Left)));

            BoxEdgesDrawHandler.DrawBoxEdges(
                g, rect, edges, box.ComputeRadii(rect),
                avoidGeometryAntialias: box.HtmlContainer is not { AvoidGeometryAntialias: false });
        }

        /// <summary>
        /// Draws one CSS 2.1 §17.6.2 collapsed-border segment. A collapsed segment butts against
        /// neighboring grid lines and therefore has no box corner or mitre of its own.
        /// </summary>
        internal static void DrawCollapsedSegment(
            RGraphics g, bool isHorizontal, RRect rect,
            LineStyle style, RColor color, double width)
        {
            if (rect is not { Width: > 0, Height: > 0 } || width <= 0 ||
                style is LineStyle.None or LineStyle.Hidden) return;

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

                    var start = isHorizontal ? rect.Left : rect.Top;
                    var end = isHorizontal ? rect.Right : rect.Bottom;
                    var span = StyledStrokeFitting.Apply(
                        pen, style == LineStyle.Dotted, width, start, end, g.PixelsPerPoint);
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
                    // A segment has no owning box to shade "into", so use the top/left convention.
                    var resolvedColor = style is LineStyle.Inset or LineStyle.Outset
                        ? BorderBevelColors.ForSegment(
                            color, isHorizontal, inset: style == LineStyle.Inset)
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

        private static void DrawDoubleOrGrooveRidgeSegment(
            RGraphics g, bool isHorizontal, RRect rect,
            LineStyle style, RColor color, double width)
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

        private static void DrawSegmentBand(
            RGraphics g, bool isHorizontal, RRect rect,
            RColor color, double from, double to)
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
    }
}

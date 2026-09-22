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
        /// <param name="g">The graphics to paint on.</param>
        /// <param name="isHorizontal">
        /// Whether the segment's rect is physically wide rather than tall - which physical primitive to
        /// draw, not the grid line's topology: a row-boundary line is <c>false</c> under a vertical
        /// writing mode, where rows stack along physical X.
        /// </param>
        /// <param name="rect">The segment's own rect, already in paint space.</param>
        /// <param name="style">The resolved line style.</param>
        /// <param name="color">The resolved line color.</param>
        /// <param name="width">The segment's thickness across its run.</param>
        /// <param name="side">
        /// The box edge this stroke <em>is</em>, for a caller that paints a real box's four edges
        /// through this primitive (<c>MarginBoxRenderer.PaintBorder</c>), or <c>null</c> for a table
        /// grid line, which belongs to the boxes on both sides of it and so has no single side. It
        /// decides the bevelled styles only: a box edge takes one face the way
        /// <see cref="BoxEdgesDrawHandler"/> would, a grid line shows both - see
        /// <see cref="BorderBevelColors.ForSegment"/>.
        /// </param>
        internal static void DrawCollapsedSegment(
            RGraphics g, bool isHorizontal, RRect rect,
            LineStyle style, RColor color, double width, Border? side)
        {
            if (rect is not { Width: > 0, Height: > 0 } || width <= 0 ||
                style is LineStyle.None or LineStyle.Hidden) return;

            switch (style)
            {
                case LineStyle.Double or LineStyle.Groove or LineStyle.Ridge:
                // A grid line shows both bevel faces, one per half, so inset/outset paint as two bands
                // here exactly as groove/ridge do - Chrome renders the two pairs identically (#1237). A
                // real box edge takes a single face instead, so it fails this section's guard and is
                // handled by the solid arm below (C# switch sections never fall through).
                case LineStyle.Inset or LineStyle.Outset when side is null:
                    DrawBandedSegment(g, isHorizontal, rect, style, color, width, side);
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
                    // Only reached for a bevel when the caller named a real side (see the case above).
                    var resolvedColor = (style is LineStyle.Inset or LineStyle.Outset) && side is { } beveledSide
                        ? BorderBevelColors.ForSide(color, beveledSide, inset: style == LineStyle.Inset)
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
        /// Draws a segment that is two bands rather than one fill: <c>double</c>'s two strokes, and the
        /// two faces of <c>groove</c>/<c>ridge</c> - and of <c>inset</c>/<c>outset</c> on a grid line,
        /// which shows both faces the same way.
        /// </summary>
        private static void DrawBandedSegment(
            RGraphics g, bool isHorizontal, RRect rect,
            LineStyle style, RColor color, double width, Border? side)
        {
            double bandWidth;
            RColor leadingColor;
            RColor trailingColor;

            if (style == LineStyle.Double)
            {
                bandWidth = width / 3;
                leadingColor = trailingColor = color;
            }
            else
            {
                bandWidth = width / 2;

                // A grid line's leading half is the bottom/right face and its trailing half the top/left
                // one, whatever the style's own two halves would be on a box - which makes a collapsed
                // inset identical to a ridge, and an outset to a groove, as in Chrome (#1237).
                if (side is not { } boxSide)
                {
                    (leadingColor, trailingColor) = BorderBevelColors.ForSegment(
                        color, inset: style is LineStyle.Inset or LineStyle.Ridge);
                }
                else
                {
                    // On a real box edge the outer half of a groove is its inset face, and a ridge's its
                    // outset one - and the outer half is the leading band on a top/left edge, the
                    // trailing one on a bottom/right edge.
                    var outerIsInset = style == LineStyle.Groove;
                    var outerColor = BorderBevelColors.ForSide(color, boxSide, outerIsInset);
                    var innerColor = BorderBevelColors.ForSide(color, boxSide, !outerIsInset);
                    var outerLeads = boxSide is Border.Top or Border.Left;

                    leadingColor = outerLeads ? outerColor : innerColor;
                    trailingColor = outerLeads ? innerColor : outerColor;
                }
            }

            DrawSegmentBand(g, isHorizontal, rect, leadingColor, 0, bandWidth);
            DrawSegmentBand(g, isHorizontal, rect, trailingColor, width - bandWidth, width);
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

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
    /// Paints a rectangle's already-resolved edges. It knows nothing about CSS borders or outlines;
    /// those callers independently resolve geometry, widths, styles, colors, fragment edges, and
    /// suppression before delegating here.
    /// </summary>
    internal static class BoxEdgesDrawHandler
    {
        private const double Epsilon = 1e-6;

        internal readonly record struct Edge(
            double Width, LineStyle Style, RColor Color, bool IsPhysical, bool IsPainted)
        {
            internal bool IsActive =>
                IsPhysical && IsPainted && Width > 0 && Style is not (LineStyle.None or LineStyle.Hidden);
        }

        internal readonly record struct EdgeSet(Edge Top, Edge Right, Edge Bottom, Edge Left)
        {
            internal Edge Get(Border side) => side switch
            {
                Border.Top => Top,
                Border.Right => Right,
                Border.Bottom => Bottom,
                Border.Left => Left,
                _ => throw new ArgumentOutOfRangeException(nameof(side))
            };

            internal static EdgeSet Uniform(
                double width, LineStyle style, RColor color,
                bool hasLeftEdge, bool hasRightEdge, bool hasTopEdge, bool hasBottomEdge) =>
                new(
                    new Edge(width, style, color, hasTopEdge, hasTopEdge),
                    new Edge(width, style, color, hasRightEdge, hasRightEdge),
                    new Edge(width, style, color, hasBottomEdge, hasBottomEdge),
                    new Edge(width, style, color, hasLeftEdge, hasLeftEdge));
        }

        private static readonly Border[] EdgePaintOrder =
            [Border.Top, Border.Left, Border.Bottom, Border.Right];

        /// <summary>
        /// Draws the active edges inward from <paramref name="outerRect"/> using one shared style,
        /// color, and width.
        /// </summary>
        internal static void DrawBoxEdges(
            RGraphics g, RRect outerRect, LineStyle style, RColor color, double width,
            bool hasLeftEdge, bool hasRightEdge, bool hasTopEdge, bool hasBottomEdge,
            BorderRadii? outerRadii = null)
        {
            DrawBoxEdges(
                g, outerRect,
                EdgeSet.Uniform(
                    width, style, color,
                    hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge),
                outerRadii);
        }

        /// <summary>
        /// Draws four independently resolved edges. This is the common paint entry point for borders
        /// and outlines; differences between those CSS features have already been reduced to values.
        /// </summary>
        internal static void DrawBoxEdges(
            RGraphics g, RRect outerRect, EdgeSet edges, BorderRadii? outerRadii = null,
            bool avoidGeometryAntialias = false)
        {
            if (outerRect is not { Width: > 0, Height: > 0 }) return;

            if (TryDrawSeamlessBevel(g, outerRect, edges, outerRadii)) return;
            if (TryDrawUniformBoxEdges(g, outerRect, edges, outerRadii)) return;

            if (outerRadii is { IsRounded: true } radii)
            {
                DrawGeneralRoundedBoxEdges(g, outerRect, radii, edges, avoidGeometryAntialias);
                return;
            }

            HashSet<RColor>? groupedColors = null;
            foreach (var side in EdgePaintOrder)
            {
                var edge = edges.Get(side);
                if (!edge.IsActive) continue;

                if (edge.Style is (LineStyle.Dotted or LineStyle.Dashed) &&
                    edge.Color.A < byte.MaxValue)
                {
                    if (!(groupedColors ??= new HashSet<RColor>()).Add(edge.Color)) continue;

                    var group = new List<Border>();
                    foreach (var candidate in EdgePaintOrder)
                    {
                        var other = edges.Get(candidate);
                        if (other.IsActive && other.Style is (LineStyle.Dotted or LineStyle.Dashed) &&
                            other.Color == edge.Color)
                            group.Add(candidate);
                    }

                    if (group.Count > 1)
                    {
                        PatternedStrokeOpacity.Paint(g, outerRect, edge.Color, (target, opaque) =>
                        {
                            foreach (var member in group)
                                DrawGeneralPatternedSide(target, member, outerRect, edges, opaque);
                        });
                        continue;
                    }
                }

                DrawGeneralSide(g, side, outerRect, edges);
            }
        }

        /// <summary>
        /// Paints the equal-shade side pairs of a complete rectangular bevel as connected paths.
        /// Painting top and left (or bottom and right) as separate polygons leaves a pale antialiasing
        /// seam along their shared mitre even though that diagonal has no color change.
        /// </summary>
        private static bool TryDrawSeamlessBevel(
            RGraphics g, RRect rect, EdgeSet edges, BorderRadii? outerRadii)
        {
            if (outerRadii is { IsRounded: true }) return false;

            var first = edges.Top;
            if (!BorderBevelColors.IsBeveled(first.Style) ||
                !first.IsActive || !edges.Right.IsActive ||
                !edges.Bottom.IsActive || !edges.Left.IsActive ||
                edges.Right.Style != first.Style || edges.Bottom.Style != first.Style ||
                edges.Left.Style != first.Style ||
                edges.Right.Color != first.Color || edges.Bottom.Color != first.Color ||
                edges.Left.Color != first.Color)
                return false;

            if (first.Style is LineStyle.Inset or LineStyle.Outset)
            {
                DrawSeamlessBevelLayer(
                    g, rect, edges, 0, 1, inset: first.Style == LineStyle.Inset);
            }
            else
            {
                var outerIsInset = first.Style == LineStyle.Groove;
                DrawSeamlessBevelLayer(g, rect, edges, 0, 0.5, outerIsInset);
                DrawSeamlessBevelLayer(g, rect, edges, 0.5, 1, !outerIsInset);
            }

            return true;
        }

        private static void DrawSeamlessBevelLayer(
            RGraphics g, RRect rect, EdgeSet edges,
            double from, double to, bool inset)
        {
            var topLeftColor = BorderBevelColors.ForSide(edges.Top.Color, Border.Top, inset);
            var bottomRightColor = BorderBevelColors.ForSide(edges.Bottom.Color, Border.Bottom, inset);

            if (topLeftColor == bottomRightColor)
            {
                DrawRing(g, rect, edges, from, to, g.GetSolidBrush(topLeftColor));
                return;
            }

            var top = GetGeneralBandPoints(Border.Top, rect, edges, from, to);
            var left = GetGeneralBandPoints(Border.Left, rect, edges, from, to);
            DrawConnectedBand(
                g, topLeftColor,
                [top[0], top[1], top[2], top[3], left[2], left[1]]);

            var bottom = GetGeneralBandPoints(Border.Bottom, rect, edges, from, to);
            var right = GetGeneralBandPoints(Border.Right, rect, edges, from, to);
            DrawConnectedBand(
                g, bottomRightColor,
                [right[0], right[1], bottom[0], bottom[3], bottom[2], right[3]]);
        }

        private static void DrawConnectedBand(
            RGraphics g, RColor color, IReadOnlyList<RPoint> points)
        {
            var scale = 1 / g.PixelsPerPoint;
            using var path = g.GetGraphicsPath();
            path.AddMove(points[0].X * scale, points[0].Y * scale);
            for (var i = 1; i < points.Count; i++)
                path.LineTo(points[i].X * scale, points[i].Y * scale);
            path.CloseFigure();
            g.DrawPath(g.GetSolidBrush(color), path);
        }

        private static bool TryDrawUniformBoxEdges(
            RGraphics g, RRect outerRect, EdgeSet edges, BorderRadii? outerRadii)
        {
            var first = edges.Top;
            if (edges.Right.Style != first.Style || edges.Bottom.Style != first.Style || edges.Left.Style != first.Style ||
                edges.Right.Color != first.Color || edges.Bottom.Color != first.Color || edges.Left.Color != first.Color ||
                first.Width <= 0 || edges.Right.Width <= 0 || edges.Bottom.Width <= 0 || edges.Left.Width <= 0)
                return false;

            var widthsAreUniform =
                Math.Abs(edges.Right.Width - first.Width) <= Epsilon &&
                Math.Abs(edges.Bottom.Width - first.Width) <= Epsilon &&
                Math.Abs(edges.Left.Width - first.Width) <= Epsilon;

            var complete = edges.Top.IsPhysical && edges.Right.IsPhysical &&
                           edges.Bottom.IsPhysical && edges.Left.IsPhysical &&
                           edges.Top.IsPainted && edges.Right.IsPainted &&
                           edges.Bottom.IsPainted && edges.Left.IsPainted;

            if (outerRadii is { IsRounded: true } radii)
            {
                if (!widthsAreUniform || !complete) return false;
                DrawRoundedBoxEdges(g, outerRect, radii, first.Style, first.Color, first.Width);
                return true;
            }

            if (widthsAreUniform)
            {
                // The four square-cornered strokes share their corner squares. Alpha belongs on
                // the completed pattern, not on each stroke (including its antialiased edge pixels).
                if (first.Style is (LineStyle.Dotted or LineStyle.Dashed) &&
                    first.Color.A < byte.MaxValue)
                    return false;

                DrawUniformWidthBoxEdges(
                    g, outerRect, first.Style, first.Color, first.Width,
                    edges.Left.IsActive, edges.Right.IsActive,
                    edges.Top.IsActive, edges.Bottom.IsActive);
                return true;
            }

            if (!complete || first.Style is not (LineStyle.Solid or LineStyle.Double)) return false;

            var brush = g.GetSolidBrush(first.Color);
            if (first.Style == LineStyle.Solid)
            {
                DrawRing(g, outerRect, edges, 0, 1, brush);
            }
            else
            {
                DrawRing(g, outerRect, edges, 0, 1 / 3d, brush);
                DrawRing(g, outerRect, edges, 2 / 3d, 1, brush);
            }

            return true;
        }

        private static void DrawUniformWidthBoxEdges(
            RGraphics g, RRect outerRect, LineStyle style, RColor color, double width,
            bool hasLeftEdge, bool hasRightEdge, bool hasTopEdge, bool hasBottomEdge)
        {
            if (width <= 0 || style is LineStyle.None or LineStyle.Hidden) return;

            var complete = hasLeftEdge && hasRightEdge && hasTopEdge && hasBottomEdge;

            if (complete && (style is LineStyle.Solid or LineStyle.Double))
            {
                var brush = g.GetSolidBrush(color);
                if (style == LineStyle.Solid)
                {
                    DrawRing(g, outerRect, width, 0, 1, brush);
                }
                else
                {
                    DrawRing(g, outerRect, width, 0, 1 / 3d, brush);
                    DrawRing(g, outerRect, width, 2 / 3d, 1, brush);
                }
                return;
            }

            var edges = EdgeSet.Uniform(
                width, style, color,
                hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge);

            if (hasTopEdge) DrawSide(Border.Top);
            if (hasLeftEdge) DrawSide(Border.Left);
            if (hasBottomEdge) DrawSide(Border.Bottom);
            if (hasRightEdge) DrawSide(Border.Right);

            void DrawSide(Border side)
            {
                if (style is LineStyle.Dotted or LineStyle.Dashed)
                {
                    DrawPatternedSide(g, side, outerRect, style, color, width);
                    return;
                }

                if (style is LineStyle.Double or LineStyle.Groove or LineStyle.Ridge)
                {
                    DrawGeneralDoubleOrBevelSide(g, side, outerRect, edges);
                    return;
                }

                var sideColor = style switch
                {
                    LineStyle.Inset => BorderBevelColors.ForSide(color, side, inset: true),
                    LineStyle.Outset => BorderBevelColors.ForSide(color, side, inset: false),
                    _ => color
                };
                DrawGeneralBand(g, side, outerRect, edges, 0, 1, sideColor);
            }
        }

        private static void DrawGeneralRoundedBoxEdges(
            RGraphics g, RRect rect, BorderRadii radii, EdgeSet edges,
            bool avoidGeometryAntialias)
        {
            var physical = new EdgeSides(
                edges.Top.IsPhysical, edges.Right.IsPhysical,
                edges.Bottom.IsPhysical, edges.Left.IsPhysical);
            var active = new EdgeSides(
                edges.Top.IsActive, edges.Right.IsActive,
                edges.Bottom.IsActive, edges.Left.IsActive);
            if (!active.Any) return;

            var widths = new EdgeWidths(
                active.Top ? edges.Top.Width : 0,
                active.Right ? edges.Right.Width : 0,
                active.Bottom ? edges.Bottom.Width : 0,
                active.Left ? edges.Left.Width : 0);

            Object? previousMode = null;
            if (!avoidGeometryAntialias)
                previousMode = g.SetAntiAliasSmoothingMode();

            try
            {
                DrawRoundedFilledLayer(g, rect, radii, edges, physical, active, widths, innerLayer: false);
                DrawRoundedFilledLayer(g, rect, radii, edges, physical, active, widths, innerLayer: true);

                foreach (var side in EdgePaintOrder)
                {
                    var edge = edges.Get(side);
                    if (edge.IsActive && edge.Style is LineStyle.Dotted or LineStyle.Dashed)
                        DrawGeneralRoundedPatternedSide(
                            g, rect, radii, edges, physical, active, widths, side);
                }
            }
            finally
            {
                g.ReturnPreviousSmoothingMode(previousMode);
            }
        }

        /// <summary>
        /// Fills one layer of every non-patterned rounded side, grouping same-colored pieces into one
        /// path so abutting pieces composite once and cannot leave an antialiasing seam.
        /// </summary>
        private static void DrawRoundedFilledLayer(
            RGraphics g, RRect rect, BorderRadii radii, EdgeSet edges,
            EdgeSides physical, EdgeSides active, EdgeWidths widths, bool innerLayer)
        {
            var angles = CornerAngles.From(widths);
            List<(RColor Color, RGraphicsPath Path)>? groups = null;

            try
            {
                foreach (var side in EdgePaintOrder)
                {
                    var edge = edges.Get(side);
                    if (!edge.IsActive ||
                        !TryGetRoundedFillBand(edge, side, innerLayer, out var from, out var to, out var color))
                        continue;

                    var groupIndex = -1;
                    if (groups is not null)
                    {
                        for (var i = 0; i < groups.Count; i++)
                        {
                            if (groups[i].Color == color)
                            {
                                groupIndex = i;
                                break;
                            }
                        }
                    }

                    groups ??= new List<(RColor, RGraphicsPath)>(4);
                    if (groupIndex < 0)
                    {
                        groupIndex = groups.Count;
                        groups.Add((color, g.GetGraphicsPath()));
                    }

                    var outer = CreateRoundedContour(rect, radii, widths, from, g.PixelsPerPoint);
                    var inner = CreateRoundedContour(rect, radii, widths, to, g.PixelsPerPoint);
                    AddRoundedBandSide(groups[groupIndex].Path, side, outer, inner, physical, active, angles);
                }

                if (groups is not null)
                {
                    foreach (var (color, path) in groups)
                        g.DrawPath(g.GetSolidBrush(color), path);
                }
            }
            finally
            {
                if (groups is not null)
                {
                    foreach (var (_, path) in groups)
                        path.Dispose();
                }
            }
        }

        private static bool TryGetRoundedFillBand(
            Edge edge, Border side, bool innerLayer,
            out double from, out double to, out RColor color)
        {
            switch (edge.Style)
            {
                case LineStyle.Solid:
                    if (innerLayer) break;
                    from = 0;
                    to = 1;
                    color = edge.Color;
                    return true;

                case LineStyle.Inset or LineStyle.Outset:
                    if (innerLayer) break;
                    from = 0;
                    to = 1;
                    color = BorderBevelColors.ForSide(
                        edge.Color, side, inset: edge.Style == LineStyle.Inset);
                    return true;

                case LineStyle.Double:
                    from = innerLayer ? 2 / 3d : 0;
                    to = innerLayer ? 1 : 1 / 3d;
                    color = edge.Color;
                    return true;

                case LineStyle.Groove or LineStyle.Ridge:
                    from = innerLayer ? 0.5 : 0;
                    to = innerLayer ? 1 : 0.5;
                    var isInset = innerLayer != (edge.Style == LineStyle.Groove);
                    color = BorderBevelColors.ForSide(edge.Color, side, isInset);
                    return true;
            }

            from = to = 0;
            color = RColor.Empty;
            return false;
        }

        private static void DrawGeneralRoundedPatternedSide(
            RGraphics g, RRect rect, BorderRadii radii, EdgeSet edges,
            EdgeSides physical, EdgeSides active, EdgeWidths widths, Border side)
        {
            var edge = edges.Get(side);
            var angles = CornerAngles.From(widths);
            var outer = CreateRoundedContour(rect, radii, widths, 0, g.PixelsPerPoint);
            var center = CreateRoundedContour(rect, radii, widths, 0.5, g.PixelsPerPoint);
            var inner = CreateRoundedContour(rect, radii, widths, 1, g.PixelsPerPoint);

            using var path = g.GetGraphicsPath();
            var closed = physical is { Top: true, Right: true, Bottom: true, Left: true };
            var pathLength = closed
                ? AddRoundedContourCenterline(path, center)
                : AddRoundedSideCenterline(path, side, center, physical, active, angles);

            using var clip = g.GetGraphicsPath();
            AddRoundedBandSide(clip, side, outer, inner, physical, active, angles);

            g.PushClip(clip);
            try
            {
                var pen = GetRoundedPatternPen(
                    g, edge.Style, edge.Color, edge.Width, pathLength, closed);
                g.DrawPath(pen, path);
            }
            finally
            {
                g.PopClip();
            }
        }

        private static RoundedContour CreateRoundedContour(
            RRect rect, BorderRadii radii, EdgeWidths widths,
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

        /// <summary>
        /// Paints a complete uniform edge set around a rounded rectangle. Single-color styles use one
        /// continuous closed stroke; bevel styles build corner-mitred curved side bands so their
        /// intentional per-side colors meet without flattening either contour.
        /// </summary>
        private static void DrawRoundedBoxEdges(
            RGraphics g, RRect rect, BorderRadii radii, LineStyle style, RColor color, double width)
        {
            switch (style)
            {
                case LineStyle.Solid:
                case LineStyle.Dotted:
                case LineStyle.Dashed:
                    DrawRoundedStroke(g, rect, radii, style, color, width, width / 2);
                    return;

                case LineStyle.Double:
                {
                    var lineWidth = width / 3;
                    DrawRoundedStroke(g, rect, radii, LineStyle.Solid, color, lineWidth, lineWidth / 2);
                    DrawRoundedStroke(g, rect, radii, LineStyle.Solid, color, lineWidth, width - lineWidth / 2);
                    return;
                }

                case LineStyle.Inset:
                case LineStyle.Outset:
                    DrawRoundedSideBand(
                        g, TopLeftSides, rect, radii,
                        BorderBevelColors.ForSide(color, Border.Top, inset: style == LineStyle.Inset),
                        width, 0, 1);
                    DrawRoundedSideBand(
                        g, BottomRightSides, rect, radii,
                        BorderBevelColors.ForSide(color, Border.Bottom, inset: style == LineStyle.Inset),
                        width, 0, 1);
                    return;

                case LineStyle.Groove:
                case LineStyle.Ridge:
                    var outerIsInset = style == LineStyle.Groove;
                    DrawRoundedSideBand(
                        g, TopLeftSides, rect, radii,
                        BorderBevelColors.ForSide(color, Border.Top, outerIsInset),
                        width, 0, 0.5);
                    DrawRoundedSideBand(
                        g, BottomRightSides, rect, radii,
                        BorderBevelColors.ForSide(color, Border.Bottom, outerIsInset),
                        width, 0, 0.5);
                    DrawRoundedSideBand(
                        g, TopLeftSides, rect, radii,
                        BorderBevelColors.ForSide(color, Border.Top, !outerIsInset),
                        width, 0.5, 1);
                    DrawRoundedSideBand(
                        g, BottomRightSides, rect, radii,
                        BorderBevelColors.ForSide(color, Border.Bottom, !outerIsInset),
                        width, 0.5, 1);
                    return;
            }
        }

        private static readonly Border[] TopLeftSides = [Border.Top, Border.Left];
        private static readonly Border[] BottomRightSides = [Border.Bottom, Border.Right];

        private static void DrawRoundedSideBand(
            RGraphics g, Border[] sides, RRect rect, BorderRadii radii, RColor color,
            double width, double from, double to)
        {
            var outer = CreateRoundedContour(rect, radii, width, from, g.PixelsPerPoint);
            var inner = CreateRoundedContour(rect, radii, width, to, g.PixelsPerPoint);

            // Do not clip a complete rounded ring with rectangular side wedges here. PDF path clipping
            // makes those wedges observable at the inner corners, flattening groove/ridge/inset/outset
            // into the square contour that prompted this implementation. Build the actual curved side
            // bands instead, using the same corner-transition geometry as rounded borders.
            using var path = g.GetGraphicsPath();
            var physical = new EdgeSides(true, true, true, true);
            var widths = new EdgeWidths(width, width, width, width);
            var angles = CornerAngles.From(widths);
            foreach (var side in sides)
                AddRoundedBandSide(path, side, outer, inner, physical, physical, angles);
            g.DrawPath(g.GetSolidBrush(color), path);
        }

        private static RoundedContour CreateRoundedContour(
            RRect rect, BorderRadii radii, double width, double fraction, double pixelsPerPoint) =>
            CreateRoundedContour(
                rect, radii, new EdgeWidths(width, width, width, width), fraction, pixelsPerPoint);

        private static double Ratio(double available, double required) =>
            required > 0 ? Math.Max(0, available) / required : 1;

        private static void AddRoundedBandSide(
            RGraphicsPath path, Border side, RoundedContour outer, RoundedContour inner,
            EdgeSides physical, EdgeSides active, CornerAngles angles)
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

        private static double AddRoundedSideCenterline(
            RGraphicsPath path, Border side, RoundedContour center,
            EdgeSides physical, EdgeSides active, CornerAngles angles)
        {
            const double QuarterTurn = Math.PI / 2;
            const double HalfTurn = Math.PI;
            const double ThreeQuarterTurn = 3 * Math.PI / 2;
            const double FullTurn = 2 * Math.PI;

            var current = default(RPoint);
            var length = 0d;

            void Move(RPoint point)
            {
                path.AddMove(point.X, point.Y);
                current = point;
            }

            void MoveCorner(RGraphicsPath.Corner corner, double angle) =>
                Move(PointOnCorner(center, corner, angle));

            void Line(RPoint point)
            {
                var dx = point.X - current.X;
                var dy = point.Y - current.Y;
                length += Math.Sqrt(dx * dx + dy * dy);
                path.LineTo(point.X, point.Y);
                current = point;
            }

            void LineCorner(RGraphicsPath.Corner corner, double angle) =>
                Line(PointOnCorner(center, corner, angle));

            void Arc(RGraphicsPath.Corner corner, double startAngle, double endAngle)
            {
                AddCornerArc(path, center, corner, startAngle, endAngle);
                var (_, _, radiusX, radiusY) = CornerGeometry(center, corner);
                length += StyledStrokeFitting.EllipseArcLength(radiusX, radiusY, startAngle, endAngle);
                current = PointOnCorner(center, corner, endAngle);
            }

            switch (side)
            {
                case Border.Top:
                    if (physical.Left)
                    {
                        var startAngle = active.Left ? angles.TopLeft : HalfTurn;
                        MoveCorner(RGraphicsPath.Corner.TopLeft, startAngle);
                        Arc(RGraphicsPath.Corner.TopLeft, startAngle, ThreeQuarterTurn);
                    }
                    else
                    {
                        Move(new RPoint(center.Rect.Left, center.Rect.Top));
                    }

                    if (physical.Right)
                    {
                        var endAngle = active.Right ? angles.TopRight : FullTurn;
                        LineCorner(RGraphicsPath.Corner.TopRight, ThreeQuarterTurn);
                        Arc(RGraphicsPath.Corner.TopRight, ThreeQuarterTurn, endAngle);
                    }
                    else
                    {
                        Line(new RPoint(center.Rect.Right, center.Rect.Top));
                    }
                    break;

                case Border.Right:
                    if (physical.Top)
                    {
                        var startAngle = active.Top ? angles.TopRight : ThreeQuarterTurn;
                        MoveCorner(RGraphicsPath.Corner.TopRight, startAngle);
                        Arc(RGraphicsPath.Corner.TopRight, startAngle, FullTurn);
                    }
                    else
                    {
                        Move(new RPoint(center.Rect.Right, center.Rect.Top));
                    }

                    if (physical.Bottom)
                    {
                        var endAngle = active.Bottom ? angles.BottomRight : QuarterTurn;
                        LineCorner(RGraphicsPath.Corner.BottomRight, 0);
                        Arc(RGraphicsPath.Corner.BottomRight, 0, endAngle);
                    }
                    else
                    {
                        Line(new RPoint(center.Rect.Right, center.Rect.Bottom));
                    }
                    break;

                case Border.Bottom:
                    if (physical.Left)
                    {
                        var startAngle = active.Left ? angles.BottomLeft : HalfTurn;
                        MoveCorner(RGraphicsPath.Corner.BottomLeft, startAngle);
                        Arc(RGraphicsPath.Corner.BottomLeft, startAngle, QuarterTurn);
                    }
                    else
                    {
                        Move(new RPoint(center.Rect.Left, center.Rect.Bottom));
                    }

                    if (physical.Right)
                    {
                        var endAngle = active.Right ? angles.BottomRight : 0;
                        LineCorner(RGraphicsPath.Corner.BottomRight, QuarterTurn);
                        Arc(RGraphicsPath.Corner.BottomRight, QuarterTurn, endAngle);
                    }
                    else
                    {
                        Line(new RPoint(center.Rect.Right, center.Rect.Bottom));
                    }
                    break;

                case Border.Left:
                    if (physical.Top)
                    {
                        var startAngle = active.Top ? angles.TopLeft : ThreeQuarterTurn;
                        MoveCorner(RGraphicsPath.Corner.TopLeft, startAngle);
                        Arc(RGraphicsPath.Corner.TopLeft, startAngle, HalfTurn);
                    }
                    else
                    {
                        Move(new RPoint(center.Rect.Left, center.Rect.Top));
                    }

                    if (physical.Bottom)
                    {
                        var endAngle = active.Bottom ? angles.BottomLeft : QuarterTurn;
                        LineCorner(RGraphicsPath.Corner.BottomLeft, HalfTurn);
                        Arc(RGraphicsPath.Corner.BottomLeft, HalfTurn, endAngle);
                    }
                    else
                    {
                        Line(new RPoint(center.Rect.Left, center.Rect.Bottom));
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(side));
            }

            return length;
        }

        private static double AddRoundedContourCenterline(RGraphicsPath path, RoundedContour center)
        {
            const double QuarterTurn = Math.PI / 2;
            const double HalfTurn = Math.PI;
            const double ThreeQuarterTurn = 3 * Math.PI / 2;
            const double FullTurn = 2 * Math.PI;

            AddMove(path, center, RGraphicsPath.Corner.TopLeft, ThreeQuarterTurn);
            LineTo(path, center, RGraphicsPath.Corner.TopRight, ThreeQuarterTurn);
            AddCornerArc(path, center, RGraphicsPath.Corner.TopRight, ThreeQuarterTurn, FullTurn);
            LineTo(path, center, RGraphicsPath.Corner.BottomRight, 0);
            AddCornerArc(path, center, RGraphicsPath.Corner.BottomRight, 0, QuarterTurn);
            LineTo(path, center, RGraphicsPath.Corner.BottomLeft, QuarterTurn);
            AddCornerArc(path, center, RGraphicsPath.Corner.BottomLeft, QuarterTurn, HalfTurn);
            LineTo(path, center, RGraphicsPath.Corner.TopLeft, HalfTurn);
            AddCornerArc(path, center, RGraphicsPath.Corner.TopLeft, HalfTurn, ThreeQuarterTurn);
            path.CloseFigure();

            var straightLength =
                Math.Max(0, center.Rect.Width - center.TLX - center.TRX) +
                Math.Max(0, center.Rect.Height - center.TRY - center.BRY) +
                Math.Max(0, center.Rect.Width - center.BLX - center.BRX) +
                Math.Max(0, center.Rect.Height - center.BLY - center.TLY);
            var arcLength =
                StyledStrokeFitting.EllipseArcLength(center.TRX, center.TRY, ThreeQuarterTurn, FullTurn) +
                StyledStrokeFitting.EllipseArcLength(center.BRX, center.BRY, 0, QuarterTurn) +
                StyledStrokeFitting.EllipseArcLength(center.BLX, center.BLY, QuarterTurn, HalfTurn) +
                StyledStrokeFitting.EllipseArcLength(center.TLX, center.TLY, HalfTurn, ThreeQuarterTurn);
            return straightLength + arcLength;
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

        private static void AddCornerArc(
            RGraphicsPath path, RoundedContour contour, RGraphicsPath.Corner corner,
            double startAngle, double endAngle)
        {
            var (_, _, radiusX, radiusY) = CornerGeometry(contour, corner);
            var end = PointOnCorner(contour, corner, endAngle);
            if (radiusX <= 0 || radiusY <= 0)
            {
                path.LineTo(end.X, end.Y);
                return;
            }

            var start = PointOnCorner(contour, corner, startAngle);
            var factor = 4d / 3 * Math.Tan((endAngle - startAngle) / 4);
            path.AddBezierTo(
                start.X - factor * radiusX * Math.Sin(startAngle),
                start.Y + factor * radiusY * Math.Cos(startAngle),
                end.X + factor * radiusX * Math.Sin(endAngle),
                end.Y - factor * radiusY * Math.Cos(endAngle),
                end.X,
                end.Y);
        }

        private static RPoint PointOnCorner(
            RoundedContour contour, RGraphicsPath.Corner corner, double angle)
        {
            var (centerX, centerY, radiusX, radiusY) = CornerGeometry(contour, corner);
            return radiusX <= 0 || radiusY <= 0
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

        private readonly record struct EdgeSides(bool Top, bool Right, bool Bottom, bool Left)
        {
            internal bool Any => Top || Right || Bottom || Left;
        }

        private readonly record struct EdgeWidths(
            double Top, double Right, double Bottom, double Left);

        private readonly record struct CornerAngles(
            double TopLeft, double TopRight, double BottomRight, double BottomLeft)
        {
            internal static CornerAngles From(EdgeWidths widths)
            {
                const double quarterTurn = Math.PI / 2;

                static double Split(double horizontal, double vertical) =>
                    horizontal + vertical > Epsilon
                        ? quarterTurn * vertical / (horizontal + vertical)
                        : quarterTurn / 2;

                return new CornerAngles(
                    Math.PI + Split(widths.Top, widths.Left),
                    2 * Math.PI - Split(widths.Top, widths.Right),
                    Split(widths.Bottom, widths.Right),
                    Math.PI - Split(widths.Bottom, widths.Left));
            }
        }

        private static RPen GetRoundedPatternPen(
            RGraphics g, LineStyle style, RColor color, double width, double pathLength, bool closed)
        {
            var pen = g.GetPen(color);
            pen.Width = width / g.PixelsPerPoint;
            pen.LineJoin = RLineJoin.Miter;

            var dotted = style == LineStyle.Dotted;
            var fitted = closed
                ? StyledStrokeFitting.FitClosed(dotted, pen.Width, pathLength)
                : StyledStrokeFitting.Fit(dotted, pen.Width, pathLength);
            if (fitted is not { } pattern)
            {
                pen.LineCap = RLineCap.Butt;
                pen.DashStyle = RDashStyle.Solid;
            }
            else if (dotted)
            {
                pen.LineCap = RLineCap.Round;
                pen.SetDashPattern(
                    [0, pattern.Period],
                    closed ? 0 : pattern.Period - pen.Width / 2);
            }
            else
            {
                pen.LineCap = RLineCap.Butt;
                pen.SetDashPattern([pattern.DashLength, pattern.GapLength], 0);
            }

            return pen;
        }

        private static void DrawRoundedStroke(
            RGraphics g, RRect rect, BorderRadii radii, LineStyle style, RColor color,
            double strokeWidth, double inset)
        {
            var centerRect = RRect.FromLTRB(
                rect.Left + inset, rect.Top + inset,
                rect.Right - inset, rect.Bottom - inset);
            if (centerRect is not { Width: > 0, Height: > 0 } || strokeWidth <= 0) return;

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
                    Math.Max(0, centerRect.Width - tlx - trx) +
                    Math.Max(0, centerRect.Width - blx - brx) +
                    Math.Max(0, centerRect.Height - tly - bly) +
                    Math.Max(0, centerRect.Height - try_ - bry) +
                    (StyledStrokeFitting.EllipsePerimeter(tlx, tly) +
                     StyledStrokeFitting.EllipsePerimeter(trx, try_) +
                     StyledStrokeFitting.EllipsePerimeter(brx, bry) +
                     StyledStrokeFitting.EllipsePerimeter(blx, bly)) / 4;

                var dotted = style == LineStyle.Dotted;
                if (StyledStrokeFitting.FitClosed(dotted, strokeWidth, perimeter) is not { } pattern)
                {
                    pen.LineCap = RLineCap.Butt;
                    pen.DashStyle = RDashStyle.Solid;
                }
                else
                {
                    pen.LineCap = dotted ? RLineCap.Round : RLineCap.Butt;
                    pen.SetDashPattern(
                        dotted
                            ? [0, pattern.Period / g.PixelsPerPoint]
                            : [pattern.DashLength / g.PixelsPerPoint,
                               pattern.GapLength / g.PixelsPerPoint],
                        0);
                }
            }

            using var path = RenderUtils.GetRoundRect(
                g, centerRect, tlx, tly, trx, try_, brx, bry, blx, bly);
            g.DrawPath(pen, path);
        }

        private static void DrawGeneralSide(
            RGraphics g, Border side, RRect rect, EdgeSet edges)
        {
            var edge = edges.Get(side);
            if (edge.Style is LineStyle.Dotted or LineStyle.Dashed)
            {
                DrawGeneralPatternedSide(g, side, rect, edges);
                return;
            }

            if (edge.Style is LineStyle.Double or LineStyle.Groove or LineStyle.Ridge)
            {
                DrawGeneralDoubleOrBevelSide(g, side, rect, edges);
                return;
            }

            var color = edge.Style switch
            {
                LineStyle.Inset => BorderBevelColors.ForSide(edge.Color, side, inset: true),
                LineStyle.Outset => BorderBevelColors.ForSide(edge.Color, side, inset: false),
                _ => edge.Color
            };
            DrawGeneralBand(g, side, rect, edges, 0, 1, color);
        }

        private static void DrawGeneralDoubleOrBevelSide(
            RGraphics g, Border side, RRect rect, EdgeSet edges)
        {
            var edge = edges.Get(side);
            double outerEnd;
            double innerStart;
            RColor outerColor;
            RColor innerColor;

            if (edge.Style == LineStyle.Double)
            {
                outerEnd = 1 / 3d;
                innerStart = 2 / 3d;
                outerColor = innerColor = edge.Color;
            }
            else
            {
                outerEnd = innerStart = 0.5;
                var outerIsInset = edge.Style == LineStyle.Groove;
                outerColor = BorderBevelColors.ForSide(edge.Color, side, outerIsInset);
                innerColor = BorderBevelColors.ForSide(edge.Color, side, !outerIsInset);
            }

            DrawGeneralBand(g, side, rect, edges, 0, outerEnd, outerColor);
            DrawGeneralBand(g, side, rect, edges, innerStart, 1, innerColor);
        }

        private static void DrawGeneralBand(
            RGraphics g, Border side, RRect rect, EdgeSet edges,
            double from, double to, RColor color) =>
            g.DrawPolygon(g.GetSolidBrush(color), GetGeneralBandPoints(side, rect, edges, from, to));

        private static RPoint[] GetGeneralBandPoints(
            Border side, RRect rect, EdgeSet edges, double from, double to)
        {
            var edge = edges.Get(side);
            var left = edges.Left.IsPhysical ? edges.Left.Width : 0;
            var right = edges.Right.IsPhysical ? edges.Right.Width : 0;
            var top = edges.Top.IsPhysical ? edges.Top.Width : 0;
            var bottom = edges.Bottom.IsPhysical ? edges.Bottom.Width : 0;

            return side switch
            {
                Border.Top =>
                [
                    new RPoint(rect.Left + from * left, rect.Top + from * edge.Width),
                    new RPoint(rect.Right - from * right, rect.Top + from * edge.Width),
                    new RPoint(rect.Right - to * right, rect.Top + to * edge.Width),
                    new RPoint(rect.Left + to * left, rect.Top + to * edge.Width)
                ],
                Border.Right =>
                [
                    new RPoint(rect.Right - from * edge.Width, rect.Top + from * top),
                    new RPoint(rect.Right - from * edge.Width, rect.Bottom - from * bottom),
                    new RPoint(rect.Right - to * edge.Width, rect.Bottom - to * bottom),
                    new RPoint(rect.Right - to * edge.Width, rect.Top + to * top)
                ],
                Border.Bottom =>
                [
                    new RPoint(rect.Left + from * left, rect.Bottom - from * edge.Width),
                    new RPoint(rect.Right - from * right, rect.Bottom - from * edge.Width),
                    new RPoint(rect.Right - to * right, rect.Bottom - to * edge.Width),
                    new RPoint(rect.Left + to * left, rect.Bottom - to * edge.Width)
                ],
                Border.Left =>
                [
                    new RPoint(rect.Left + from * edge.Width, rect.Top + from * top),
                    new RPoint(rect.Left + from * edge.Width, rect.Bottom - from * bottom),
                    new RPoint(rect.Left + to * edge.Width, rect.Bottom - to * bottom),
                    new RPoint(rect.Left + to * edge.Width, rect.Top + to * top)
                ],
                _ => throw new ArgumentOutOfRangeException(nameof(side))
            };
        }

        private static void DrawGeneralPatternedSide(
            RGraphics g, Border side, RRect rect, EdgeSet edges, RColor? strokeColor = null)
        {
            var edge = edges.Get(side);
            var pen = g.GetPen(strokeColor ?? edge.Color);
            pen.Width = edge.Width / g.PixelsPerPoint;
            pen.LineJoin = RLineJoin.Miter;

            var horizontal = side is Border.Top or Border.Bottom;
            var acrossAxis = side switch
            {
                Border.Top => rect.Top + edge.Width / 2,
                Border.Right => rect.Right - edge.Width / 2,
                Border.Bottom => rect.Bottom - edge.Width / 2,
                Border.Left => rect.Left + edge.Width / 2,
                _ => throw new ArgumentOutOfRangeException(nameof(side))
            };
            var start = horizontal ? rect.Left : rect.Top;
            var end = horizontal ? rect.Right : rect.Bottom;

            if (StyledStrokeFitting.Apply(
                    pen, edge.Style == LineStyle.Dotted, edge.Width,
                    start, end, g.PixelsPerPoint) is { } fitted)
            {
                start = fitted.Start;
                end = fitted.End;
            }
            else
            {
                pen.LineCap = RLineCap.Butt;
                pen.DashStyle = RDashStyle.Solid;
            }

            var (startAdjacent, endAdjacent) = side switch
            {
                Border.Top or Border.Bottom => (Border.Left, Border.Right),
                Border.Left or Border.Right => (Border.Top, Border.Bottom),
                _ => throw new ArgumentOutOfRangeException(nameof(side))
            };
            var clipStart = NeedsPatternCornerClip(edge, edges.Get(startAdjacent));
            var clipEnd = NeedsPatternCornerClip(edge, edges.Get(endAdjacent));

            void DrawStroke()
            {
                if (horizontal)
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
                g, side, rect, edges, edge.Width, clipStart, clipEnd);
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

        private static bool NeedsPatternCornerClip(Edge edge, Edge adjacent) =>
            adjacent.IsActive &&
            (adjacent.Style != edge.Style || adjacent.Color != edge.Color ||
             Math.Abs(adjacent.Width - edge.Width) > Epsilon);

        private static RGraphicsPath CreatePatternCornerClip(
            RGraphics g, Border side, RRect rect, EdgeSet edges, double width,
            bool clipStart, bool clipEnd)
        {
            var points = GetGeneralBandPoints(side, rect, edges, 0, 1);
            var edgeDirection = side is Border.Top or Border.Bottom
                ? new RPoint(1, 0)
                : new RPoint(0, 1);
            var outerDirection = side switch
            {
                Border.Top => new RPoint(0, -1),
                Border.Right => new RPoint(1, 0),
                Border.Bottom => new RPoint(0, 1),
                Border.Left => new RPoint(-1, 0),
                _ => throw new ArgumentOutOfRangeException(nameof(side))
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
            var ppp = g.PixelsPerPoint;
            unscaled.Transform(new RMatrix(1 / ppp, 0, 0, 1 / ppp, 0, 0));
            clip.AddPath(unscaled);
            return clip;
        }

        private static RPoint Offset(RPoint point, RPoint direction, double distance) =>
            new(point.X + direction.X * distance, point.Y + direction.Y * distance);

        private static void DrawPatternedSide(
            RGraphics g, Border side, RRect rect, LineStyle style, RColor color, double width)
        {
            var pen = g.GetPen(color);
            pen.Width = width / g.PixelsPerPoint;
            pen.LineJoin = RLineJoin.Miter;

            var horizontal = side is Border.Top or Border.Bottom;
            var acrossAxis = side switch
            {
                Border.Top => rect.Top + width / 2,
                Border.Right => rect.Right - width / 2,
                Border.Bottom => rect.Bottom - width / 2,
                Border.Left => rect.Left + width / 2,
                _ => throw new ArgumentOutOfRangeException(nameof(side))
            };
            var start = horizontal ? rect.Left : rect.Top;
            var end = horizontal ? rect.Right : rect.Bottom;

            if (StyledStrokeFitting.Apply(
                    pen, style == LineStyle.Dotted, width, start, end, g.PixelsPerPoint) is { } fitted)
            {
                start = fitted.Start;
                end = fitted.End;
            }
            else
            {
                pen.LineCap = RLineCap.Butt;
                pen.DashStyle = RDashStyle.Solid;
            }

            if (horizontal)
                g.DrawLine(pen, start, acrossAxis, end, acrossAxis);
            else
                g.DrawLine(pen, acrossAxis, start, acrossAxis, end);
        }

        private static void DrawRing(
            RGraphics g, RRect rect, double width, double from, double to, RBrush brush) =>
            DrawRectangularRing(
                g, brush,
                GetBandRectangle(rect, width, from),
                GetBandRectangle(rect, width, to));

        private static void DrawRing(
            RGraphics g, RRect rect, EdgeSet edges, double from, double to, RBrush brush) =>
            DrawRectangularRing(
                g, brush,
                GetBandRectangle(rect, edges, from),
                GetBandRectangle(rect, edges, to));

        /// <summary>
        /// Fills a uniform rectangular border or outline as one even-odd path, avoiding antialiasing
        /// seams and double-painted translucent corners from four abutting side polygons.
        /// </summary>
        private static void DrawRectangularRing(
            RGraphics g, RBrush brush, RRect outer, RRect inner)
        {
            // Paths bypass the adapter's coordinate scaling, unlike polygons and lines, so normalize
            // layout-space coordinates here (issue #812).
            var pixelsPerPoint = g.PixelsPerPoint;
            using var path = g.GetGraphicsPath();
            path.FillMode = RFillMode.EvenOdd;
            AddRectangle(path, outer, pixelsPerPoint);
            AddRectangle(path, inner, pixelsPerPoint);
            g.DrawPath(brush, path);
        }

        private static void AddRectangle(
            RGraphicsPath path, RRect rect, double pixelsPerPoint)
        {
            var left = rect.Left / pixelsPerPoint;
            var top = rect.Top / pixelsPerPoint;
            var right = rect.Right / pixelsPerPoint;
            var bottom = rect.Bottom / pixelsPerPoint;

            path.AddMove(left, top);
            path.LineTo(right, top);
            path.LineTo(right, bottom);
            path.LineTo(left, bottom);
            path.CloseFigure();
        }

        private static RRect GetBandRectangle(RRect rect, double width, double fraction) =>
            RRect.FromLTRB(
                rect.Left + fraction * width,
                rect.Top + fraction * width,
                rect.Right - fraction * width,
                rect.Bottom - fraction * width);

        private static RRect GetBandRectangle(RRect rect, EdgeSet edges, double fraction) =>
            RRect.FromLTRB(
                rect.Left + fraction * edges.Left.Width,
                rect.Top + fraction * edges.Top.Width,
                rect.Right - fraction * edges.Right.Width,
                rect.Bottom - fraction * edges.Bottom.Width);
    }
}

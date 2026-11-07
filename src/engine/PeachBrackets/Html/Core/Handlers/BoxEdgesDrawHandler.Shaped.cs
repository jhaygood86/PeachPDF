using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;

namespace PeachBrackets.Html.Handlers
{
    internal static partial class BoxEdgesDrawHandler
    {
        /// <summary>
        /// Paints the edges of a box one of whose corners has a <c>corner-shape</c> other than <c>round</c> (css-borders-4 §3.9.4):
        /// the border is the area between the outer contour and the same contour inset by each side's width, so each band is
        /// filled as an even-odd ring of two shaped paths, clipped to its side's mitred sector. A dotted or dashed side strokes
        /// the shaped centerline instead. The corners use the same K inside and out, which is what the spec's inner contour does.
        /// </summary>
        private static void DrawShapedBoxEdges(
            Canvas g, Rect rect, BorderRadii radii, EdgeSet edges, bool avoidGeometryAntialias)
        {
            static double Width(Edge e) => e.IsActive ? e.Width : 0;
            if (Width(edges.Top) + Width(edges.Right) + Width(edges.Bottom) + Width(edges.Left) <= 0) return;

            if (!avoidGeometryAntialias) g.PushAntiAlias(true);
            try
            {
                // Four identical solid sides are one ring: no sector clips, so no seam where they meet.
                var first = edges.Top;
                var uniformSolid = first.IsActive && first.Style == LineStyle.Solid
                    && edges.Right.IsActive && edges.Bottom.IsActive && edges.Left.IsActive
                    && edges.Right.Style == LineStyle.Solid && edges.Bottom.Style == LineStyle.Solid && edges.Left.Style == LineStyle.Solid
                    && edges.Right.PaintColor == first.PaintColor && edges.Bottom.PaintColor == first.PaintColor && edges.Left.PaintColor == first.PaintColor;
                if (uniformSolid)
                {
                    FillShapedBand(g, rect, radii, edges, 0, 1, g.GetSolidBrush(first.PaintColor));
                    return;
                }

                foreach (var side in EdgePaintOrder)
                {
                    var edge = edges.Get(side);
                    if (!edge.IsActive) continue;

                    using var sector = CreateSideSector(g, rect, radii, edges, side);
                    g.PushClip(sector);
                    try
                    {
                        DrawShapedSide(g, rect, radii, edges, side, edge);
                    }
                    finally
                    {
                        g.PopClip();
                    }
                }
            }
            finally
            {
                if (!avoidGeometryAntialias) g.PopAntiAlias();
            }
        }

        private static void DrawShapedSide(Canvas g, Rect rect, BorderRadii radii, EdgeSet edges, Border side, Edge edge)
        {
            var color = edge.PaintColor;
            switch (edge.Style)
            {
                case LineStyle.Dotted:
                case LineStyle.Dashed:
                    StrokeShapedCenterline(g, rect, radii, edges, edge);
                    return;
                case LineStyle.Double:
                    FillShapedBand(g, rect, radii, edges, 0, 1 / 3d, g.GetSolidBrush(color));
                    FillShapedBand(g, rect, radii, edges, 2 / 3d, 1, g.GetSolidBrush(color));
                    return;
                case LineStyle.Groove:
                case LineStyle.Ridge:
                {
                    var outerIsInset = edge.Style == LineStyle.Groove;
                    FillShapedBand(g, rect, radii, edges, 0, 0.5, g.GetSolidBrush(BorderBevelColors.ForSide(color, side, outerIsInset)));
                    FillShapedBand(g, rect, radii, edges, 0.5, 1, g.GetSolidBrush(BorderBevelColors.ForSide(color, side, !outerIsInset)));
                    return;
                }
                case LineStyle.Inset:
                case LineStyle.Outset:
                    color = BorderBevelColors.ForSide(color, side, edge.Style == LineStyle.Inset);
                    break;
            }

            FillShapedBand(g, rect, radii, edges, 0, 1, g.GetSolidBrush(color));
        }

        /// <summary>Fills the band between the contours at <paramref name="from"/> and <paramref name="to"/> of the way through the borders.</summary>
        private static void FillShapedBand(Canvas g, Rect rect, BorderRadii radii, EdgeSet edges, double from, double to, Brush brush)
        {
            using var path = g.GetGraphicsPath();
            path.FillMode = FillMode.EvenOdd;
            using (var outer = CreateShapedContour(g, rect, radii, edges, from)) path.AddPath(outer);
            using (var inner = CreateShapedContour(g, rect, radii, edges, to)) path.AddPath(inner);
            g.DrawPath(brush, path);
        }

        /// <summary>The border-box contour inset by <paramref name="fraction"/> of each side's width, with the radii reduced by the same insets.</summary>
        private static GraphicsPath CreateShapedContour(Canvas g, Rect rect, BorderRadii radii, EdgeSet edges, double fraction)
        {
            double Inset(Edge e) => (e.IsActive ? e.Width : 0) * fraction;
            double left = Inset(edges.Left), top = Inset(edges.Top), right = Inset(edges.Right), bottom = Inset(edges.Bottom);

            var contour = Rect.FromLTRB(rect.Left + left, rect.Top + top, Math.Max(rect.Left + left, rect.Right - right), Math.Max(rect.Top + top, rect.Bottom - bottom));
            // A convex corner follows the inset: its radii shrink with it. A concave one (scoop, notch) keeps the cut centred on the
            // border box's own corner and grows around it, which is what keeps the border as thick along the cut as along the edges.
            double Reduce(double radius, double inset, double k) => k < 0 ? radius : Math.Max(0, radius - inset);
            var reduced = new BorderRadii(
                Reduce(radii.TLX, left, radii.KTL), Reduce(radii.TLY, top, radii.KTL),
                Reduce(radii.TRX, right, radii.KTR), Reduce(radii.TRY, top, radii.KTR),
                Reduce(radii.BRX, right, radii.KBR), Reduce(radii.BRY, bottom, radii.KBR),
                Reduce(radii.BLX, left, radii.KBL), Reduce(radii.BLY, bottom, radii.KBL))
                .WithShapes(radii.KTL, radii.KTR, radii.KBR, radii.KBL);

            var ppp = g.PixelsPerPoint;
            var scaled = new BorderRadii(reduced.TLX / ppp, reduced.TLY / ppp, reduced.TRX / ppp, reduced.TRY / ppp,
                    reduced.BRX / ppp, reduced.BRY / ppp, reduced.BLX / ppp, reduced.BLY / ppp)
                .WithShapes(reduced.KTL, reduced.KTR, reduced.KBR, reduced.KBL);
            var path = g.GetGraphicsPath();
            CornerShapePath.AddRectangle(path, new Rect(contour.Left / ppp, contour.Top / ppp, contour.Width / ppp, contour.Height / ppp),
                scaled, new CornerOverhang(left / ppp, top / ppp, right / ppp, bottom / ppp));
            return path;
        }

        /// <summary>
        /// The side's mitred sector: from its border-box edge inwards, split from its neighbours along the diagonals that run from each
        /// outer corner through the matching inner corner. The split is carried as deep as the corner shapes reach (never past the middle
        /// of the box), because a shaped corner puts border there too.
        /// </summary>
        private static GraphicsPath CreateSideSector(Canvas g, Rect rect, BorderRadii radii, EdgeSet edges, Border side)
        {
            static double Width(Edge e) => e.IsActive ? e.Width : 0;
            double oL = rect.Left, oT = rect.Top, oR = rect.Right, oB = rect.Bottom;
            double leftW = Width(edges.Left), topW = Width(edges.Top), rightW = Width(edges.Right), bottomW = Width(edges.Bottom);
            var ppp = g.PixelsPerPoint;

            // How far across a side's thickness the diagonal has to be followed: the deepest corner on that side plus the border.
            double VerticalDepth(double reach, double width) => Math.Min(rect.Height / 2, reach + width);
            double HorizontalDepth(double reach, double width) => Math.Min(rect.Width / 2, reach + width);

            (double X, double Y)[] points;
            switch (side)
            {
                case Border.Top:
                {
                    var d = VerticalDepth(Math.Max(radii.TLY, radii.TRY), topW);
                    points = [(oL, oT), (oR, oT), (oR - rightW * d / topW, oT + d), (oL + leftW * d / topW, oT + d)];
                    break;
                }
                case Border.Right:
                {
                    var d = HorizontalDepth(Math.Max(radii.TRX, radii.BRX), rightW);
                    points = [(oR, oT), (oR, oB), (oR - d, oB - bottomW * d / rightW), (oR - d, oT + topW * d / rightW)];
                    break;
                }
                case Border.Bottom:
                {
                    var d = VerticalDepth(Math.Max(radii.BLY, radii.BRY), bottomW);
                    points = [(oR, oB), (oL, oB), (oL + leftW * d / bottomW, oB - d), (oR - rightW * d / bottomW, oB - d)];
                    break;
                }
                default:
                {
                    var d = HorizontalDepth(Math.Max(radii.TLX, radii.BLX), leftW);
                    points = [(oL, oB), (oL, oT), (oL + d, oT + topW * d / leftW), (oL + d, oB - bottomW * d / leftW)];
                    break;
                }
            }

            var path = g.GetGraphicsPath();
            path.AddMove(points[0].X / ppp, points[0].Y / ppp);
            for (var i = 1; i < points.Length; i++) path.LineTo(points[i].X / ppp, points[i].Y / ppp);
            path.CloseFigure();
            return path;
        }

        /// <summary>Strokes the contour halfway through the border with the side's dotted or dashed pattern, fitted to the contour's length.</summary>
        private static void StrokeShapedCenterline(Canvas g, Rect rect, BorderRadii radii, EdgeSet edges, Edge edge)
        {
            using var center = CreateShapedContour(g, rect, radii, edges, 0.5);

            double length = 0;
            foreach (var contour in center.Flatten(0.05))
            {
                IReadOnlyList<PaintPoint> pts = contour.Points;
                for (var i = 1; i < pts.Count; i++)
                    length += Distance(pts[i - 1], pts[i]);
                if (contour.Closed && pts.Count > 1)
                    length += Distance(pts[^1], pts[0]);
            }

            // The pattern is fitted in layout units, the path is in points.
            var pen = GetRoundedPatternPen(g, edge.Style, edge.PaintColor, edge.Width, length * g.PixelsPerPoint, closed: true);
            g.DrawPath(pen, center);
        }

        private static double Distance(PaintPoint a, PaintPoint b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}

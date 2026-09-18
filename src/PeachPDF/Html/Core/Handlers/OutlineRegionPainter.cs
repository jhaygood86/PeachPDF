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
    /// Paints an outline that spans several rectangles as one connected shape, by filling the band just
    /// inside the contours bounding their union rather than drawing a separate ring per rectangle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what CSS Basic User Interface 4 §4 asks for when it says a fragmented box's outline
    /// "should" be drawn as a single connected shape, and what Chromium does: it unions every one of a
    /// box's outline rectangles and takes the boundary of the result, so wherever consecutive
    /// rectangles touch, the edges between them are interior to the region and simply are not part of
    /// any boundary. A wrapped inline whose lines are tall enough to meet - which a border on the
    /// inline is usually enough to cause - therefore gets one outline around the whole run rather than
    /// one closed ring per line.
    /// </para>
    /// <para>
    /// The band is filled as a single path under the nonzero winding rule: each contour contributes its
    /// outer edge, and the same contour inset by the outline width contributes its inner edge wound the
    /// opposite way, so the inset area cancels and only the band remains. Nonzero rather than even-odd
    /// specifically because an inset contour can self-cross where the region is narrower than twice the
    /// outline width - see <see cref="RectilinearRegion.Shrink"/>. There, the reversed sub-loop stops
    /// cancelling and the too-narrow neck fills solid, which is how it should look; under even-odd it
    /// would punch a hole instead.
    /// </para>
    /// </remarks>
    internal static partial class OutlineRegionPainter
    {
        /// <summary>
        /// Fills <paramref name="contours"/>' boundary band, <paramref name="width"/> thick, with
        /// <paramref name="color"/>.
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="contours">the contours bounding the region the outline surrounds</param>
        /// <param name="style">
        /// the outline's style. <see cref="LineStyle.Solid"/> and <see cref="LineStyle.Double"/> are
        /// decided by filled area alone; the patterned and bevelled styles are resolved per boundary
        /// segment - see <see cref="PaintPatternedRegion"/> and <see cref="PaintBevelledRegion"/>.
        /// </param>
        /// <param name="color">the outline's color</param>
        /// <param name="width">the outline's width</param>
        /// <param name="radii">
        /// the box's own corner radii, or <c>null</c> when it has none. Grown per corner so the band
        /// keeps a constant width around one - see <see cref="AddContour"/>.
        /// </param>
        /// <param name="offset">
        /// how far outside the box's border edge the band's inner edge sits, i.e. the resolved
        /// <c>outline-offset</c>. Only used to grow <paramref name="radii"/>.
        /// </param>
        internal static void DrawRegionOutline(
            RGraphics g, IReadOnlyList<RectilinearRegion.Contour> contours,
            LineStyle style, RColor color, double width, BorderRadii? radii, double offset)
        {
            if (contours.Count == 0 || width <= 0) return;

            if (style is LineStyle.Dotted or LineStyle.Dashed)
            {
                PaintPatternedRegion(g, contours, style, color, width, radii, offset);
                return;
            }

            if (style is LineStyle.Inset or LineStyle.Outset or LineStyle.Groove or LineStyle.Ridge)
            {
                PaintBevelledRegion(g, contours, style, color, width, radii, offset);
                return;
            }

            var brush = g.GetSolidBrush(color);

            if (style == LineStyle.Double)
            {
                // CSS Backgrounds and Borders 3 §4.3: two lines with a gap, together totalling the
                // declared width. Splitting it in equal thirds is what this renderer does for every
                // other `double` edge - see BoxEdgesDrawHandler.
                var third = width / 3;
                FillBand(g, brush, contours, 0, third, width, radii, offset);
                FillBand(g, brush, contours, 2 * third, width, width, radii, offset);
                return;
            }

            FillBand(g, brush, contours, 0, width, width, radii, offset);
        }

        #region Private methods

        /// <summary>
        /// Fills the area between the contours inset by <paramref name="fromInset"/> and the same
        /// contours inset by <paramref name="toInset"/>.
        /// </summary>
        private static void FillBand(
            RGraphics g, RBrush brush, IReadOnlyList<RectilinearRegion.Contour> contours,
            double fromInset, double toInset, double width, BorderRadii? radii, double offset)
        {
            using var path = BuildBandPath(g, contours, fromInset, toInset, width, radii, offset);
            g.DrawPath(brush, path);
        }

        /// <summary>
        /// The band between <paramref name="fromInset"/> and <paramref name="toInset"/>, as a path the
        /// caller owns - so a caller painting the same band several times under different clips builds
        /// its geometry once instead of once per fill.
        /// </summary>
        private static RGraphicsPath BuildBandPath(
            RGraphics g, IReadOnlyList<RectilinearRegion.Contour> contours,
            double fromInset, double toInset, double width, BorderRadii? radii, double offset)
        {
            // Paths bypass the adapter's coordinate scaling, unlike polygons and lines, so normalize
            // layout-space coordinates here (issue #812).
            var pixelsPerPoint = g.PixelsPerPoint;

            var path = g.GetGraphicsPath();
            path.FillMode = RFillMode.Nonzero;

            foreach (var contour in contours)
            {
                AddContour(path, contour, fromInset, width, radii, offset, pixelsPerPoint, reverse: false);
                AddContour(path, contour, toInset, width, radii, offset, pixelsPerPoint, reverse: true);
            }

            return path;
        }

        /// <summary>
        /// Adds <paramref name="contour"/>, inset by <paramref name="inset"/>, as one closed subpath.
        /// </summary>
        /// <remarks>
        /// A corner's radius depends on which way the contour turns there. Travelling with the region
        /// on the right, a clockwise turn rounds a corner the region bulges out of and a
        /// counter-clockwise turn rounds one it is notched into - and those two have to change in
        /// opposite directions as the band is inset, or the band would not stay the same thickness
        /// turning a corner as it is along an edge. So a convex corner's radius shrinks with the inset
        /// and a concave one's grows, both starting from the box's own radius at its border edge.
        /// </remarks>
        private static void AddContour(
            RGraphicsPath path, RectilinearRegion.Contour contour,
            double inset, double width, BorderRadii? radii, double offset,
            double pixelsPerPoint, bool reverse)
        {
            var segments = BuildContourSegments(contour, inset, width, radii, offset);
            EmitSegments(path, segments, pixelsPerPoint, reverse);
        }

        /// <summary>
        /// <paramref name="contour"/>, inset by <paramref name="inset"/> and with its corners rounded,
        /// as the run of straight and curved segments that traces it.
        /// </summary>
        private static List<PathSegment> BuildContourSegments(
            RectilinearRegion.Contour contour,
            double inset, double width, BorderRadii? radii, double offset)
        {
            var (points, corners) = BuildContourCorners(contour, inset, width, radii, offset);
            return corners.Length == 0 ? [] : BuildSegments(points, corners);
        }

        /// <summary>
        /// <paramref name="contour"/>'s right-angled corner points once inset by
        /// <paramref name="inset"/>, each paired with how far its arc reaches back along the two edges
        /// meeting there.
        /// </summary>
        private static (IReadOnlyList<RPoint> Points, Corner[] Corners) BuildContourCorners(
            RectilinearRegion.Contour contour,
            double inset, double width, BorderRadii? radii, double offset)
        {
            var points = RectilinearRegion.Shrink(contour, inset).Points;
            var count = points.Count;
            if (count < 3) return (points, []);

            var corners = new Corner[count];
            for (var i = 0; i < count; i++)
            {
                var previous = points[(i - 1 + count) % count];
                var current = points[i];
                var next = points[(i + 1) % count];
                corners[i] = DescribeCorner(previous, current, next);
            }

            // A convex corner's radius shrinks as the band is inset and a concave one's grows. Both are
            // measured from the box's border edge, which the band's outer edge sits offset + width
            // outside of. Keeping them opposite is what holds the band the same thickness turning a
            // corner as it is along an edge.
            var convexGrowth = offset + width - inset;
            var concaveGrowth = offset + inset;

            for (var i = 0; i < count; i++)
                corners[i] = corners[i].WithRadii(radii, convexGrowth, concaveGrowth);

            FitRadiiToEdges(points, corners);

            return (points, corners);
        }

        /// <summary>
        /// Classifies the corner at <paramref name="current"/> and records which of the box's four
        /// radii applies to it.
        /// </summary>
        private static Corner DescribeCorner(RPoint previous, RPoint current, RPoint next)
        {
            var inX = Math.Sign(current.X - previous.X);
            var inY = Math.Sign(current.Y - previous.Y);
            var outX = Math.Sign(next.X - current.X);
            var outY = Math.Sign(next.Y - current.Y);

            // Positive cross product is a clockwise turn in y-down space, and the region is always on
            // the right of travel, so a clockwise turn is one the region bulges out of.
            var isConvex = inX * outY - inY * outX > 0;

            // The arc's centre lies diagonally off the corner, and which diagonal that is names the
            // corner - the same naming for a notched corner as for a bulging one, since only the
            // direction the arc bows differs between them.
            var towardCenterX = outX - inX;
            var towardCenterY = outY - inY;

            return new Corner(
                new RPoint(inX, inY),
                new RPoint(outX, outY),
                isConvex,
                towardCenterX > 0
                    ? towardCenterY > 0 ? RGraphicsPath.Corner.TopLeft : RGraphicsPath.Corner.BottomLeft
                    : towardCenterY > 0 ? RGraphicsPath.Corner.TopRight : RGraphicsPath.Corner.BottomRight,
                0, 0);
        }

        /// <summary>
        /// Shrinks any pair of radii that would otherwise overrun the edge between them, scaling both
        /// by the same factor so the corners stay in proportion (CSS Backgrounds and Borders 3 §5.1).
        /// </summary>
        private static void FitRadiiToEdges(IReadOnlyList<RPoint> points, Corner[] corners)
        {
            var count = points.Count;

            for (var i = 0; i < count; i++)
            {
                var next = (i + 1) % count;
                var length = Math.Abs(points[next].X - points[i].X) + Math.Abs(points[next].Y - points[i].Y);

                var used = corners[i].LeavingRadius + corners[next].ArrivingRadius;
                if (used <= length || used <= 0) continue;

                var scale = length / used;
                corners[i] = corners[i].ScaleLeaving(scale);
                corners[next] = corners[next].ScaleArriving(scale);
            }
        }

        private static List<PathSegment> BuildSegments(IReadOnlyList<RPoint> points, Corner[] corners)
        {
            const double ArcControlDistance = 0.5522847498307933; // 4/3 * tan(45 degrees / 2)

            var count = points.Count;
            var segments = new List<PathSegment>(count * 2);

            for (var i = 0; i < count; i++)
            {
                var corner = corners[i];
                var current = points[i];

                var arcStart = new RPoint(
                    current.X - corner.ArrivingRadius * corner.In.X,
                    current.Y - corner.ArrivingRadius * corner.In.Y);
                var arcEnd = new RPoint(
                    current.X + corner.LeavingRadius * corner.Out.X,
                    current.Y + corner.LeavingRadius * corner.Out.Y);

                if (segments.Count > 0)
                {
                    var previousEnd = segments[^1].End;
                    if (Distance(previousEnd, arcStart) > 0)
                        segments.Add(PathSegment.Line(previousEnd, arcStart));
                }

                if (corner.ArrivingRadius > 0 || corner.LeavingRadius > 0)
                {
                    segments.Add(PathSegment.Curve(
                        arcStart,
                        new RPoint(
                            arcStart.X + ArcControlDistance * corner.ArrivingRadius * corner.In.X,
                            arcStart.Y + ArcControlDistance * corner.ArrivingRadius * corner.In.Y),
                        new RPoint(
                            arcEnd.X - ArcControlDistance * corner.LeavingRadius * corner.Out.X,
                            arcEnd.Y - ArcControlDistance * corner.LeavingRadius * corner.Out.Y),
                        arcEnd));
                }
                else if (segments.Count == 0)
                {
                    // Nothing has been emitted yet and this corner is square, so the run starts here.
                    segments.Add(PathSegment.Line(arcStart, arcEnd));
                }
            }

            if (segments.Count > 0)
            {
                var last = segments[^1].End;
                var first = segments[0].Start;
                if (Distance(last, first) > 0)
                    segments.Add(PathSegment.Line(last, first));
            }

            return segments;
        }

        /// <summary>
        /// Writes <paramref name="segments"/> into <paramref name="path"/> as one closed subpath,
        /// optionally wound the other way round.
        /// </summary>
        /// <remarks>
        /// Reversing here rather than reversing the points before measuring the corners is what lets
        /// the convex/concave rule above stay written once: it depends on the direction of travel, so a
        /// contour walked backwards would classify every corner the wrong way.
        /// </remarks>
        private static void EmitSegments(
            RGraphicsPath path, List<PathSegment> segments, double pixelsPerPoint, bool reverse)
        {
            if (segments.Count == 0) return;

            RPoint Scale(RPoint point) => new(point.X / pixelsPerPoint, point.Y / pixelsPerPoint);

            if (reverse)
            {
                var start = Scale(segments[^1].End);
                path.AddMove(start.X, start.Y);

                for (var i = segments.Count - 1; i >= 0; i--)
                {
                    var segment = segments[i];
                    var end = Scale(segment.Start);

                    if (segment.IsCurve)
                    {
                        var c1 = Scale(segment.Control2);
                        var c2 = Scale(segment.Control1);
                        path.AddBezierTo(c1.X, c1.Y, c2.X, c2.Y, end.X, end.Y);
                    }
                    else
                    {
                        path.LineTo(end.X, end.Y);
                    }
                }
            }
            else
            {
                var start = Scale(segments[0].Start);
                path.AddMove(start.X, start.Y);

                foreach (var segment in segments)
                {
                    var end = Scale(segment.End);

                    if (segment.IsCurve)
                    {
                        var c1 = Scale(segment.Control1);
                        var c2 = Scale(segment.Control2);
                        path.AddBezierTo(c1.X, c1.Y, c2.X, c2.Y, end.X, end.Y);
                    }
                    else
                    {
                        path.LineTo(end.X, end.Y);
                    }
                }
            }

            path.CloseFigure();
        }

        private static double Distance(RPoint a, RPoint b) =>
            Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        /// <summary>
        /// A corner of a contour: how it is entered and left, whether the region bulges out of it, and
        /// how far back along each of those two edges its arc reaches.
        /// </summary>
        private readonly record struct Corner(
            RPoint In, RPoint Out, bool IsConvex, RGraphicsPath.Corner Which,
            double ArrivingRadius, double LeavingRadius)
        {
            /// <summary>
            /// Resolves this corner's two arc radii from the box's own <paramref name="radii"/>, grown
            /// by <paramref name="convexGrowth"/> or <paramref name="concaveGrowth"/> as appropriate.
            /// </summary>
            /// <remarks>
            /// Which of the box's four corners applies is read off the directions the contour is
            /// entered and left by, exactly as it would be for the box's own border box - a corner the
            /// contour turns through in the same sense the box's top-left corner does takes the
            /// top-left radius, and so on. Every corner a union introduces that the box has no
            /// declared radius for therefore takes zero and stays square, which is right: it is a
            /// corner of the region, not of the box.
            ///
            /// Radii are elliptical, so which of a corner's two radii applies to which of its two arc
            /// ends depends on the direction of the edge that end runs along.
            /// </remarks>
            internal Corner WithRadii(BorderRadii? radii, double convexGrowth, double concaveGrowth)
            {
                if (radii is not { IsRounded: true } declared) return this;

                var (radiusX, radiusY) = Which switch
                {
                    RGraphicsPath.Corner.TopLeft => (declared.TLX, declared.TLY),
                    RGraphicsPath.Corner.TopRight => (declared.TRX, declared.TRY),
                    RGraphicsPath.Corner.BottomRight => (declared.BRX, declared.BRY),
                    _ => (declared.BLX, declared.BLY)
                };

                if (radiusX <= 0 || radiusY <= 0) return this;

                var growth = IsConvex ? convexGrowth : concaveGrowth;
                radiusX = Math.Max(0, radiusX + growth);
                radiusY = Math.Max(0, radiusY + growth);

                return this with
                {
                    ArrivingRadius = In.X != 0 ? radiusX : radiusY,
                    LeavingRadius = Out.X != 0 ? radiusX : radiusY
                };
            }

            internal Corner ScaleArriving(double scale) =>
                this with { ArrivingRadius = ArrivingRadius * scale };

            internal Corner ScaleLeaving(double scale) =>
                this with { LeavingRadius = LeavingRadius * scale };
        }

        private readonly record struct PathSegment(
            RPoint Start, RPoint Control1, RPoint Control2, RPoint End, bool IsCurve)
        {
            internal static PathSegment Line(RPoint start, RPoint end) =>
                new(start, start, end, end, false);

            internal static PathSegment Curve(RPoint start, RPoint c1, RPoint c2, RPoint end) =>
                new(start, c1, c2, end, true);
        }

        #endregion
    }
}

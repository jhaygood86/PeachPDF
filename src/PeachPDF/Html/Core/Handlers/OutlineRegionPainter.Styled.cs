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
    /// The outline styles whose appearance is not decided by filled area alone: the patterned pair
    /// (<c>dotted</c>, <c>dashed</c>) and the bevelled four (<c>inset</c>, <c>outset</c>,
    /// <c>groove</c>, <c>ridge</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both groups are defined per side on an ordinary box - a dash pattern fitted to a side's length,
    /// a bevel lit from a side's direction - and a unioned region has neither four sides nor only
    /// convex corners. Chromium resolves that by dropping the notion of a side entirely and working
    /// from the boundary itself, which is what this reproduces: the region's boundary, inset to the
    /// middle of the band, is walked as an ordered run of directed straight segments, and each segment
    /// is treated the way the corresponding side of a box would be. A segment's <i>direction of
    /// travel</i> is all either group needs - which is well defined on every segment a union can
    /// produce, including the concave ones, where a "side" is not.
    /// </para>
    /// <para>
    /// Travel direction names a side because the boundary is wound with the region on the right (see
    /// <see cref="RectilinearRegion"/>): running rightwards or upwards is a top or left edge, and
    /// running leftwards or downwards is a bottom or right one. That is the same test Chromium makes,
    /// and it is what lets one rule shade every segment of a merged region without asking which
    /// fragment the segment came from.
    /// </para>
    /// </remarks>
    internal static partial class OutlineRegionPainter
    {
        /// <summary>
        /// Strokes <c>dotted</c>/<c>dashed</c> along the middle of the band.
        /// </summary>
        /// <remarks>
        /// Square corners and rounded ones fit the pattern differently, because they have different
        /// things to line a dash up with. A square-cornered boundary is fitted one straight edge at a
        /// time, each edge extended half a width past both of its ends so the corner squares - which
        /// belong to no edge - are still covered, and so a dash lands on every corner. A rounded one
        /// has no corner to land on, so it is fitted to the closed path as a whole, which spaces the
        /// pattern evenly all the way round instead of restarting it at each corner.
        /// </remarks>
        private static void PaintPatternedRegion(
            RGraphics g, IReadOnlyList<RectilinearRegion.Contour> contours,
            LineStyle style, RColor color, double width, BorderRadii? radii, double offset)
        {
            var dotted = style == LineStyle.Dotted;
            var halfWidth = width / 2;
            var isRounded = radii is { IsRounded: true };

            foreach (var contour in contours)
            {
                var center = BuildContourSegments(contour, halfWidth, width, radii, offset);
                if (center.Count == 0) continue;

                if (isRounded)
                    StrokeClosedPattern(g, center, dotted, color, width);
                else
                    StrokeStraightPattern(g, center, dotted, color, width, halfWidth);
            }
        }

        /// <summary>
        /// Strokes the whole closed contour with one pattern fitted to its total length.
        /// </summary>
        private static void StrokeClosedPattern(
            RGraphics g, List<PathSegment> center, bool dotted, RColor color, double width)
        {
            var pixelsPerPoint = g.PixelsPerPoint;

            var pen = g.GetPen(color);
            pen.Width = width / pixelsPerPoint;
            pen.LineJoin = RLineJoin.Miter;

            if (StyledStrokeFitting.FitClosed(dotted, width, MeasureSegments(center)) is not { } pattern)
            {
                // Not reachable from any authored outline today: OutlineDrawHandler.ClampReach holds
                // every rectangle the region is built from at least two outline widths across, so the
                // contour is always long enough for a dash and its gap. Kept because pens are pooled
                // and handed back configured by a previous caller - a fitting that ever does fail has
                // to reset the pen rather than inherit someone else's pattern.
                pen.LineCap = RLineCap.Butt;
                pen.DashStyle = RDashStyle.Solid;
            }
            else if (dotted)
            {
                // A zero-length dash under a round cap is a dot centred on the path.
                pen.LineCap = RLineCap.Round;
                pen.SetDashPattern([0, pattern.Period / pixelsPerPoint], 0);
            }
            else
            {
                pen.LineCap = RLineCap.Butt;
                pen.SetDashPattern(
                    [pattern.DashLength / pixelsPerPoint, pattern.GapLength / pixelsPerPoint], 0);
            }

            using var path = g.GetGraphicsPath();
            EmitSegments(path, center, pixelsPerPoint, reverse: false);
            g.DrawPath(pen, path);
        }

        /// <summary>
        /// Strokes each straight edge of the contour with its own fitted pattern.
        /// </summary>
        private static void StrokeStraightPattern(
            RGraphics g, List<PathSegment> center, bool dotted, RColor color,
            double width, double halfWidth)
        {
            var pixelsPerPoint = g.PixelsPerPoint;

            foreach (var segment in center)
            {
                var horizontal = Math.Abs(segment.End.X - segment.Start.X) > 0;
                var vertical = Math.Abs(segment.End.Y - segment.Start.Y) > 0;

                // BuildSegments opens a square-cornered run with a zero-length segment, and a union can
                // close a contour onto its own start. Neither is an edge.
                if (horizontal == vertical) continue;

                // The corner square at each end belongs to no edge, so both edges reaching it cover it.
                // Overdrawing it in the same colour is what puts a dash on every corner, which is how a
                // patterned border's corners read on an ordinary box too.
                var direction = horizontal
                    ? Math.Sign(segment.End.X - segment.Start.X)
                    : Math.Sign(segment.End.Y - segment.Start.Y);
                var from = (horizontal ? segment.Start.X : segment.Start.Y) - direction * halfWidth;
                var to = (horizontal ? segment.End.X : segment.End.Y) + direction * halfWidth;

                var pen = g.GetPen(color);
                pen.Width = width / pixelsPerPoint;
                pen.LineJoin = RLineJoin.Miter;

                if (StyledStrokeFitting.Apply(pen, dotted, width, from, to, pixelsPerPoint) is { } fitted)
                {
                    from = fitted.Start;
                    to = fitted.End;
                }
                else
                {
                    pen.LineCap = RLineCap.Butt;
                    pen.DashStyle = RDashStyle.Solid;
                }

                if (horizontal)
                    g.DrawLine(pen, from, segment.Start.Y, to, segment.Start.Y);
                else
                    g.DrawLine(pen, segment.Start.X, from, segment.Start.X, to);
            }
        }

        /// <summary>
        /// Fills the band in the two shades of a bevel, choosing each segment's shade from the
        /// direction it travels in.
        /// </summary>
        /// <remarks>
        /// <c>inset</c> and <c>outset</c> shade the whole width in one pass; <c>groove</c> and
        /// <c>ridge</c> are two such passes over half the width each, the inner one lit the opposite
        /// way, which is the same construction <see cref="BoxEdgesDrawHandler"/> gives a box's own
        /// bevelled border.
        /// </remarks>
        private static void PaintBevelledRegion(
            RGraphics g, IReadOnlyList<RectilinearRegion.Contour> contours,
            LineStyle style, RColor color, double width, BorderRadii? radii, double offset)
        {
            if (style is LineStyle.Inset or LineStyle.Outset)
            {
                PaintBevelLayer(
                    g, contours, color, 0, width, width, radii, offset,
                    inset: style == LineStyle.Inset);
                return;
            }

            var outerIsInset = style == LineStyle.Groove;
            var half = width / 2;
            PaintBevelLayer(g, contours, color, 0, half, width, radii, offset, outerIsInset);
            PaintBevelLayer(g, contours, color, half, width, width, radii, offset, !outerIsInset);
        }

        /// <summary>
        /// Fills the band between <paramref name="fromInset"/> and <paramref name="toInset"/> in the
        /// two shades of a bevel, choosing each boundary edge's shade from the direction it travels in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Each shade is painted in a single pass, clipped to every edge that resolves to it at once.
        /// Painting one fill per edge instead would leave a pale antialiased seam wherever two
        /// same-shade fills abut along a mitre - the artifact
        /// <see cref="BoxEdgesDrawHandler.TryDrawSeamlessBevel"/> exists to avoid on an unfragmented
        /// box - and would re-emit the whole band's geometry once per edge.
        /// </para>
        /// <para>
        /// The mitres are built from the region's own right-angled corners rather than from the
        /// rounded path, because a rounded corner's arc has no single direction of travel and so no
        /// shade of its own. The corner's two mitres split its arc down the 45-degree diagonal,
        /// handing each half to the edge it runs into, which is how a rounded bevelled border is
        /// shaded on an unfragmented box too.
        /// </para>
        /// </remarks>
        private static void PaintBevelLayer(
            RGraphics g, IReadOnlyList<RectilinearRegion.Contour> contours, RColor color,
            double fromInset, double toInset, double width, BorderRadii? radii, double offset,
            bool inset)
        {
            var halfWidth = width / 2;

            var lit = g.GetGraphicsPath();
            var shaded = g.GetGraphicsPath();
            lit.FillMode = RFillMode.Nonzero;
            shaded.FillMode = RFillMode.Nonzero;

            try
            {
                var any = false;

                foreach (var contour in contours)
                {
                    var (points, corners) =
                        BuildContourCorners(contour, halfWidth, width, radii, offset);
                    var count = corners.Length;
                    if (count < 3) continue;

                    for (var i = 0; i < count; i++)
                    {
                        var next = (i + 1) % count;
                        var start = points[i];
                        var end = points[next];

                        var dx = Math.Sign(end.X - start.X);
                        var dy = Math.Sign(end.Y - start.Y);
                        if ((dx == 0) == (dy == 0)) continue;

                        // Wound with the region on the right, travelling rightwards or upwards is the
                        // boundary's top-or-left side.
                        var isTopOrLeft = dx > 0 || dy < 0;

                        // Half the width carries the straight run of the band; whichever of this
                        // edge's two corners is rounded most carries the arc, which curves away from
                        // that straight centreline by its own radius.
                        var reach = halfWidth + Math.Max(
                            Radius(corners[i]), Radius(corners[next]));

                        AddMiterClip(
                            isTopOrLeft ? lit : shaded, g.PixelsPerPoint, start, end, dx, dy, reach,
                            corners[i].IsConvex, corners[next].IsConvex);
                        any = true;
                    }
                }

                if (!any) return;

                using var band = BuildBandPath(g, contours, fromInset, toInset, width, radii, offset);

                FillThroughClip(
                    g, band, lit, BorderBevelColors.ForSide(color, Border.Top, inset));
                FillThroughClip(
                    g, band, shaded, BorderBevelColors.ForSide(color, Border.Bottom, inset));
            }
            finally
            {
                lit.Dispose();
                shaded.Dispose();
            }

            static double Radius(Corner corner) =>
                Math.Max(0, Math.Max(corner.ArrivingRadius, corner.LeavingRadius));
        }

        private static void FillThroughClip(RGraphics g, RGraphicsPath band, RGraphicsPath clip, RColor color)
        {
            g.PushClip(clip);
            g.DrawPath(g.GetSolidBrush(color), band);
            g.PopClip();
        }

        /// <summary>
        /// Adds the part of the band that belongs to one boundary edge to <paramref name="clip"/>: its
        /// own span, closed off at each end by the 45-degree mitre it shares with the neighbouring edge.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Measuring <c>s</c> along the edge from its start and <c>h</c> outwards from its centreline,
        /// the edge's territory is bounded by one 45-degree line per end. Which way each leans is the
        /// corner's business and not the edge's: a convex corner is the outside of the turn, so the
        /// band there is longer on the outward side and the mitre opens outwards (<c>s = -h</c> at the
        /// start, <c>s = L + h</c> at the end), while a reflex corner is the inside of the turn and
        /// each mitre leans the other way (<c>s = h</c>, <c>s = L - h</c>). Both edges meeting at a
        /// corner read that one fact the same way, so they cut it along the same line and their
        /// pieces abut exactly - whereas leaning every mitre the convex way, as though the region were
        /// a rectangle, leaves the inner half of every reflex corner in neither edge's territory. On a
        /// wrapped inline that is the notch between two lines, and it shows up as a clean unpainted
        /// diagonal slash across the band.
        /// </para>
        /// <para>
        /// Only the lines have to be exact; how far the clip runs along them need not, because it is
        /// <see cref="BuildBandPath"/> that bounds the paint. <paramref name="reach"/> is therefore
        /// generous - it carries the corner radius as well as half the width, so that the arc a
        /// rounded corner bulges away from the straight centreline stays inside the clip.
        /// </para>
        /// <para>
        /// Where the two mitres lean towards each other they eventually cross, and past the crossing
        /// the territory has turned inside out: drawn to a fixed depth anyway it folds into a bowtie
        /// whose waist cancels under <see cref="RFillMode.Nonzero"/> - another unpainted slash, on any
        /// edge shorter than twice the reach, which the short steps of a staircase routinely are. The
        /// clip is cut off at the crossing instead, which is where the territory genuinely ends.
        /// </para>
        /// </remarks>
        private static void AddMiterClip(
            RGraphicsPath clip, double pixelsPerPoint, RPoint start, RPoint end,
            int dx, int dy, double reach, bool startConvex, bool endConvex)
        {
            // The outward normal is the direction of travel turned a quarter turn anti-clockwise in
            // this renderer's y-down space, i.e. away from the region on the right.
            double nx = dy;
            double ny = -dx;

            var length = Math.Abs(end.X - start.X) + Math.Abs(end.Y - start.Y);

            var startLean = startConvex ? -1 : 1;
            var endLean = endConvex ? 1 : -1;

            var outward = reach;
            var inward = -reach;

            // The mitres converge at a rate of (endLean - startLean) per unit depth, and meet where the
            // edge's remaining span reaches zero.
            var converge = endLean - startLean;
            if (converge > 0) inward = Math.Max(inward, -length / converge);
            else if (converge < 0) outward = Math.Min(outward, -length / converge);

            var scale = 1 / pixelsPerPoint;
            var last = new RPoint(double.NaN, double.NaN);
            var started = false;

            void Add(double s, double h)
            {
                var x = (start.X + dx * s + nx * h) * scale;
                var y = (start.Y + dy * s + ny * h) * scale;

                // Cut off at the crossing the two far vertices coincide, and a repeated point is a
                // zero-length edge some path consumers will not thank us for.
                if (started && Math.Abs(x - last.X) < 1e-9 && Math.Abs(y - last.Y) < 1e-9) return;

                if (started) clip.LineTo(x, y);
                else clip.AddMove(x, y);

                last = new RPoint(x, y);
                started = true;
            }

            Add(startLean * outward, outward);
            Add(length + endLean * outward, outward);
            Add(length + endLean * inward, inward);
            Add(startLean * inward, inward);
            clip.CloseFigure();
        }

        /// <summary>
        /// The length of <paramref name="segments"/>, treating each curve as the quarter ellipse it
        /// was built as - see <see cref="BuildSegments"/>.
        /// </summary>
        private static double MeasureSegments(List<PathSegment> segments)
        {
            var total = 0.0;

            foreach (var segment in segments)
            {
                var dx = segment.End.X - segment.Start.X;
                var dy = segment.End.Y - segment.Start.Y;

                total += segment.IsCurve
                    ? StyledStrokeFitting.EllipsePerimeter(Math.Abs(dx), Math.Abs(dy)) / 4
                    : Math.Sqrt(dx * dx + dy * dy);
            }

            return total;
        }
    }
}

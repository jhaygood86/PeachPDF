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

            // One band fill per contour, clipped to that contour's own edges only. Accumulating
            // every contour's edges into a single shared clip would let one piece's over-wide
            // corner caps repaint another piece's band in the wrong shade wherever two disjoint
            // pieces pass within a radius of each other.
            foreach (var contour in contours)
            {
                var (points, corners) =
                    BuildContourCorners(contour, halfWidth, width, radii, offset);
                var count = corners.Length;
                if (count < 3) continue;

                // The band this layer fills runs between fromInset and toInset, and each of those
                // two contours fits its radii to its own edge lengths - so either one's arcs can run
                // past where this centreline's fitted radii end. The caps below take the furthest
                // reach of the three fittings at each corner, matched by index (Shrink keeps every
                // inset's point count, so the three corner arrays line up).
                var (_, outerCorners) = BuildContourCorners(
                    contour, fromInset, width, radii, offset);
                var (_, innerCorners) = BuildContourCorners(
                    contour, toInset, width, radii, offset);
                var matchOuter = outerCorners.Length == count;
                var matchInner = innerCorners.Length == count;

                var lit = g.GetGraphicsPath();
                var shaded = g.GetGraphicsPath();
                lit.FillMode = RFillMode.Nonzero;
                shaded.FillMode = RFillMode.Nonzero;

                try
                {
                    var edges = new List<BevelEdge>();

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

                        // A corner the three fittings disagree on (one inset collapsed its notch
                        // into a reversed loop) gets a symmetric cap: its shade there is genuinely
                        // ambiguous, and covering both sides keeps the band painted.
                        var agreeStart = (!matchOuter || outerCorners[i].IsConvex == corners[i].IsConvex)
                            && (!matchInner || innerCorners[i].IsConvex == corners[i].IsConvex);
                        var agreeEnd = (!matchOuter || outerCorners[next].IsConvex == corners[next].IsConvex)
                            && (!matchInner || innerCorners[next].IsConvex == corners[next].IsConvex);

                        edges.Add(BevelEdge.Create(
                            start, end, dx, dy, halfWidth, isTopOrLeft,
                            corners[i], corners[next], agreeStart, agreeEnd,
                            MaxLeaving(corners[i], outerCorners, innerCorners, i, matchOuter, matchInner),
                            MaxArriving(corners[i], outerCorners, innerCorners, i, matchOuter, matchInner),
                            MaxArriving(corners[next], outerCorners, innerCorners, next, matchOuter, matchInner),
                            MaxLeaving(corners[next], outerCorners, innerCorners, next, matchOuter, matchInner)));
                    }

                    if (edges.Count == 0) continue;

                    // The lit shade paints first, so a lit territory reaching into a shaded band is
                    // repainted correctly by the shaded fill running after it - but a shaded
                    // territory reaching into a lit band is not. Shaded caps are therefore cut back
                    // against every lit edge's straight strip (recorded here in layout coords, where
                    // every edge is axis-aligned). Cutting never punches a hole: anything cut out of
                    // a shaded cap lay inside a lit strip, so the lit fill paints it.
                    var litStrips = new List<LayoutRect>();
                    foreach (var edge in edges)
                        if (edge.IsLit)
                            litStrips.Add(edge.CentralStrip());

                    foreach (var edge in edges)
                        if (edge.IsLit)
                            edge.Emit(g, lit, cuts: null);

                    foreach (var edge in edges)
                        if (!edge.IsLit)
                            edge.Emit(g, shaded, cuts: litStrips);

                    using var band = BuildBandPath(
                        g, [contour], fromInset, toInset, width, radii, offset);

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
            }
        }

        private static void FillThroughClip(RGraphics g, RGraphicsPath band, RGraphicsPath clip, RColor color)
        {
            g.PushClip(clip);
            g.DrawPath(g.GetSolidBrush(color), band);
            g.PopClip();
        }

        /// <summary>
        /// The furthest <c>leaving</c> radius at one corner over the layer's three fittings - the
        /// centreline's and the band's own outer and inner contours, matched by index (which
        /// <see cref="RectilinearRegion.Shrink"/> keeps lined up). Defaults to the centreline's own
        /// wherever a fitting's point count disagrees.
        /// </summary>
        private static double MaxLeaving(
            Corner own, Corner[] outer, Corner[] inner, int index, bool matchOuter, bool matchInner)
        {
            var max = Math.Max(0, own.LeavingRadius);
            if (matchOuter) max = Math.Max(max, Math.Max(0, outer[index].LeavingRadius));
            if (matchInner) max = Math.Max(max, Math.Max(0, inner[index].LeavingRadius));
            return max;
        }

        /// <summary>
        /// The furthest <c>arriving</c> radius at one corner over the layer's three fittings - see
        /// <see cref="MaxLeaving(Corner, Corner[], Corner[], int, bool, bool)"/>.
        /// </summary>
        private static double MaxArriving(
            Corner own, Corner[] outer, Corner[] inner, int index, bool matchOuter, bool matchInner)
        {
            var max = Math.Max(0, own.ArrivingRadius);
            if (matchOuter) max = Math.Max(max, Math.Max(0, outer[index].ArrivingRadius));
            if (matchInner) max = Math.Max(max, Math.Max(0, inner[index].ArrivingRadius));
            return max;
        }

        /// <summary>An axis-aligned rectangle in layout coords, for cutting shaded caps.</summary>
        private readonly record struct LayoutRect(double X0, double Y0, double X1, double Y1);

        /// <summary>
        /// One boundary edge's bevel territory: its span, its shade, and how far each end's cap
        /// reaches along the edge and across it.
        /// </summary>
        private readonly record struct BevelEdge(
            RPoint Start, RPoint End, int Dx, int Dy,
            bool IsLit, double S0, double S1,
            double StartReach, double EndReach,
            double StartHMin, double StartHMax, double EndHMin, double EndHMax,
            double HalfWidth, int StartLean, int EndLean)
        {
            internal double Length => Math.Abs(End.X - Start.X) + Math.Abs(End.Y - Start.Y);

            // The outward normal is the direction of travel turned a quarter turn anti-clockwise
            // in this renderer's y-down space, i.e. away from the region on the right.
            internal double Nx => Dy;
            internal double Ny => -Dx;

            internal static BevelEdge Create(
                RPoint start, RPoint end, int dx, int dy, double halfWidth, bool isLit,
                Corner startCorner, Corner endCorner, bool agreeStart, bool agreeEnd,
                double startLeaving, double startArriving, double endArriving, double endLeaving)
            {
                var length = Math.Abs(end.X - start.X) + Math.Abs(end.Y - start.Y);
                var startReach = halfWidth + startArriving;
                var endReach = halfWidth + endLeaving;

                return new BevelEdge(
                    start, end, dx, dy, isLit, startLeaving, length - endArriving,
                    startReach, endReach,
                    startCorner.IsConvex || !agreeStart ? -startReach : -halfWidth,
                    !startCorner.IsConvex || !agreeStart ? startReach : halfWidth,
                    endCorner.IsConvex || !agreeEnd ? -endReach : -halfWidth,
                    !endCorner.IsConvex || !agreeEnd ? endReach : halfWidth,
                    halfWidth,
                    startCorner.IsConvex ? -1 : 1,
                    endCorner.IsConvex ? 1 : -1);
            }

            /// <summary>
            /// This edge's straight strip in layout coords: the centreline carried half a width to
            /// either side, but not past its ends - the corner squares past each end belong to the
            /// mitre zone both shades meet along, so cuts stop where they start.
            /// </summary>
            internal LayoutRect CentralStrip() =>
                ToLayout(0, Length, -HalfWidth, HalfWidth);

            /// <summary>Adds this edge's territory pieces to <paramref name="clip"/>.</summary>
            /// <param name="g">the device, for its coordinate scale</param>
            /// <param name="clip">the shade's clip path to append subpaths to</param>
            /// <param name="cuts">
            /// Layout rectangles to punch out of each corner cap, or <c>null</c> for none. Only a
            /// shaded edge's caps take cuts (against every lit edge's
            /// <see cref="CentralStrip"/>): the lit shade paints first, so anything cut out of a
            /// shaded cap is still painted - by the lit fill - while anything left in paints shaded.
            /// </param>
            /// <remarks>
            /// <para>
            /// Measuring <c>s</c> along the edge from its start and <c>h</c> outwards from its
            /// centreline, the edge's territory is bounded by one 45-degree line per end. Which way
            /// each leans is the corner's business and not the edge's: a convex corner is the
            /// outside of the turn, so the band there is longer on the outward side and the mitre
            /// opens outwards (<c>s = -h</c> at the start, <c>s = L + h</c> at the end), while a
            /// reflex corner is the inside of the turn and each mitre leans the other way
            /// (<c>s = h</c>, <c>s = L - h</c>). Both edges meeting at a corner read that one fact
            /// the same way, so they cut it along the same line and their pieces abut exactly -
            /// whereas leaning every mitre the convex way, as though the region were a rectangle,
            /// leaves the inner half of every reflex corner in neither edge's territory. On a
            /// wrapped inline that is the notch between two lines, and it shows up as a clean
            /// unpainted diagonal slash across the band.
            /// </para>
            /// <para>
            /// Every piece is clipped to the wedge between the edge's own two mitres, which is what
            /// keeps neighbouring edges' pieces abutting along the mitre instead of overlapping
            /// across it - and what keeps each uncut piece a simple convex polygon. Where the two
            /// mitres lean towards each other they eventually cross, and past the crossing the
            /// territory has turned inside out; clipping a rectangle to the wedge cuts it off at the
            /// crossing instead, which is where the territory genuinely ends. That is the same cutoff
            /// the fixed-depth quad used to compute directly, on any edge shorter than twice the
            /// reach, which the short steps of a staircase routinely are.
            /// </para>
            /// </remarks>
            internal void Emit(RGraphics g, RGraphicsPath clip, List<LayoutRect>? cuts)
            {
                // The straight middle, mitre tip to mitre tip to hold the corner squares the caps
                // below are cut back from. Never cut: it is this edge's own straight band.
                EmitRect(g, clip, -HalfWidth, Length + HalfWidth, -HalfWidth, HalfWidth, cuts: null);

                // A cap holds its corner's arc, which curves away from the straight centreline onto
                // one side only: inward for a convex corner, outward for a concave one (symmetric
                // where the three fittings disagree on the turn - see PaintBevelLayer). The arc is
                // an elliptical quadrant, so it never leaves the box its tips span: past the
                // straight run (half the width) on the arc side only, up to half the width plus the
                // arriving radius. Covering the other side to the full reach as well would paint the
                // diagonally-opposite quadrant around the corner, which holds no band of this edge
                // - but does hold whatever other edge's band passes within reach behind the corner,
                // repainting it in this edge's shade. Along the edge the cap stays wide past the
                // arc's end, because the outer and inner contours fit their own radii to their own
                // edge lengths and can run past the centreline's.
                // A cap with no radius is already inside the middle, so emitting it would only add
                // a redundant subpath for every square corner.
                if (StartReach > HalfWidth + 1e-9)
                    EmitRect(g, clip, -StartReach, S0, StartHMin, StartHMax, cuts);
                if (EndReach > HalfWidth + 1e-9)
                    EmitRect(g, clip, S1, Length + EndReach, EndHMin, EndHMax, cuts);
            }

            private void EmitRect(
                RGraphics g, RGraphicsPath clip,
                double sMin, double sMax, double hMin, double hMax, List<LayoutRect>? cuts)
            {
                if (sMin >= sMax || hMin >= hMax) return;

                if (cuts is null)
                {
                    EmitPolygon(g, clip, ClipRectToWedge(
                        sMin, sMax, hMin, hMax, Length, StartLean, EndLean));
                    return;
                }

                foreach (var piece in SubtractCuts(ToLayout(sMin, sMax, hMin, hMax), cuts))
                {
                    var (psMin, psMax, phMin, phMax) = FromLayout(piece);
                    if (psMin >= psMax || phMin >= phMax) continue;

                    EmitPolygon(g, clip, ClipRectToWedge(
                        psMin, psMax, phMin, phMax, Length, StartLean, EndLean));
                }
            }

            private void EmitPolygon(
                RGraphics g, RGraphicsPath clip, List<(double S, double H)> polygon)
            {
                if (polygon.Count < 3) return;

                var scale = 1 / g.PixelsPerPoint;
                var last = new RPoint(double.NaN, double.NaN);
                var started = false;
                var vertices = 0;

                foreach (var (s, h) in polygon)
                {
                    var x = (Start.X + Dx * s + Nx * h) * scale;
                    var y = (Start.Y + Dy * s + Ny * h) * scale;

                    // A repeated point is a zero-length edge some path consumers will not thank
                    // us for; the wedge crossing lands exactly on a rect corner often enough
                    // that this fires on real staircase steps, not just in theory.
                    if (started && Math.Abs(x - last.X) < 1e-9 && Math.Abs(y - last.Y) < 1e-9)
                        continue;

                    if (started) clip.LineTo(x, y);
                    else clip.AddMove(x, y);

                    last = new RPoint(x, y);
                    started = true;
                    vertices++;
                }

                if (vertices >= 3) clip.CloseFigure();
            }

            private LayoutRect ToLayout(double sMin, double sMax, double hMin, double hMax)
            {
                var x0 = Start.X + Dx * sMin + Nx * hMin;
                var x1 = Start.X + Dx * sMax + Nx * hMax;
                var y0 = Start.Y + Dy * sMin + Ny * hMin;
                var y1 = Start.Y + Dy * sMax + Ny * hMax;

                return new LayoutRect(
                    Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
            }

            private (double SMin, double SMax, double HMin, double HMax) FromLayout(LayoutRect rect)
            {
                var s0 = Dx != 0
                    ? (Dx > 0 ? rect.X0 - Start.X : Start.X - rect.X1)
                    : (Dy > 0 ? rect.Y0 - Start.Y : Start.Y - rect.Y1);
                var s1 = Dx != 0
                    ? (Dx > 0 ? rect.X1 - Start.X : Start.X - rect.X0)
                    : (Dy > 0 ? rect.Y1 - Start.Y : Start.Y - rect.Y0);
                var h0 = Nx != 0
                    ? (Nx > 0 ? rect.X0 - Start.X : Start.X - rect.X1)
                    : (Ny > 0 ? rect.Y0 - Start.Y : Start.Y - rect.Y1);
                var h1 = Nx != 0
                    ? (Nx > 0 ? rect.X1 - Start.X : Start.X - rect.X0)
                    : (Ny > 0 ? rect.Y1 - Start.Y : Start.Y - rect.Y0);

                return (s0, s1, h0, h1);
            }

            private static List<LayoutRect> SubtractCuts(LayoutRect rect, List<LayoutRect>? cuts)
            {
                var pieces = new List<LayoutRect> { rect };
                if (cuts is null) return pieces;

                foreach (var cut in cuts)
                {
                    var kept = new List<LayoutRect>();
                    foreach (var piece in pieces)
                        kept.AddRange(SubtractOne(piece, cut));
                    pieces = kept;
                    if (pieces.Count == 0) break;
                }

                return pieces;
            }

            private static IEnumerable<LayoutRect> SubtractOne(LayoutRect rect, LayoutRect cut)
            {
                var x0 = Math.Max(rect.X0, cut.X0);
                var x1 = Math.Min(rect.X1, cut.X1);
                var y0 = Math.Max(rect.Y0, cut.Y0);
                var y1 = Math.Min(rect.Y1, cut.Y1);

                // No interior overlap: touching at an edge or a point removes nothing.
                if (x0 >= x1 || y0 >= y1)
                {
                    yield return rect;
                    yield break;
                }

                if (rect.X0 < x0) yield return rect with { X1 = x0 };
                if (x1 < rect.X1) yield return rect with { X0 = x1 };
                if (rect.Y0 < y0) yield return rect with { X0 = x0, X1 = x1, Y1 = y0 };
                if (y1 < rect.Y1) yield return rect with { X0 = x0, X1 = x1, Y0 = y1 };
            }
        }

        /// <summary>
        /// The axis-aligned rectangle in <c>(s, h)</c> space clipped to the wedge between one edge's
        /// own two mitres (<c>s &gt;= startLean * h</c> and <c>s &lt;= length + endLean * h</c>), as the
        /// convex polygon of its surviving corners and wedge crossings, in order.
        /// </summary>
        private static List<(double S, double H)> ClipRectToWedge(
            double sMin, double sMax, double hMin, double hMax,
            double length, int startLean, int endLean)
        {
            var polygon = new List<(double S, double H)>
            {
                (sMin, hMax),
                (sMax, hMax),
                (sMax, hMin),
                (sMin, hMin),
            };

            // Left mitre: keep s - startLean * h >= 0.
            polygon = ClipToHalfPlane(polygon, (s, h) => s - startLean * h);
            if (polygon.Count == 0) return polygon;

            // Right mitre: keep length + endLean * h - s >= 0.
            polygon = ClipToHalfPlane(polygon, (s, h) => length + endLean * h - s);
            return polygon;
        }

        /// <summary>
        /// Sutherland-Hodgman against one half-plane: keeps the part of <paramref name="polygon"/>
        /// where <paramref name="distance"/> is non-negative, inserting the boundary crossing wherever
        /// an edge straddles it.
        /// </summary>
        private static List<(double S, double H)> ClipToHalfPlane(
            List<(double S, double H)> polygon, Func<double, double, double> distance)
        {
            var output = new List<(double S, double H)>(polygon.Count + 1);
            var count = polygon.Count;
            if (count == 0) return output;

            for (var i = 0; i < count; i++)
            {
                var current = polygon[i];
                var next = polygon[(i + 1) % count];
                var currentDistance = distance(current.S, current.H);
                var nextDistance = distance(next.S, next.H);
                var currentInside = currentDistance >= -1e-9;
                var nextInside = nextDistance >= -1e-9;

                if (currentInside)
                    output.Add(current);

                // Straddling edge: emit the crossing after the current point so the order survives.
                if (currentInside != nextInside)
                {
                    var t = currentDistance / (currentDistance - nextDistance);
                    output.Add((
                        current.S + t * (next.S - current.S),
                        current.H + t * (next.H - current.H)));
                }
            }

            return output;
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

using PeachPDF.Html.Adapters.Entities;
using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Unions axis-aligned rectangles into the closed, axis-aligned contours that bound the region
    /// they cover, and shrinks such a contour inward by a uniform inset. This is what lets an outline
    /// spanning several rectangles be painted as one connected shape rather than one ring per
    /// rectangle - see <see cref="Handlers.OutlineDrawHandler"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Chromium builds the same region with an integer <c>SkRegion</c> and takes its boundary path,
    /// which forces every coordinate onto the device pixel grid first. This works on the rectangles'
    /// own coordinates instead: their distinct edge positions are the only grid lines a union of
    /// axis-aligned rectangles can possibly turn on, so tracing that grid is exact in layout space and
    /// needs no device-pixel grid - which this renderer does not otherwise have, writing real-valued
    /// PDF coordinates rather than rasterizing.
    /// </para>
    /// <para>
    /// Contours are emitted with the region's interior consistently on the <b>right</b> of travel: an
    /// outer boundary runs clockwise and a hole runs counter-clockwise (in this renderer's y-down
    /// space). Every consumer depends on that, since it is what makes <see cref="Shrink"/>'s single
    /// corner rule work for holes and reflex corners without a special case, and what lets a ring be
    /// filled under the nonzero winding rule.
    /// </para>
    /// </remarks>
    internal static class RectilinearRegion
    {
        /// <summary>
        /// Coordinates closer together than this are the same grid line. Layout space is PDF points,
        /// so this is far below anything a real box geometry distinguishes.
        /// </summary>
        private const double Epsilon = 1e-6;

        /// <summary>
        /// One closed, axis-aligned contour. Consecutive points are never collinear, so every point is
        /// a real 90-degree corner, and the closing edge runs from the last point back to the first.
        /// </summary>
        /// <param name="Points">the contour's corners, in traversal order</param>
        /// <param name="IsOuter">
        /// whether this contour bounds region (clockwise) rather than a hole in it (counter-clockwise)
        /// </param>
        internal sealed record Contour(IReadOnlyList<RPoint> Points, bool IsOuter);

        /// <summary>
        /// The contours bounding the union of <paramref name="rects"/>. Empty rectangles contribute
        /// nothing. The result may hold several contours - disjoint pieces of the region, and holes
        /// enclosed by it.
        /// </summary>
        internal static IReadOnlyList<Contour> Union(IReadOnlyList<RRect> rects)
        {
            var xs = GridLines(rects, horizontal: true);
            var ys = GridLines(rects, horizontal: false);
            if (xs.Count < 2 || ys.Count < 2) return [];

            var columns = xs.Count - 1;
            var rows = ys.Count - 1;
            var inside = new bool[columns, rows];

            // Every grid line came from some rectangle's edge, so each rectangle spans a whole block of
            // cells exactly: mark that block directly rather than testing every cell against every
            // rectangle, which would cost the cube of the rectangle count on a long wrapped inline.
            foreach (var rect in rects)
            {
                if (rect.Width <= 0 || rect.Height <= 0) continue;

                var firstColumn = IndexOf(xs, rect.Left);
                var lastColumn = IndexOf(xs, rect.Right);
                var firstRow = IndexOf(ys, rect.Top);
                var lastRow = IndexOf(ys, rect.Bottom);

                for (var i = firstColumn; i < lastColumn; i++)
                {
                    for (var j = firstRow; j < lastRow; j++)
                        inside[i, j] = true;
                }
            }

            return TraceContours(inside, xs, ys, columns, rows);
        }

        /// <summary>
        /// The index of the grid line at <paramref name="value"/>. The value is always one of the edge
        /// positions the lines were built from, so this is an exact lookup up to <see cref="Epsilon"/>.
        /// </summary>
        private static int IndexOf(List<double> lines, double value)
        {
            var index = lines.BinarySearch(value);
            if (index >= 0) return index;

            // The edge merged into a neighbouring grid line within Epsilon; ~index is the first line
            // above it, so whichever of it and its predecessor is nearer is that line.
            index = ~index;
            if (index == 0) return 0;
            if (index >= lines.Count) return lines.Count - 1;

            return value - lines[index - 1] <= lines[index] - value ? index - 1 : index;
        }

        /// <summary>
        /// Moves every edge of <paramref name="contour"/> <paramref name="inset"/> toward the region's
        /// interior, keeping all corners at right angles.
        /// </summary>
        /// <remarks>
        /// Each corner is the meeting point of two perpendicular edges, so the shrunk corner is just
        /// the original displaced by both edges' inward normals at once. Because the interior is always
        /// on the right of travel (see the class remarks), "inward" is the same rotation everywhere -
        /// which is what makes this one expression correct for convex corners, for the reflex corners a
        /// union introduces, and for holes alike.
        ///
        /// A notch narrower than twice the inset collapses, and the edges bounding it come back
        /// reversed - deliberately left as-is rather than repaired. Painted as the inner contour of a
        /// ring under the nonzero winding rule, a reversed sub-loop stops cancelling the outer contour
        /// and the collapsed notch fills solid, which is exactly how it should look once the ring is
        /// thicker than the gap it runs through.
        /// </remarks>
        internal static Contour Shrink(Contour contour, double inset)
        {
            var points = contour.Points;
            var count = points.Count;
            var shrunk = new List<RPoint>(count);

            for (var i = 0; i < count; i++)
            {
                var previous = points[(i - 1 + count) % count];
                var current = points[i];
                var next = points[(i + 1) % count];

                var (inX, inY) = InwardNormal(previous, current);
                var (outX, outY) = InwardNormal(current, next);

                shrunk.Add(new RPoint(
                    current.X + (inX + outX) * inset,
                    current.Y + (inY + outY) * inset));
            }

            return new Contour(shrunk, contour.IsOuter);
        }

        /// <summary>
        /// Whether <paramref name="contour"/> is a single rectangle - the case a caller can paint
        /// through its ordinary rectangle geometry instead of as a general region.
        /// </summary>
        internal static bool IsRectangle(Contour contour) => contour.Points.Count == 4;

        /// <summary>
        /// The unit normal pointing into the region from the edge running
        /// <paramref name="from"/>-&gt;<paramref name="to"/>: its direction rotated a quarter turn
        /// clockwise, which in this renderer's y-down space is the right-hand side of travel.
        /// </summary>
        private static (double X, double Y) InwardNormal(RPoint from, RPoint to)
        {
            var dx = Math.Sign(to.X - from.X);
            var dy = Math.Sign(to.Y - from.Y);
            return (-dy, dx);
        }

        /// <summary>
        /// The distinct edge positions of <paramref name="rects"/> along one axis, ascending. A union
        /// of axis-aligned rectangles can only change between covered and uncovered at one of these, so
        /// they are the complete set of grid lines the region needs.
        /// </summary>
        private static List<double> GridLines(IReadOnlyList<RRect> rects, bool horizontal)
        {
            var values = new List<double>(rects.Count * 2);
            foreach (var rect in rects)
            {
                if (rect.Width <= 0 || rect.Height <= 0) continue;

                values.Add(horizontal ? rect.Left : rect.Top);
                values.Add(horizontal ? rect.Right : rect.Bottom);
            }

            values.Sort();

            var distinct = new List<double>(values.Count);
            foreach (var value in values)
            {
                if (distinct.Count == 0 || value - distinct[^1] > Epsilon)
                    distinct.Add(value);
            }

            return distinct;
        }

        /// <summary>
        /// Walks the covered/uncovered grid and chains its boundary into closed contours.
        /// </summary>
        private static List<Contour> TraceContours(
            bool[,] inside, List<double> xs, List<double> ys, int columns, int rows)
        {
            var edges = CollectBoundaryEdges(inside, columns, rows);
            if (edges.Count == 0) return [];

            var stride = columns + 1;
            var outgoing = new Dictionary<int, List<int>>();
            for (var i = 0; i < edges.Count; i++)
            {
                var key = edges[i].FromY * stride + edges[i].FromX;
                if (!outgoing.TryGetValue(key, out var list))
                    outgoing[key] = list = [];
                list.Add(i);
            }

            var used = new bool[edges.Count];
            var contours = new List<Contour>();

            for (var start = 0; start < edges.Count; start++)
            {
                if (used[start]) continue;

                var points = new List<RPoint>();
                var current = start;

                while (!used[current])
                {
                    used[current] = true;
                    var edge = edges[current];
                    points.Add(new RPoint(xs[edge.FromX], ys[edge.FromY]));

                    var key = edge.ToY * stride + edge.ToX;
                    if (!outgoing.TryGetValue(key, out var candidates)) break;

                    var next = SelectNextEdge(edges, candidates, used, edge);
                    if (next < 0) break;

                    current = next;
                }

                var merged = MergeCollinear(points);

                // Fewer than four corners means the contour enclosed no area at all.
                if (merged.Count >= 4)
                    contours.Add(new Contour(merged, IsClockwise(merged)));
            }

            return contours;
        }

        /// <summary>
        /// Every grid cell edge where covered meets uncovered, directed so the covered side is on the
        /// right of travel. Each directed edge is produced exactly once, by the covered cell owning it.
        /// </summary>
        private static List<GridEdge> CollectBoundaryEdges(bool[,] inside, int columns, int rows)
        {
            var edges = new List<GridEdge>();

            for (var i = 0; i < columns; i++)
            {
                for (var j = 0; j < rows; j++)
                {
                    if (!inside[i, j]) continue;

                    if (j == 0 || !inside[i, j - 1])
                        edges.Add(new GridEdge(i, j, i + 1, j));

                    if (i == columns - 1 || !inside[i + 1, j])
                        edges.Add(new GridEdge(i + 1, j, i + 1, j + 1));

                    if (j == rows - 1 || !inside[i, j + 1])
                        edges.Add(new GridEdge(i + 1, j + 1, i, j + 1));

                    if (i == 0 || !inside[i - 1, j])
                        edges.Add(new GridEdge(i, j + 1, i, j));
                }
            }

            return edges;
        }

        /// <summary>
        /// Picks which unused edge continues the contour arriving along <paramref name="arriving"/>.
        /// </summary>
        /// <remarks>
        /// A vertex where two diagonally opposite cells are covered carries two incoming and two
        /// outgoing edges, and the choice decides whether the two pieces meeting there are traced as
        /// two contours touching at a point or merged into one self-touching contour. Always taking the
        /// sharpest clockwise turn keeps them separate, which is what makes each traced contour bound a
        /// simple area.
        /// </remarks>
        private static int SelectNextEdge(
            List<GridEdge> edges, List<int> candidates, bool[] used, GridEdge arriving)
        {
            var inX = Math.Sign(arriving.ToX - arriving.FromX);
            var inY = Math.Sign(arriving.ToY - arriving.FromY);

            var best = -1;
            var bestRank = int.MaxValue;

            foreach (var candidate in candidates)
            {
                if (used[candidate]) continue;

                var edge = edges[candidate];
                var outX = Math.Sign(edge.ToX - edge.FromX);
                var outY = Math.Sign(edge.ToY - edge.FromY);

                // Positive cross product is a clockwise turn in y-down space.
                var cross = inX * outY - inY * outX;
                var rank = cross > 0 ? 0 : cross == 0 ? 1 : 2;

                if (rank >= bestRank) continue;

                bestRank = rank;
                best = candidate;
            }

            return best;
        }

        /// <summary>
        /// Drops points that merely continue a straight run, so each remaining point is a real corner.
        /// </summary>
        private static List<RPoint> MergeCollinear(List<RPoint> points)
        {
            var count = points.Count;
            if (count < 3) return points;

            var merged = new List<RPoint>(count);
            for (var i = 0; i < count; i++)
            {
                var previous = points[(i - 1 + count) % count];
                var current = points[i];
                var next = points[(i + 1) % count];

                var arrivedHorizontally = Math.Abs(current.X - previous.X) > Epsilon;
                var leavesHorizontally = Math.Abs(next.X - current.X) > Epsilon;

                if (arrivedHorizontally != leavesHorizontally) merged.Add(current);
            }

            return merged.Count >= 3 ? merged : points;
        }

        /// <summary>
        /// Whether <paramref name="points"/> runs clockwise - a positive shoelace sum in y-down space.
        /// </summary>
        private static bool IsClockwise(List<RPoint> points)
        {
            double sum = 0;
            for (var i = 0; i < points.Count; i++)
            {
                var current = points[i];
                var next = points[(i + 1) % points.Count];
                sum += current.X * next.Y - next.X * current.Y;
            }

            return sum > 0;
        }

        /// <summary>One boundary edge, in grid-line indices rather than coordinates.</summary>
        private readonly record struct GridEdge(int FromX, int FromY, int ToX, int ToY);
    }
}

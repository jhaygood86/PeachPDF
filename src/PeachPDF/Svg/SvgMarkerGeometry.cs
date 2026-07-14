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

using PeachPDF.Html.Adapters.Entities;
using System;
using System.Collections.Generic;

namespace PeachPDF.Svg
{
    /// <summary>
    /// One vertex a marker can be placed at, plus the tangent angle (degrees, atan2 convention) a
    /// <c>marker</c> with <c>orient="auto"</c> should rotate to. <see cref="IsStart"/>/<see cref="IsEnd"/>
    /// identify the very first/last vertex of the *whole* element (across all subpaths, for a
    /// multi-subpath <c>&lt;path&gt;</c>) - every other vertex, including subsequent subpaths' own
    /// start points, is a "mid" marker position per spec.
    /// </summary>
    internal readonly record struct MarkerVertex(double X, double Y, double AngleDegrees, bool IsStart, bool IsEnd);

    /// <summary>
    /// Computes marker attachment vertices/tangent-angles for the shape kinds SVG allows markers on
    /// (<c>&lt;path&gt;</c>, <c>&lt;line&gt;</c>, <c>&lt;polyline&gt;</c>, <c>&lt;polygon&gt;</c> - not
    /// basic shapes like <c>&lt;rect&gt;</c>/<c>&lt;circle&gt;</c>/<c>&lt;ellipse&gt;</c>, per spec).
    /// A vertex's angle is the bisector of its incoming and outgoing segment directions (matching
    /// spec-recommended behavior at interior/join points); at an open path's endpoints, only the one
    /// available direction is used.
    /// </summary>
    internal static class SvgMarkerGeometry
    {
        public static List<MarkerVertex> ComputeForPath(IReadOnlyList<PathSegment> segments)
        {
            var vertices = new List<(double X, double Y, double? InAngle, double? OutAngle)>();
            var subpathStartIndex = -1;
            double curX = 0, curY = 0;

            void SetOutgoing(int index, double angle)
            {
                if (index < 0 || index >= vertices.Count) return;
                var v = vertices[index];
                vertices[index] = (v.X, v.Y, v.InAngle, angle);
            }

            void SetIncoming(int index, double angle)
            {
                if (index < 0 || index >= vertices.Count) return;
                var v = vertices[index];
                vertices[index] = (v.X, v.Y, angle, v.OutAngle);
            }

            foreach (var seg in segments)
            {
                switch (seg.Kind)
                {
                    case PathSegmentKind.MoveTo:
                        vertices.Add((seg.X, seg.Y, null, null));
                        subpathStartIndex = vertices.Count - 1;
                        curX = seg.X; curY = seg.Y;
                        break;

                    case PathSegmentKind.LineTo:
                    {
                        var angle = AngleOf(seg.X - curX, seg.Y - curY);
                        SetOutgoing(vertices.Count - 1, angle);
                        vertices.Add((seg.X, seg.Y, angle, null));
                        curX = seg.X; curY = seg.Y;
                        break;
                    }

                    case PathSegmentKind.CubicBezierTo:
                    {
                        var (outDx, outDy) = FirstNonZero((seg.X1 - curX, seg.Y1 - curY), (seg.X2 - curX, seg.Y2 - curY), (seg.X - curX, seg.Y - curY));
                        SetOutgoing(vertices.Count - 1, AngleOf(outDx, outDy));

                        var (inDx, inDy) = FirstNonZero((seg.X - seg.X2, seg.Y - seg.Y2), (seg.X - seg.X1, seg.Y - seg.Y1), (seg.X - curX, seg.Y - curY));
                        vertices.Add((seg.X, seg.Y, AngleOf(inDx, inDy), null));
                        curX = seg.X; curY = seg.Y;
                        break;
                    }

                    case PathSegmentKind.ArcTo:
                    {
                        // Approximated using the chord direction rather than the true elliptical arc
                        // tangent - a documented v1 simplification; markers on elliptical arcs are a
                        // rare combination in practice, and the chord is a reasonable approximation
                        // except for very large sweep angles.
                        var angle = AngleOf(seg.X - curX, seg.Y - curY);
                        SetOutgoing(vertices.Count - 1, angle);
                        vertices.Add((seg.X, seg.Y, angle, null));
                        curX = seg.X; curY = seg.Y;
                        break;
                    }

                    case PathSegmentKind.ClosePath:
                    {
                        if (subpathStartIndex >= 0 && subpathStartIndex < vertices.Count)
                        {
                            var start = vertices[subpathStartIndex];
                            if (start.X != curX || start.Y != curY)
                            {
                                var angle = AngleOf(start.X - curX, start.Y - curY);
                                SetOutgoing(vertices.Count - 1, angle);
                                SetIncoming(subpathStartIndex, angle);
                            }
                            curX = start.X; curY = start.Y;
                        }
                        break;
                    }
                }
            }

            return ToMarkerVertices(vertices);
        }

        public static List<MarkerVertex> ComputeForLine(double x1, double y1, double x2, double y2)
        {
            var angle = AngleOf(x2 - x1, y2 - y1);
            return [new MarkerVertex(x1, y1, angle, true, false), new MarkerVertex(x2, y2, angle, false, true)];
        }

        /// <summary>Shared by <c>&lt;polyline&gt;</c> (<paramref name="closed"/>=false) and <c>&lt;polygon&gt;</c> (true, wrapping the last segment back to the first point).</summary>
        public static List<MarkerVertex> ComputeForPoints(RPoint[] points, bool closed)
        {
            if (points.Length == 0)
                return [];

            if (points.Length == 1)
                return [new MarkerVertex(points[0].X, points[0].Y, 0, true, true)];

            var n = points.Length;
            var segmentAngles = new double[n - 1];
            for (var i = 0; i < n - 1; i++)
                segmentAngles[i] = AngleOf(points[i + 1].X - points[i].X, points[i + 1].Y - points[i].Y);

            var closingAngle = closed ? AngleOf(points[0].X - points[n - 1].X, points[0].Y - points[n - 1].Y) : (double?)null;

            var result = new List<MarkerVertex>(n);
            for (var i = 0; i < n; i++)
            {
                double? inAngle = i > 0 ? segmentAngles[i - 1] : closingAngle;
                double? outAngle = i < n - 1 ? segmentAngles[i] : closingAngle;
                result.Add(new MarkerVertex(points[i].X, points[i].Y, BisectAngle(inAngle, outAngle), IsStart: i == 0, IsEnd: i == n - 1));
            }

            return result;
        }

        private static List<MarkerVertex> ToMarkerVertices(List<(double X, double Y, double? InAngle, double? OutAngle)> vertices)
        {
            var result = new List<MarkerVertex>(vertices.Count);
            for (var i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                result.Add(new MarkerVertex(v.X, v.Y, BisectAngle(v.InAngle, v.OutAngle), IsStart: i == 0, IsEnd: i == vertices.Count - 1));
            }
            return result;
        }

        private static (double, double) FirstNonZero((double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
        {
            if (a.X != 0 || a.Y != 0) return a;
            if (b.X != 0 || b.Y != 0) return b;
            return c;
        }

        private static double AngleOf(double dx, double dy) => Math.Atan2(dy, dx) * (180.0 / Math.PI);

        /// <summary>
        /// Averages two angles by their unit vectors (not the raw degree values), so bisecting e.g.
        /// 170° and -170° correctly gives 180° rather than 0°.
        /// </summary>
        private static double BisectAngle(double? inAngle, double? outAngle)
        {
            if (inAngle is not { } a) return outAngle ?? 0;
            if (outAngle is not { } b) return a;

            var radA = a * (Math.PI / 180.0);
            var radB = b * (Math.PI / 180.0);
            var x = Math.Cos(radA) + Math.Cos(radB);
            var y = Math.Sin(radA) + Math.Sin(radB);

            return x == 0 && y == 0 ? a : Math.Atan2(y, x) * (180.0 / Math.PI);
        }
    }
}

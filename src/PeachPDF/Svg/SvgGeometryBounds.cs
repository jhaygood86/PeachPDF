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
    /// Computes a shape's local-space bounding box - needed to resolve <c>objectBoundingBox</c>-unit
    /// gradients/patterns/masks (fractions of the referencing shape's own geometry) at paint time.
    /// </summary>
    internal static class SvgGeometryBounds
    {
        public static RRect? GetBoundingBox(SvgElement element) => element switch
        {
            SvgPathElement path => PathBounds(path.Segments),
            SvgCircleElement { R: > 0 } circle => new RRect(circle.Cx - circle.R, circle.Cy - circle.R, circle.R * 2, circle.R * 2),
            SvgEllipseElement { Rx: > 0, Ry: > 0 } ellipse => new RRect(ellipse.Cx - ellipse.Rx, ellipse.Cy - ellipse.Ry, ellipse.Rx * 2, ellipse.Ry * 2),
            SvgRectElement { Width: > 0, Height: > 0 } rect => new RRect(rect.X, rect.Y, rect.Width, rect.Height),
            SvgPolygonElement polygon => PointsBounds(polygon.Points),
            SvgPolylineElement polyline => PointsBounds(polyline.Points),
            SvgLineElement line => PointsBounds([new RPoint(line.X1, line.Y1), new RPoint(line.X2, line.Y2)]),
            SvgUseElement { Target: { } target } use => Offset(GetBoundingBox(target), use.X, use.Y),
            SvgGroupElement group => UnionAll(group.Children),
            _ => null,
        };

        private static RRect? Offset(RRect? rect, double dx, double dy) =>
            rect is { } r ? new RRect(r.X + dx, r.Y + dy, r.Width, r.Height) : null;

        private static RRect? UnionAll(IEnumerable<SvgElement> elements)
        {
            RRect? result = null;

            foreach (var element in elements)
            {
                var bounds = GetBoundingBox(element);
                if (bounds is not { } b)
                    continue;

                result = result is { } r ? Union(r, b) : b;
            }

            return result;
        }

        private static RRect Union(RRect a, RRect b)
        {
            var minX = Math.Min(a.X, b.X);
            var minY = Math.Min(a.Y, b.Y);
            var maxX = Math.Max(a.X + a.Width, b.X + b.Width);
            var maxY = Math.Max(a.Y + a.Height, b.Y + b.Height);
            return new RRect(minX, minY, maxX - minX, maxY - minY);
        }

        private static RRect? PointsBounds(RPoint[] points)
        {
            if (points.Length == 0)
                return null;

            double minX = points[0].X, maxX = minX;
            double minY = points[0].Y, maxY = minY;

            for (var i = 1; i < points.Length; i++)
            {
                minX = Math.Min(minX, points[i].X);
                maxX = Math.Max(maxX, points[i].X);
                minY = Math.Min(minY, points[i].Y);
                maxY = Math.Max(maxY, points[i].Y);
            }

            return new RRect(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// Includes bezier control points and arc endpoints as a conservative envelope rather than
        /// computing exact curve extrema - a slight over-estimate for curved segments, adequate for
        /// objectBoundingBox gradient/pattern/mask positioning (minor over-estimation just shifts
        /// stops/tiles slightly, with no visible artifact).
        /// </summary>
        private static RRect? PathBounds(IReadOnlyList<PathSegment> segments)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            var any = false;

            void Include(double x, double y)
            {
                any = true;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            foreach (var segment in segments)
            {
                switch (segment.Kind)
                {
                    case PathSegmentKind.MoveTo:
                    case PathSegmentKind.LineTo:
                    case PathSegmentKind.ArcTo:
                        Include(segment.X, segment.Y);
                        break;
                    case PathSegmentKind.CubicBezierTo:
                        Include(segment.X1, segment.Y1);
                        Include(segment.X2, segment.Y2);
                        Include(segment.X, segment.Y);
                        break;
                }
            }

            return any ? new RRect(minX, minY, maxX - minX, maxY - minY) : null;
        }
    }
}

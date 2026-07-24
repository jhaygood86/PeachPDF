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
    /// Arc-length parameterization of a parsed path, used to lay <c>&lt;textPath&gt;</c> glyphs along a
    /// curve. The path is flattened once to a polyline (each <see cref="PathSegmentKind.CubicBezierTo"/>
    /// subdivided by de Casteljau, each <see cref="PathSegmentKind.ArcTo"/> sampled from the SVG
    /// endpoint-to-center arc parameterization), and <see cref="PointAtLength"/> then interpolates a
    /// point and its tangent direction at a given distance along the total path. A <c>MoveTo</c> starts
    /// a fresh subpath without contributing the pen-up jump to the length (SVG textPath concatenates
    /// subpath lengths).
    /// </summary>
    internal sealed class SvgTextPathGeometry
    {
        // Subdivision steps per cubic segment; arcs use a sweep-proportional count. Fixed subdivision is
        // a documented approximation - fine for text placement, where sub-pixel arc-length error is
        // invisible.
        private const int CubicSteps = 24;

        private readonly struct Segment(RPoint a, RPoint b, double startLength, double endLength)
        {
            public RPoint A { get; } = a;
            public RPoint B { get; } = b;
            public double StartLength { get; } = startLength;
            public double EndLength { get; } = endLength;
        }

        private readonly List<Segment> _segments = [];
        private double _total;
        private RPoint _current;
        private RPoint _subpathStart;
        private bool _hasCurrent;

        private double _minX = double.MaxValue, _minY = double.MaxValue, _maxX = double.MinValue, _maxY = double.MinValue;

        public double TotalLength => _total;

        public bool IsEmpty => _segments.Count == 0;

        /// <summary>The axis-aligned bounding box of the flattened path (default when <see cref="IsEmpty"/>).</summary>
        public RRect Bounds => IsEmpty ? default : new RRect(_minX, _minY, _maxX - _minX, _maxY - _minY);

        public SvgTextPathGeometry(IReadOnlyList<PathSegment> segments)
        {
            foreach (var segment in segments)
            {
                switch (segment.Kind)
                {
                    case PathSegmentKind.MoveTo:
                        _current = new RPoint(segment.X, segment.Y);
                        _subpathStart = _current;
                        _hasCurrent = true;
                        break;

                    case PathSegmentKind.LineTo:
                        Emit(new RPoint(segment.X, segment.Y));
                        break;

                    case PathSegmentKind.CubicBezierTo:
                        FlattenCubic(
                            new RPoint(segment.X1, segment.Y1),
                            new RPoint(segment.X2, segment.Y2),
                            new RPoint(segment.X, segment.Y));
                        break;

                    case PathSegmentKind.ArcTo:
                        FlattenArc(segment);
                        break;

                    case PathSegmentKind.ClosePath:
                        if (_hasCurrent)
                            Emit(_subpathStart);
                        break;
                }
            }
        }

        /// <summary>
        /// The point and tangent direction (degrees) at distance <paramref name="s"/> along the path.
        /// <paramref name="s"/> is clamped to <c>[0, TotalLength]</c>.
        /// </summary>
        public (double X, double Y, double TangentDegrees) PointAtLength(double s)
        {
            if (_segments.Count == 0)
                return (_current.X, _current.Y, 0);

            s = Math.Clamp(s, 0, _total);

            // Linear scan is fine: a flattened glyph run walks monotonically increasing s, and paths are
            // small. (A binary search would be a micro-optimization with no practical effect here.)
            foreach (var segment in _segments)
            {
                if (s <= segment.EndLength || segment.EndLength >= _total)
                {
                    var span = segment.EndLength - segment.StartLength;
                    var t = span > 0 ? (s - segment.StartLength) / span : 0;
                    var x = segment.A.X + (segment.B.X - segment.A.X) * t;
                    var y = segment.A.Y + (segment.B.Y - segment.A.Y) * t;
                    var angle = Math.Atan2(segment.B.Y - segment.A.Y, segment.B.X - segment.A.X) * (180.0 / Math.PI);
                    return (x, y, angle);
                }
            }

            var last = _segments[^1];
            var lastAngle = Math.Atan2(last.B.Y - last.A.Y, last.B.X - last.A.X) * (180.0 / Math.PI);
            return (last.B.X, last.B.Y, lastAngle);
        }

        private void Emit(RPoint to)
        {
            if (!_hasCurrent)
            {
                _current = to;
                _subpathStart = to;
                _hasCurrent = true;
                return;
            }

            var d = Distance(_current, to);
            if (d > 1e-12)
            {
                _segments.Add(new Segment(_current, to, _total, _total + d));
                _total += d;
                Track(_current);
                Track(to);
            }

            _current = to;
        }

        private void FlattenCubic(RPoint c1, RPoint c2, RPoint end)
        {
            var p0 = _current;
            for (var i = 1; i <= CubicSteps; i++)
            {
                var t = (double)i / CubicSteps;
                Emit(CubicPoint(p0, c1, c2, end, t));
            }
        }

        private static RPoint CubicPoint(RPoint p0, RPoint c1, RPoint c2, RPoint p3, double t)
        {
            var u = 1 - t;
            var a = u * u * u;
            var b = 3 * u * u * t;
            var c = 3 * u * t * t;
            var d = t * t * t;
            return new RPoint(
                a * p0.X + b * c1.X + c * c2.X + d * p3.X,
                a * p0.Y + b * c1.Y + c * c2.Y + d * p3.Y);
        }

        /// <summary>
        /// Flattens an SVG elliptical arc via the endpoint-to-center parameterization
        /// (SVG 1.1 Implementation Notes F.6.5/F.6.6), sampling points along the resulting arc. A
        /// zero-radius arc degenerates to a straight line, per spec.
        /// </summary>
        private void FlattenArc(PathSegment segment)
        {
            var end = new RPoint(segment.X, segment.Y);
            var rx = Math.Abs(segment.RadiusX);
            var ry = Math.Abs(segment.RadiusY);

            if (rx < 1e-12 || ry < 1e-12)
            {
                Emit(end);
                return;
            }

            var phi = segment.RotationAngle * (Math.PI / 180.0);
            var cosPhi = Math.Cos(phi);
            var sinPhi = Math.Sin(phi);

            var start = _current;
            var dx = (start.X - end.X) / 2.0;
            var dy = (start.Y - end.Y) / 2.0;

            // Step 1: transform to the ellipse's own axis-aligned frame.
            var x1p = cosPhi * dx + sinPhi * dy;
            var y1p = -sinPhi * dx + cosPhi * dy;

            // Step 2: correct out-of-range radii (F.6.6).
            var lambda = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);
            if (lambda > 1)
            {
                var scale = Math.Sqrt(lambda);
                rx *= scale;
                ry *= scale;
            }

            // Step 3: the center in the transformed frame.
            var rx2 = rx * rx;
            var ry2 = ry * ry;
            var num = rx2 * ry2 - rx2 * y1p * y1p - ry2 * x1p * x1p;
            var den = rx2 * y1p * y1p + ry2 * x1p * x1p;
            var coef = Math.Sqrt(Math.Max(0, num / den));
            if (segment.IsLargeArc == segment.SweepClockwise)
                coef = -coef;

            var cxp = coef * rx * y1p / ry;
            var cyp = -coef * ry * x1p / rx;

            // Step 4: the center in the original frame, and the start/sweep angles.
            var cx = cosPhi * cxp - sinPhi * cyp + (start.X + end.X) / 2.0;
            var cy = sinPhi * cxp + cosPhi * cyp + (start.Y + end.Y) / 2.0;

            var theta1 = AngleBetween(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
            var deltaTheta = AngleBetween((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);

            if (!segment.SweepClockwise && deltaTheta > 0)
                deltaTheta -= 2 * Math.PI;
            else if (segment.SweepClockwise && deltaTheta < 0)
                deltaTheta += 2 * Math.PI;

            // Step 5: sample. Use enough steps to keep the polyline smooth for the arc's sweep.
            var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(deltaTheta) / (Math.PI / 16)));
            for (var i = 1; i <= steps; i++)
            {
                var theta = theta1 + deltaTheta * i / steps;
                var ex = rx * Math.Cos(theta);
                var ey = ry * Math.Sin(theta);
                Emit(new RPoint(
                    cosPhi * ex - sinPhi * ey + cx,
                    sinPhi * ex + cosPhi * ey + cy));
            }
        }

        private static double AngleBetween(double ux, double uy, double vx, double vy)
        {
            var dot = ux * vx + uy * vy;
            var len = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
            var angle = Math.Acos(Math.Clamp(len > 0 ? dot / len : 0, -1, 1));
            if (ux * vy - uy * vx < 0)
                angle = -angle;
            return angle;
        }

        private void Track(RPoint p)
        {
            _minX = Math.Min(_minX, p.X);
            _minY = Math.Min(_minY, p.Y);
            _maxX = Math.Max(_maxX, p.X);
            _maxY = Math.Max(_maxY, p.Y);
        }

        private static double Distance(RPoint a, RPoint b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}

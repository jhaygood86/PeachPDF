using System;
using System.Collections.Generic;

namespace PeachDrawing.Core.Geometry
{
    /// <summary>A point on a path and the direction the path is heading there.</summary>
    /// <param name="X">the point's x-coordinate</param>
    /// <param name="Y">the point's y-coordinate</param>
    /// <param name="TangentDegrees">the direction of travel at the point, in degrees from the positive x-axis toward positive y</param>
    public readonly record struct PathSample(double X, double Y, double TangentDegrees);

    /// <summary>
    /// Measures a <see cref="GraphicsPath"/>: how long it is, how much it encloses, what box it fits in, and where a given
    /// distance along it lands. The path is read once, when the measure is created, so later edits to the path are not seen.
    /// Distances are measured along the flattened path, within the <c>tolerance</c> given to the constructor of the true curve;
    /// a subpath's jump from where the previous one ended is not part of the length.
    /// </summary>
    public sealed class PathMeasure
    {
        private readonly record struct Piece(PaintPoint From, PaintPoint To, double StartLength, double EndLength);

        private readonly List<Piece> _pieces = [];
        private readonly PaintPoint _firstPoint;

        /// <summary>Measures <paramref name="path"/>.</summary>
        /// <param name="path">the path to measure</param>
        /// <param name="tolerance">the largest distance the flattened path may stray from the true curve; a value that is not
        /// positive uses 0.05</param>
        public PathMeasure(GraphicsPath path, double tolerance = 0.05)
        {
            ArgumentNullException.ThrowIfNull(path);
            if (tolerance <= 0 || double.IsNaN(tolerance))
                tolerance = 0.05;

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            void Extend(double x, double y)
            {
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            double signedArea = 0;
            var hasPoint = false;
            foreach (var contour in path.GetCurveContours())
            {
                var current = contour.Start;
                if (!hasPoint)
                {
                    _firstPoint = contour.Start;
                    hasPoint = true;
                }

                Extend(current.X, current.Y);
                var contourArea = 0.0;
                var flat = new List<PaintPoint> { current };
                foreach (var command in contour.Commands)
                {
                    if (command.Kind == PathCommandKind.Cubic)
                    {
                        foreach (var t in Extrema(current, command))
                        {
                            var p = CubicPoint(current, command, t);
                            Extend(p.X, p.Y);
                        }

                        FlattenCubic(flat, current, command, tolerance);
                        contourArea += CubicCross(current, command);
                    }
                    else
                    {
                        flat.Add(command.End);
                        contourArea += current.X * command.End.Y - command.End.X * current.Y;
                    }

                    Extend(command.End.X, command.End.Y);
                    current = command.End;
                }

                if (contour.Closed && flat.Count > 1)
                    flat.Add(contour.Start);

                for (var i = 1; i < flat.Count; i++)
                    AddPiece(flat[i - 1], flat[i]);

                // Area treats every contour as closed, whatever its flag says.
                signedArea += contourArea + current.X * contour.Start.Y - contour.Start.X * current.Y;
            }

            Length = _pieces.Count == 0 ? 0 : _pieces[^1].EndLength;
            IsEmpty = _pieces.Count == 0;
            Area = Math.Abs(signedArea) / 2;
            Bounds = hasPoint ? new Rect(minX, minY, maxX - minX, maxY - minY) : default;
        }

        /// <summary>The total length of the path.</summary>
        public double Length { get; }

        /// <summary>Whether the path has no length: it is empty, or only moves.</summary>
        public bool IsEmpty { get; }

        /// <summary>
        /// The area the path encloses, counting every subpath as closed. Subpaths wound the opposite way from the rest subtract
        /// from it, as a hole does; an open, straight path encloses nothing.
        /// </summary>
        public double Area { get; }

        /// <summary>The tightest axis-aligned box around the path, including the extremes of its curves (default for an empty path).</summary>
        public Rect Bounds { get; }

        /// <summary>
        /// The point at <paramref name="distance"/> along the path and the direction of travel there. The distance is clamped to
        /// <c>[0, Length]</c>; for a path with no length the answer is its first point, heading 0.
        /// </summary>
        /// <param name="distance">how far along the path to go</param>
        public PathSample PointAtLength(double distance)
        {
            if (_pieces.Count == 0)
                return new PathSample(_firstPoint.X, _firstPoint.Y, 0);

            distance = Math.Clamp(distance, 0, Length);

            // Binary search for the piece whose range holds the distance.
            int lo = 0, hi = _pieces.Count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (distance <= _pieces[mid].EndLength)
                    hi = mid;
                else
                    lo = mid + 1;
            }

            var piece = _pieces[lo];
            var span = piece.EndLength - piece.StartLength;
            var t = span > 0 ? (distance - piece.StartLength) / span : 0;
            return new PathSample(
                piece.From.X + (piece.To.X - piece.From.X) * t,
                piece.From.Y + (piece.To.Y - piece.From.Y) * t,
                Math.Atan2(piece.To.Y - piece.From.Y, piece.To.X - piece.From.X) * (180.0 / Math.PI));
        }

        private void AddPiece(PaintPoint from, PaintPoint to)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= 1e-12)
                return;

            var start = _pieces.Count == 0 ? 0 : _pieces[^1].EndLength;
            _pieces.Add(new Piece(from, to, start, start + length));
        }

        private static void FlattenCubic(List<PaintPoint> points, PaintPoint p0, PathCommand c, double tolerance)
        {
            var ddx = Math.Max(Math.Abs(p0.X - 2 * c.Control1.X + c.Control2.X), Math.Abs(c.Control1.X - 2 * c.Control2.X + c.End.X));
            var ddy = Math.Max(Math.Abs(p0.Y - 2 * c.Control1.Y + c.Control2.Y), Math.Abs(c.Control1.Y - 2 * c.Control2.Y + c.End.Y));
            var dd = Math.Sqrt(ddx * ddx + ddy * ddy);
            var n = Math.Clamp((int)Math.Ceiling(Math.Sqrt(0.75 * dd / tolerance)), 1, 500);
            for (var s = 1; s <= n; s++)
                points.Add(CubicPoint(p0, c, (double)s / n));
        }

        /// <summary>
        /// The exact value of the integral of <c>x dy - y dx</c> over a cubic (twice the signed area it sweeps from the origin),
        /// by three-point Gauss-Legendre quadrature, which is exact for the degree-5 polynomial being integrated.
        /// </summary>
        private static double CubicCross(PaintPoint p0, PathCommand c)
        {
            ReadOnlySpan<double> nodes = [0.5 - 0.5 * 0.7745966692414834, 0.5, 0.5 + 0.5 * 0.7745966692414834];
            ReadOnlySpan<double> weights = [5.0 / 18.0, 8.0 / 18.0, 5.0 / 18.0];

            double total = 0;
            for (var i = 0; i < 3; i++)
            {
                var t = nodes[i];
                var u = 1 - t;
                var p = CubicPoint(p0, c, t);
                var dx = 3 * (u * u * (c.Control1.X - p0.X) + 2 * u * t * (c.Control2.X - c.Control1.X) + t * t * (c.End.X - c.Control2.X));
                var dy = 3 * (u * u * (c.Control1.Y - p0.Y) + 2 * u * t * (c.Control2.Y - c.Control1.Y) + t * t * (c.End.Y - c.Control2.Y));
                total += weights[i] * (p.X * dy - p.Y * dx);
            }

            return total;
        }

        private static PaintPoint CubicPoint(PaintPoint p0, PathCommand c, double t)
        {
            var u = 1 - t;
            double a = u * u * u, b = 3 * u * u * t, d = 3 * u * t * t, e = t * t * t;
            return new PaintPoint(
                a * p0.X + b * c.Control1.X + d * c.Control2.X + e * c.End.X,
                a * p0.Y + b * c.Control1.Y + d * c.Control2.Y + e * c.End.Y);
        }

        /// <summary>The parameters where a cubic's x or y stops increasing or decreasing: the only places it can leave the box its end points make.</summary>
        private static IEnumerable<double> Extrema(PaintPoint p0, PathCommand c)
        {
            foreach (var t in Roots(p0.X, c.Control1.X, c.Control2.X, c.End.X))
                yield return t;

            foreach (var t in Roots(p0.Y, c.Control1.Y, c.Control2.Y, c.End.Y))
                yield return t;
        }

        private static IEnumerable<double> Roots(double p0, double p1, double p2, double p3)
        {
            // The derivative of a cubic Bézier is the quadratic a*t^2 + b*t + c.
            var a = -p0 + 3 * p1 - 3 * p2 + p3;
            var b = 2 * (p0 - 2 * p1 + p2);
            var c = p1 - p0;

            if (Math.Abs(a) < 1e-12)
            {
                if (Math.Abs(b) > 1e-12)
                {
                    var t = -c / b;
                    if (t is > 0 and < 1)
                        yield return t;
                }

                yield break;
            }

            var disc = b * b - 4 * a * c;
            if (disc < 0)
                yield break;

            var sqrt = Math.Sqrt(disc);
            var t1 = (-b + sqrt) / (2 * a);
            var t2 = (-b - sqrt) / (2 * a);
            if (t1 is > 0 and < 1)
                yield return t1;
            if (t2 is > 0 and < 1 && t2 != t1)
                yield return t2;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachDrawing.Core.Geometry
{
    /// <summary>
    /// The engine behind <see cref="PathOperations.Combine"/>. Every curve of both shapes is cut wherever it meets another curve, so
    /// each piece lies wholly on the boundary of one shape or inside or outside of the other. A piece is then kept exactly when the
    /// combined shape covers one side of it and not the other, turned so the covered side is on its left, and the kept pieces are
    /// joined end to start into closed outlines.
    /// </summary>
    internal static class PathCombiner
    {
        private const int MaxDepth = 26;
        private const int MaxIntersectionsPerPair = 64;

        /// <summary>A line or cubic Bézier. A line's control points sit on it, and it stays a line however it is cut.</summary>
        private readonly record struct Bez(double X0, double Y0, double X1, double Y1, double X2, double Y2, double X3, double Y3, bool IsLine)
        {
            public static Bez Line(double x0, double y0, double x3, double y3) =>
                new(x0, y0, x0 + (x3 - x0) / 3, y0 + (y3 - y0) / 3, x0 + (x3 - x0) * 2 / 3, y0 + (y3 - y0) * 2 / 3, x3, y3, true);

            public Bez Reversed() => new(X3, Y3, X2, Y2, X1, Y1, X0, Y0, IsLine);

            public (double X, double Y) At(double t)
            {
                var u = 1 - t;
                double a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
                return (a * X0 + b * X1 + c * X2 + d * X3, a * Y0 + b * Y1 + c * Y2 + d * Y3);
            }

            public (double X, double Y) Derivative(double t)
            {
                var u = 1 - t;
                double a = 3 * u * u, b = 6 * u * t, c = 3 * t * t;
                return (a * (X1 - X0) + b * (X2 - X1) + c * (X3 - X2), a * (Y1 - Y0) + b * (Y2 - Y1) + c * (Y3 - Y2));
            }

            public (Bez Left, Bez Right) Split(double t)
            {
                double x01 = X0 + (X1 - X0) * t, y01 = Y0 + (Y1 - Y0) * t;
                double x12 = X1 + (X2 - X1) * t, y12 = Y1 + (Y2 - Y1) * t;
                double x23 = X2 + (X3 - X2) * t, y23 = Y2 + (Y3 - Y2) * t;
                double xa = x01 + (x12 - x01) * t, ya = y01 + (y12 - y01) * t;
                double xb = x12 + (x23 - x12) * t, yb = y12 + (y23 - y12) * t;
                double xm = xa + (xb - xa) * t, ym = ya + (yb - ya) * t;
                return (new Bez(X0, Y0, x01, y01, xa, ya, xm, ym, IsLine), new Bez(xm, ym, xb, yb, x23, y23, X3, Y3, IsLine));
            }

            public Bez Between(double t0, double t1)
            {
                var right = t0 <= 0 ? this : Split(t0).Right;
                return t1 >= 1 ? right : right.Split((t1 - t0) / (1 - t0)).Left;
            }

            public double MinX => Math.Min(Math.Min(X0, X1), Math.Min(X2, X3));
            public double MaxX => Math.Max(Math.Max(X0, X1), Math.Max(X2, X3));
            public double MinY => Math.Min(Math.Min(Y0, Y1), Math.Min(Y2, Y3));
            public double MaxY => Math.Max(Math.Max(Y0, Y1), Math.Max(Y2, Y3));

            /// <summary>Whether both control points lie within <paramref name="tolerance"/> of the chord, so it is a line for our purposes.</summary>
            public bool IsFlat(double tolerance)
            {
                if (IsLine)
                    return true;

                double dx = X3 - X0, dy = Y3 - Y0;
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length < 1e-300)
                    return Math.Max(Math.Abs(X1 - X0) + Math.Abs(Y1 - Y0), Math.Abs(X2 - X0) + Math.Abs(Y2 - Y0)) <= tolerance;

                var d1 = Math.Abs((X1 - X0) * dy - (Y1 - Y0) * dx) / length;
                var d2 = Math.Abs((X2 - X0) * dy - (Y2 - Y0) * dx) / length;
                return Math.Max(d1, d2) <= tolerance;
            }

            public double ControlLength =>
                Math.Sqrt((X1 - X0) * (X1 - X0) + (Y1 - Y0) * (Y1 - Y0))
                + Math.Sqrt((X2 - X1) * (X2 - X1) + (Y2 - Y1) * (Y2 - Y1))
                + Math.Sqrt((X3 - X2) * (X3 - X2) + (Y3 - Y2) * (Y3 - Y2));
        }

        /// <summary>One filled shape: its curves, with each subpath closed, and the rule that decides what is inside.</summary>
        private sealed class Shape(List<Bez> curves, List<int> contourOf, FillMode fillMode)
        {
            public List<Bez> Curves { get; } = curves;
            public List<int> ContourOf { get; } = contourOf;
            public FillMode FillMode { get; } = fillMode;
        }

        public static void Combine(GraphicsPath first, GraphicsPath second, PathOperation operation, GraphicsPath destination)
        {
            var a = ReadShape(first);
            var b = ReadShape(second);

            // Everything is judged relative to the size of the inputs, so the precision is the same for a shape a point wide and one
            // ten thousand points wide.
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var c in a.Curves.Concat(b.Curves))
            {
                minX = Math.Min(minX, c.MinX);
                maxX = Math.Max(maxX, c.MaxX);
                minY = Math.Min(minY, c.MinY);
                maxY = Math.Max(maxY, c.MaxY);
            }

            if (a.Curves.Count + b.Curves.Count == 0)
                return;

            var scale = Math.Max(Math.Max(maxX - minX, maxY - minY), 1e-9);
            var flatTolerance = scale * 1e-7;
            var pointTolerance = scale * 1e-6;
            var sideOffset = scale * 1e-6;

            // Every curve of both shapes, with the parameters at which it is cut.
            var all = new List<(Bez Curve, int Shape, int Contour)>();
            for (var i = 0; i < a.Curves.Count; i++)
                all.Add((a.Curves[i], 0, a.ContourOf[i]));
            for (var i = 0; i < b.Curves.Count; i++)
                all.Add((b.Curves[i], 1, b.ContourOf[i] + 1_000_000));

            var cuts = new List<double>[all.Count];
            for (var i = 0; i < all.Count; i++)
                cuts[i] = [];

            // The first and last curve of the subpath each curve belongs to, so neighbours are found without scanning.
            var firstOf = new int[all.Count];
            var lastOf = new int[all.Count];
            for (var i = 0; i < all.Count; i++)
                firstOf[i] = i > 0 && all[i - 1].Shape == all[i].Shape && all[i - 1].Contour == all[i].Contour ? firstOf[i - 1] : i;
            for (var i = all.Count - 1; i >= 0; i--)
                lastOf[i] = i < all.Count - 1 && all[i + 1].Shape == all[i].Shape && all[i + 1].Contour == all[i].Contour ? lastOf[i + 1] : i;

            for (var i = 0; i < all.Count; i++)
            {
                for (var j = i + 1; j < all.Count; j++)
                {
                    if (AreNeighbours(all, firstOf, lastOf, i, j))
                        continue;

                    var ci = all[i].Curve;
                    var cj = all[j].Curve;
                    if (ci.MaxX + flatTolerance < cj.MinX || cj.MaxX + flatTolerance < ci.MinX
                        || ci.MaxY + flatTolerance < cj.MinY || cj.MaxY + flatTolerance < ci.MinY)
                        continue;

                    if (IsSameCurve(ci, cj, pointTolerance))
                        continue;

                    var found = new List<(double Ti, double Tj)>();
                    Intersect(ci, 0, 1, cj, 0, 1, 0, flatTolerance, found);
                    foreach (var (ti, tj) in found)
                    {
                        cuts[i].Add(ti);
                        cuts[j].Add(tj);
                    }
                }
            }

            // Cut every curve into pieces, dropping tiny ones and pieces that lie exactly on another piece.
            var pieces = new List<Bez>();
            for (var i = 0; i < all.Count; i++)
            {
                var ts = CleanCuts(cuts[i]);
                var previous = 0.0;
                foreach (var t in ts.Append(1.0))
                {
                    var piece = all[i].Curve.Between(previous, t);
                    previous = t;
                    if (piece.ControlLength < pointTolerance)
                        continue;

                    if (!pieces.Exists(p => IsSameCurve(p, piece, pointTolerance)))
                        pieces.Add(piece);
                }
            }

            // Keep the pieces that bound the combined area, turned so the area is on the left.
            var kept = new List<Bez>();
            foreach (var piece in pieces)
            {
                var (mx, my) = piece.At(0.5);
                var (tx, ty) = piece.Derivative(0.5);
                var length = Math.Sqrt(tx * tx + ty * ty);
                if (length < 1e-300)
                {
                    tx = piece.X3 - piece.X0;
                    ty = piece.Y3 - piece.Y0;
                    length = Math.Sqrt(tx * tx + ty * ty);
                    if (length < 1e-300)
                        continue;
                }

                var nx = -ty / length;
                var ny = tx / length;
                var onLeft = Covered(a, b, operation, mx + nx * sideOffset, my + ny * sideOffset);
                var onRight = Covered(a, b, operation, mx - nx * sideOffset, my - ny * sideOffset);
                if (onLeft == onRight)
                    continue;

                kept.Add(onLeft ? piece : piece.Reversed());
            }

            Link(kept, pointTolerance * 10, destination);
        }

        private static bool Covered(Shape a, Shape b, PathOperation operation, double x, double y)
        {
            var inA = WindingAt(a, x, y) != 0;
            var inB = WindingAt(b, x, y) != 0;
            return operation switch
            {
                PathOperation.Union => inA || inB,
                PathOperation.Intersect => inA && inB,
                PathOperation.Difference => inA && !inB,
                _ => inA != inB,
            };
        }

        private static Shape ReadShape(GraphicsPath path)
        {
            var curves = new List<Bez>();
            var contourOf = new List<int>();
            var contourIndex = 0;
            foreach (var contour in path.GetCurveContours())
            {
                if (contour.Commands.Count == 0)
                    continue;

                var (cx, cy) = (contour.Start.X, contour.Start.Y);
                foreach (var command in contour.Commands)
                {
                    if (command.Kind == PathCommandKind.Line)
                    {
                        if (command.End.X != cx || command.End.Y != cy)
                        {
                            curves.Add(Bez.Line(cx, cy, command.End.X, command.End.Y));
                            contourOf.Add(contourIndex);
                        }
                    }
                    else
                    {
                        curves.Add(new Bez(cx, cy, command.Control1.X, command.Control1.Y, command.Control2.X, command.Control2.Y,
                            command.End.X, command.End.Y, false));
                        contourOf.Add(contourIndex);
                    }

                    (cx, cy) = (command.End.X, command.End.Y);
                }

                // A filled area is always closed.
                if (cx != contour.Start.X || cy != contour.Start.Y)
                {
                    curves.Add(Bez.Line(cx, cy, contour.Start.X, contour.Start.Y));
                    contourOf.Add(contourIndex);
                }

                contourIndex++;
            }

            foreach (var c in curves)
            {
                if (!double.IsFinite(c.X0 + c.Y0 + c.X1 + c.Y1 + c.X2 + c.Y2 + c.X3 + c.Y3))
                    throw new ArgumentException("A path with a coordinate that is NaN or infinite cannot be combined.", nameof(path));
            }

            return new Shape(curves, contourOf, path.FillMode);
        }

        /// <summary>Curves that are next to each other along one subpath share an end point by design and are not cut against each other.</summary>
        private static bool AreNeighbours(List<(Bez Curve, int Shape, int Contour)> all, int[] firstOf, int[] lastOf, int i, int j)
        {
            if (all[i].Shape != all[j].Shape || all[i].Contour != all[j].Contour)
                return false;

            if (j - i == 1)
                return true;

            // The first and last curve of a subpath are neighbours too (the subpath is closed).
            return i == firstOf[i] && j == lastOf[j];
        }

        private static bool IsSameCurve(Bez p, Bez q, double tolerance)
        {
            static bool Close(double a, double b, double tol) => Math.Abs(a - b) <= tol;

            bool Forward() =>
                Close(p.X0, q.X0, tolerance) && Close(p.Y0, q.Y0, tolerance) && Close(p.X3, q.X3, tolerance) && Close(p.Y3, q.Y3, tolerance);

            bool Backward() =>
                Close(p.X0, q.X3, tolerance) && Close(p.Y0, q.Y3, tolerance) && Close(p.X3, q.X0, tolerance) && Close(p.Y3, q.Y0, tolerance);

            if (!Forward() && !Backward())
                return false;

            // The same end points can be joined by different curves, so compare the middle as well.
            var (px, py) = p.At(0.5);
            var (qx, qy) = q.At(0.5);
            return Close(px, qx, tolerance) && Close(py, qy, tolerance);
        }

        private static List<double> CleanCuts(List<double> cuts)
        {
            cuts.Sort();
            var result = new List<double>();
            foreach (var t in cuts)
            {
                if (t <= 1e-9 || t >= 1 - 1e-9)
                    continue;

                if (result.Count > 0 && t - result[^1] < 1e-7)
                    continue;

                result.Add(t);
            }

            return result;
        }

        #region Intersections

        /// <summary>Finds where two curves cross by cutting both in half until each is a line; overlapping lines report the ends of the overlap.</summary>
        private static void Intersect(Bez a, double a0, double a1, Bez b, double b0, double b1, int depth, double flatTolerance,
            List<(double, double)> found)
        {
            // Two curves that run along each other for a stretch make every level of the search report hits; past this many the pair is
            // treated as overlapping and no more are looked for.
            if (found.Count >= MaxIntersectionsPerPair)
                return;

            if (a.MaxX + flatTolerance < b.MinX || b.MaxX + flatTolerance < a.MinX
                || a.MaxY + flatTolerance < b.MinY || b.MaxY + flatTolerance < a.MinY)
                return;

            var aFlat = a.IsFlat(flatTolerance);
            var bFlat = b.IsFlat(flatTolerance);
            if (aFlat && bFlat)
            {
                IntersectLines(a, a0, a1, b, b0, b1, flatTolerance, found);
                return;
            }

            if (depth >= MaxDepth)
            {
                found.Add(((a0 + a1) / 2, (b0 + b1) / 2));
                return;
            }

            var aSize = Math.Max(a.MaxX - a.MinX, a.MaxY - a.MinY);
            var bSize = Math.Max(b.MaxX - b.MinX, b.MaxY - b.MinY);
            if (!aFlat && (bFlat || aSize >= bSize))
            {
                var (left, right) = a.Split(0.5);
                var mid = (a0 + a1) / 2;
                Intersect(left, a0, mid, b, b0, b1, depth + 1, flatTolerance, found);
                Intersect(right, mid, a1, b, b0, b1, depth + 1, flatTolerance, found);
            }
            else
            {
                var (left, right) = b.Split(0.5);
                var mid = (b0 + b1) / 2;
                Intersect(a, a0, a1, left, b0, mid, depth + 1, flatTolerance, found);
                Intersect(a, a0, a1, right, mid, b1, depth + 1, flatTolerance, found);
            }
        }

        private static void IntersectLines(Bez a, double a0, double a1, Bez b, double b0, double b1, double tolerance, List<(double, double)> found)
        {
            double d1x = a.X3 - a.X0, d1y = a.Y3 - a.Y0;
            double d2x = b.X3 - b.X0, d2y = b.Y3 - b.Y0;
            var len1 = Math.Sqrt(d1x * d1x + d1y * d1y);
            var len2 = Math.Sqrt(d2x * d2x + d2y * d2y);
            if (len1 < 1e-300 || len2 < 1e-300)
                return;

            var denom = d1x * d2y - d1y * d2x;
            double ex = b.X0 - a.X0, ey = b.Y0 - a.Y0;

            if (Math.Abs(denom) > 1e-9 * len1 * len2)
            {
                var s = (ex * d2y - ey * d2x) / denom;
                var u = (ex * d1y - ey * d1x) / denom;
                var slack = 1e-9 + tolerance / Math.Min(len1, len2);
                if (s < -slack || s > 1 + slack || u < -slack || u > 1 + slack)
                    return;

                s = Math.Clamp(s, 0, 1);
                u = Math.Clamp(u, 0, 1);
                found.Add((a0 + s * (a1 - a0), b0 + u * (b1 - b0)));
                return;
            }

            // Parallel: they only meet if they lie on the same line, and then along the stretch where they overlap.
            var distance = Math.Abs(ex * d1y - ey * d1x) / len1;
            if (distance > tolerance * 4)
                return;

            double sb0 = (ex * d1x + ey * d1y) / (len1 * len1);
            double sb3 = ((b.X3 - a.X0) * d1x + (b.Y3 - a.Y0) * d1y) / (len1 * len1);
            var lo = Math.Max(0, Math.Min(sb0, sb3));
            var hi = Math.Min(1, Math.Max(sb0, sb3));
            if (lo > hi)
                return;

            foreach (var s in lo == hi ? [lo] : new[] { lo, hi })
            {
                var px = a.X0 + d1x * s;
                var py = a.Y0 + d1y * s;
                var u = Math.Clamp(((px - b.X0) * d2x + (py - b.Y0) * d2y) / (len2 * len2), 0, 1);
                found.Add((a0 + s * (a1 - a0), b0 + u * (b1 - b0)));
            }
        }

        #endregion

        #region Winding

        /// <summary>The winding number (or, for the even-odd rule, 0 or 1) of a shape around a point, by casting a ray to the right.</summary>
        private static int WindingAt(Shape shape, double x, double y)
        {
            var winding = 0;
            foreach (var curve in shape.Curves)
                winding += Crossings(curve, x, y, 0);

            return shape.FillMode == FillMode.EvenOdd ? winding & 1 : winding;
        }

        private static int Crossings(Bez c, double x, double y, int depth)
        {
            // A curve stays inside the hull of its control points, so it cannot cross the ray if the hull is wholly on one side of it.
            if (c.MaxX < x)
                return 0;

            var allAbove = c.Y0 > y && c.Y1 > y && c.Y2 > y && c.Y3 > y;
            var allBelow = c.Y0 <= y && c.Y1 <= y && c.Y2 <= y && c.Y3 <= y;
            if (allAbove || allBelow)
                return 0;

            if (depth >= 30 || c.IsFlat(1e-12 * Math.Max(1, Math.Max(Math.Abs(c.MaxX), Math.Abs(c.MaxY)))))
            {
                var rising = c.Y3 > c.Y0;
                if ((c.Y0 <= y) == (c.Y3 <= y))
                    return 0;

                var crossX = c.X0 + (y - c.Y0) * (c.X3 - c.X0) / (c.Y3 - c.Y0);
                return crossX > x ? (rising ? 1 : -1) : 0;
            }

            var (left, right) = c.Split(0.5);
            return Crossings(left, x, y, depth + 1) + Crossings(right, x, y, depth + 1);
        }

        #endregion

        #region Linking

        private static void Link(List<Bez> kept, double tolerance, GraphicsPath destination)
        {
            var used = new bool[kept.Count];
            for (var s = 0; s < kept.Count; s++)
            {
                if (used[s])
                    continue;

                used[s] = true;
                var chain = new List<Bez> { kept[s] };
                var startX = kept[s].X0;
                var startY = kept[s].Y0;

                while (true)
                {
                    var tail = chain[^1];
                    if (Near(tail.X3, tail.Y3, startX, startY, tolerance))
                        break;

                    var best = -1;
                    var bestDistance = double.MaxValue;
                    for (var k = 0; k < kept.Count; k++)
                    {
                        if (used[k])
                            continue;

                        var d = Math.Abs(kept[k].X0 - tail.X3) + Math.Abs(kept[k].Y0 - tail.Y3);
                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            best = k;
                        }
                    }

                    if (best < 0 || bestDistance > tolerance)
                        break;

                    used[best] = true;
                    chain.Add(kept[best]);
                }

                destination.Start(chain[0].X0, chain[0].Y0);
                foreach (var piece in chain)
                {
                    if (piece.IsLine)
                        destination.LineTo(piece.X3, piece.Y3);
                    else
                        destination.AddBezierTo(piece.X1, piece.Y1, piece.X2, piece.Y2, piece.X3, piece.Y3);
                }

                destination.CloseFigure();
            }
        }

        private static bool Near(double x0, double y0, double x1, double y1, double tolerance) =>
            Math.Abs(x0 - x1) <= tolerance && Math.Abs(y0 - y1) <= tolerance;

        #endregion
    }
}

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

using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeachDrawing.Core
{
    /// <summary>
    /// Adapter for platform specific graphics path object - used to render (draw/fill) path shape.
    /// </summary>
    /// <remarks>
    /// The recorder methods (<see cref="Start"/>/<see cref="LineTo"/>/<see cref="ArcTo(double,double,double,double,Corner)"/>/
    /// <see cref="AddMove"/>/<see cref="AddBezierTo"/>/<see cref="AddArc"/>/<see cref="CloseFigure"/>) are
    /// <c>virtual</c>, not <c>abstract</c>: the base implementation also records a concrete, backend-agnostic
    /// segment list (every arc converted to an equivalent cubic Bézier at record time, the same way a
    /// concrete backend's own native path representation typically does - see <see cref="Transform"/>'s own
    /// remarks on why that makes an affine transform exact regardless of which segment type it touches), so
    /// <see cref="Flatten"/> works for any subclass without that subclass doing anything extra. A concrete
    /// override still does its own native-representation work and must call the base method too (e.g.
    /// <c>base.Start(x, y)</c>) to keep the shared segment list in sync.
    /// </remarks>
    public abstract class GraphicsPath : IDisposable
    {
        #region Shared segment recording (backs Flatten)

        private abstract class Segment;
        private sealed class LineSegment(double x, double y) : Segment { public readonly double X = x, Y = y; }
        private sealed class CubicSegment(double x1, double y1, double x2, double y2, double x3, double y3) : Segment
        {
            public readonly double X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, X3 = x3, Y3 = y3;
        }

        private sealed class RecordedContour
        {
            public double StartX, StartY;
            public readonly List<Segment> Segments = [];
            public bool Closed;
        }

        private readonly List<RecordedContour> _contours = [];
        private RecordedContour? _current;
        private double _lastX, _lastY;

        private RecordedContour BeginContour(double x, double y)
        {
            var c = new RecordedContour { StartX = x, StartY = y };
            _contours.Add(c);
            _current = c;
            _lastX = x;
            _lastY = y;
            return c;
        }

        /// <summary>
        /// Seeds this path's shared segment list directly from already-flat (line-only) contours - used
        /// when a concrete subclass builds a result that is already flattened (e.g. <see cref="ClipToRect"/>'s
        /// polygon clip), so <see cref="Flatten"/> doesn't need to re-derive anything.
        /// </summary>
        protected void SeedFlatContours(IEnumerable<(IReadOnlyList<(double X, double Y)> Points, bool Closed)> contours)
        {
            foreach (var (points, closed) in contours)
            {
                if (points.Count == 0)
                    continue;

                var c = BeginContour(points[0].X, points[0].Y);
                for (var i = 1; i < points.Count; i++)
                    c.Segments.Add(new LineSegment(points[i].X, points[i].Y));
                c.Closed = closed;
            }

            _current = null;
        }

        #endregion

        /// <summary>
        /// Start path at the given point.
        /// </summary>
        public virtual void Start(double x, double y) => BeginContour(x, y);

        /// <summary>
        /// Add straight line to the given point from the last point.
        /// </summary>
        public virtual void LineTo(double x, double y)
        {
            (_current ??= BeginContour(x, y)).Segments.Add(new LineSegment(x, y));
            _lastX = x;
            _lastY = y;
        }

        /// <summary>
        /// Add elliptical arc with separate horizontal and vertical radii to the given point.
        /// </summary>
        public virtual void ArcTo(double x, double y, double radiusX, double radiusY, Corner corner)
        {
            // The same bounding-box construction every concrete backend's own corner-arc uses: a quarter
            // ellipse (always a 90-degree sweep, never the "large arc") between the last point and
            // (x, y). Center derived directly from the box rather than via AddArc's general endpoint
            // parameterization, since the box is already known here - see GetStartAngleDegrees.
            var left = Math.Min(x, _lastX) - (corner is Corner.TopRight or Corner.BottomRight ? radiusX : 0);
            var top = Math.Min(y, _lastY) - (corner is Corner.BottomLeft or Corner.BottomRight ? radiusY : 0);
            var centerX = left + radiusX;
            var centerY = top + radiusY;
            var startDeg = GetStartAngleDegrees(corner);

            AppendEllipseArc(centerX, centerY, radiusX, radiusY, startDeg * Math.PI / 180, (startDeg + 90) * Math.PI / 180);
            _lastX = x;
            _lastY = y;
        }

        /// <summary>
        /// Add circular arc of the given size to the given point from the last point.
        /// </summary>
        public void ArcTo(double x, double y, double size, Corner corner) => ArcTo(x, y, size, size, corner);

        /// <summary>
        /// Start a new subpath at the given point without connecting it to the previous subpath
        /// (unlike <see cref="Start"/>, which only remembers the point and lets the next
        /// <see cref="LineTo"/>/<see cref="AddBezierTo"/>/<see cref="AddArc"/> call implicitly connect
        /// to it if a subpath is already open). Needed for paths with multiple disjoint subpaths, e.g.
        /// an SVG <c>d</c> attribute with more than one <c>M</c> command, or a clip region built from
        /// several shapes in one path.
        /// </summary>
        public virtual void AddMove(double x, double y) => BeginContour(x, y);

        /// <summary>
        /// Add a cubic Bezier curve from the last point through the two given control points to the given end point.
        /// </summary>
        public virtual void AddBezierTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            (_current ??= BeginContour(_lastX, _lastY)).Segments.Add(new CubicSegment(x1, y1, x2, y2, x3, y3));
            _lastX = x3;
            _lastY = y3;
        }

        /// <summary>
        /// Add an elliptical arc (SVG-style parameterization) from the last point to the given end point.
        /// </summary>
        /// <param name="x">the x-coordinate of the arc's end point</param>
        /// <param name="y">the y-coordinate of the arc's end point</param>
        /// <param name="radiusX">the ellipse's x-radius</param>
        /// <param name="radiusY">the ellipse's y-radius</param>
        /// <param name="rotationAngle">the ellipse's x-axis rotation, in degrees</param>
        /// <param name="isLargeArc">whether to take the larger of the two possible arcs</param>
        /// <param name="sweepClockwise">whether the arc is drawn in the clockwise direction</param>
        public virtual void AddArc(double x, double y, double radiusX, double radiusY, double rotationAngle, bool isLargeArc, bool sweepClockwise)
        {
            if (EllipticalArc.TryGetCenterParameterization(_lastX, _lastY, x, y, radiusX, radiusY,
                    rotationAngle * Math.PI / 180, isLargeArc, sweepClockwise, out var arc))
            {
                AppendEllipseArc(arc.CenterX, arc.CenterY, arc.RadiusX, arc.RadiusY, arc.StartAngle, arc.EndAngle, arc.RotationRadians);
            }
            else
            {
                // Degenerate (coincident endpoints, or a zero radius): SVG treats this as a straight line.
                LineTo(x, y);
                return;
            }

            _lastX = x;
            _lastY = y;
        }

        /// <summary>
        /// Appends an elliptical arc, converted to cubic Bézier segments (at most 90 degrees each, so the
        /// standard 4/3*tan(Δ/4) control-point approximation stays within a fraction of a percent of the
        /// true ellipse), to the current contour.
        /// </summary>
        private void AppendEllipseArc(double centerX, double centerY, double radiusX, double radiusY, double startAngle, double endAngle, double rotation = 0)
        {
            _current ??= BeginContour(centerX + radiusX * Math.Cos(startAngle), centerY + radiusY * Math.Sin(startAngle));

            var total = endAngle - startAngle;
            var segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(total) / (Math.PI / 2) - 1e-9));
            var step = total / segments;
            var cosRot = Math.Cos(rotation);
            var sinRot = Math.Sin(rotation);

            (double X, double Y) PointAt(double angle)
            {
                var ex = radiusX * Math.Cos(angle);
                var ey = radiusY * Math.Sin(angle);
                return (centerX + ex * cosRot - ey * sinRot, centerY + ex * sinRot + ey * cosRot);
            }

            (double X, double Y) TangentAt(double angle)
            {
                var ex = -radiusX * Math.Sin(angle);
                var ey = radiusY * Math.Cos(angle);
                return (ex * cosRot - ey * sinRot, ex * sinRot + ey * cosRot);
            }

            var a0 = startAngle;
            for (var i = 0; i < segments; i++)
            {
                var a1 = a0 + step;
                var p0 = PointAt(a0);
                var p1 = PointAt(a1);
                var t0 = TangentAt(a0);
                var t1 = TangentAt(a1);

                // Standard cubic-Bézier approximation of a circular/elliptical arc segment (de Casteljau's
                // "kappa" construction): the control-point distance along each endpoint's tangent that
                // makes the Bézier's midpoint and the true arc's midpoint coincide.
                var k = 4.0 / 3.0 * Math.Tan(step / 4);

                _current.Segments.Add(new CubicSegment(
                    p0.X + k * t0.X, p0.Y + k * t0.Y,
                    p1.X - k * t1.X, p1.Y - k * t1.Y,
                    p1.X, p1.Y));

                a0 = a1;
            }
        }

        /// <summary>Marks the current contour closed - connecting its end back to its start, per <see cref="CloseFigure"/>.</summary>
        public virtual void CloseFigure()
        {
            if (_current is not null)
                _current.Closed = true;
        }

        /// <summary>
        /// Applies an affine transform to every point already added to the path. Because the concrete
        /// path stores arcs as Bézier segments (which are closed under affine transforms), this is an
        /// exact transform of the whole geometry for any matrix - translate, scale, rotate or skew.
        /// Used to bake a clip shape's own <c>transform</c> into its geometry, since a clip region is
        /// built as a single path and cannot use the graphics-state transform (which would apply to the
        /// whole clip rather than one contributing shape).
        /// </summary>
        public virtual void Transform(Matrix3x2 matrix)
        {
            (double X, double Y) Apply(double x, double y) =>
                (x * matrix.M11 + y * matrix.M21 + matrix.M31, x * matrix.M12 + y * matrix.M22 + matrix.M32);

            foreach (var contour in _contours)
            {
                (contour.StartX, contour.StartY) = Apply(contour.StartX, contour.StartY);
                for (var i = 0; i < contour.Segments.Count; i++)
                {
                    contour.Segments[i] = contour.Segments[i] switch
                    {
                        LineSegment l => TransformLine(l),
                        CubicSegment c => TransformCubic(c),
                        var s => s,
                    };
                }
            }

            return;

            LineSegment TransformLine(LineSegment l)
            {
                var (x, y) = Apply(l.X, l.Y);
                return new LineSegment(x, y);
            }

            CubicSegment TransformCubic(CubicSegment c)
            {
                var (x1, y1) = Apply(c.X1, c.Y1);
                var (x2, y2) = Apply(c.X2, c.Y2);
                var (x3, y3) = Apply(c.X3, c.Y3);
                return new CubicSegment(x1, y1, x2, y2, x3, y3);
            }
        }

        /// <summary>
        /// Appends <paramref name="path"/>'s geometry as one or more disjoint subpaths (a union, not a
        /// connected continuation). Used to merge an individually-transformed clip shape into the
        /// combined clip region.
        /// </summary>
        public virtual void AddPath(GraphicsPath path)
        {
            _current = null;
            foreach (var contour in path._contours)
            {
                var copy = new RecordedContour { StartX = contour.StartX, StartY = contour.StartY, Closed = contour.Closed };
                copy.Segments.AddRange(contour.Segments);
                _contours.Add(copy);
            }

            if (_contours.Count > 0)
            {
                var last = _contours[^1];
                _current = last;
                if (last.Segments.Count > 0 && last.Segments[^1] is LineSegment { } l)
                {
                    _lastX = l.X;
                    _lastY = l.Y;
                }
                else if (last.Segments.Count > 0 && last.Segments[^1] is CubicSegment { } c)
                {
                    _lastX = c.X3;
                    _lastY = c.Y3;
                }
                else
                {
                    _lastX = last.StartX;
                    _lastY = last.StartY;
                }
            }
        }

        /// <summary>
        /// Gets or sets how the interior of a self-intersecting path is determined for filling.
        /// </summary>
        public abstract FillMode FillMode { get; set; }

        /// <summary>
        /// Returns a new path holding this path's own filled area (per its own <see cref="FillMode"/>)
        /// intersected with the axis-aligned rectangle <paramref name="rect"/> - the geometric
        /// equivalent of the clip <see cref="Canvas.PushClip(Rect)"/>/<see cref="Canvas.PopClip"/>
        /// would apply around this path at paint time, but expressed as path geometry a caller can union
        /// into a larger clip shape instead (see <c>FragmentPainter.Decorations.cs</c>'s
        /// <c>CollectUprightWord</c>, which needs each upright character's own glyph outline confined to
        /// its own reserved cell - the same cell <c>PaintUprightVerticalRun</c> already clips *painting*
        /// to for a font with real vertical metrics - before the outline is added to the
        /// <c>background-clip: text</c> union).
        /// </summary>
        /// <remarks>
        /// Deliberately narrower than a general polygon-boolean intersection between two arbitrary
        /// paths: only the clip region is required to be an axis-aligned rectangle, which keeps a correct
        /// implementation cheap (Sutherland-Hodgman against a convex clip window, which is exact for any
        /// subject contour regardless of its own winding or convexity - see a concrete implementation's
        /// own remarks) rather than requiring a general-purpose polygon-clipping library. A curve is
        /// flattened to line segments as part of the clip (an axis-aligned rectangle clip of a cubic
        /// Bézier is not itself expressible as a cubic Bézier in general), so the returned path's
        /// fidelity is bounded by whatever flattening tolerance the concrete implementation chooses -
        /// fine for a filled text-clip shape, where sub-point deviation is invisible, but not intended as
        /// a general-purpose curve-preserving clip.
        /// </remarks>
        public abstract GraphicsPath ClipToRect(Rect rect);

        /// <summary>
        /// Release path resources.
        /// </summary>
        public abstract void Dispose();

        /// <summary>
        /// Flattens this path's recorded geometry (independent of whatever native representation a
        /// concrete subclass also builds) into polylines within <paramref name="tolerance"/> of the true
        /// curve, one <see cref="PathContour"/> per subpath. Lets any <see cref="Canvas"/> backend -
        /// the raster backend, or a third party's own - read this path's geometry without knowing which
        /// concrete <see cref="GraphicsPath"/> built it.
        /// </summary>
        public IReadOnlyList<PathContour> Flatten(double tolerance)
        {
            if (tolerance <= 0 || double.IsNaN(tolerance))
                tolerance = 0.1;

            var result = new List<PathContour>(_contours.Count);
            foreach (var contour in _contours)
            {
                var points = new List<PaintPoint> { new(contour.StartX, contour.StartY) };
                var (px, py) = (contour.StartX, contour.StartY);
                foreach (var segment in contour.Segments)
                {
                    switch (segment)
                    {
                        case LineSegment l:
                            points.Add(new PaintPoint(l.X, l.Y));
                            (px, py) = (l.X, l.Y);
                            break;
                        case CubicSegment c:
                            AppendFlattenedCubic(points, px, py, c.X1, c.Y1, c.X2, c.Y2, c.X3, c.Y3, tolerance);
                            (px, py) = (c.X3, c.Y3);
                            break;
                    }
                }

                result.Add(new PathContour(points, contour.Closed));
            }

            return result;
        }

        /// <summary>Wang's-formula uniform subdivision - the same bound <c>Raster/FlatPath.cs</c> uses, so
        /// flattening this path directly agrees with flattening the same geometry via a concrete backend's
        /// own native path representation.</summary>
        private static void AppendFlattenedCubic(List<PaintPoint> points, double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, double tolerance)
        {
            var ddx = Math.Max(Math.Abs(x0 - 2 * x1 + x2), Math.Abs(x1 - 2 * x2 + x3));
            var ddy = Math.Max(Math.Abs(y0 - 2 * y1 + y2), Math.Abs(y1 - 2 * y2 + y3));
            var dd = Math.Sqrt(ddx * ddx + ddy * ddy);
            var n = (int)Math.Ceiling(Math.Sqrt(0.75 * dd / tolerance));
            if (n < 1) n = 1;
            else if (n > 500) n = 500;

            for (var s = 1; s <= n; s++)
            {
                var t = (double)s / n;
                var mt = 1 - t;
                var a = mt * mt * mt;
                var b = 3 * mt * mt * t;
                var c = 3 * mt * t * t;
                var d = t * t * t;
                points.Add(new PaintPoint(a * x0 + b * x1 + c * x2 + d * x3, a * y0 + b * y1 + c * y2 + d * y3));
            }
        }

        private static double GetStartAngleDegrees(Corner corner) => corner switch
        {
            Corner.TopLeft => 180,
            Corner.TopRight => 270,
            Corner.BottomLeft => 90,
            Corner.BottomRight => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(corner)),
        };

        /// <summary>
        /// The 4 corners that are handled in arc rendering.
        /// </summary>
        public enum Corner
        {
            /// <summary>The box's top-left corner.</summary>
            TopLeft,
            /// <summary>The box's top-right corner.</summary>
            TopRight,
            /// <summary>The box's bottom-left corner.</summary>
            BottomLeft,
            /// <summary>The box's bottom-right corner.</summary>
            BottomRight,
        }
    }
}

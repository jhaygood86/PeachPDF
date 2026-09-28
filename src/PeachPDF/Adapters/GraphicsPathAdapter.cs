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

using PeachDrawing.Abstractions;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeachPDF.Adapters
{
    /// <summary>
    /// Adapter for WinForms graphics path object for core.
    /// </summary>
    internal sealed class GraphicsPathAdapter : GraphicsPath
    {
        /// <summary>
        /// The actual PdfSharp graphics path instance.
        /// </summary>
        private readonly XGraphicsPath _graphicsPath;

        /// <summary>
        /// the last point added to the path to begin next segment from
        /// </summary>
        private PaintPoint _lastPoint;

        public GraphicsPathAdapter()
        {
            _graphicsPath = new XGraphicsPath();
        }

        /// <summary>Wraps an already-built <see cref="XGraphicsPath"/> - used by <see cref="ClipToRect"/> to
        /// hand back its clipped result without another round-trip through the public path-building API.
        /// <see cref="_lastPoint"/> is seeded from the wrapped path's own last point (not left at the
        /// parameterless constructor's implicit (0,0)) so a caller that keeps building onto the returned
        /// path - e.g. a further <see cref="LineTo"/> - continues from where the clipped geometry actually
        /// ends, not from the origin.</summary>
        private GraphicsPathAdapter(XGraphicsPath graphicsPath)
        {
            _graphicsPath = graphicsPath;
            var points = graphicsPath._corePath.PathPointsSpan;
            _lastPoint = points.Length > 0 ? new PaintPoint(points[^1].X, points[^1].Y) : new PaintPoint(0, 0);
            SeedFlatContours(ReadFlatContours(graphicsPath));
        }

        /// <summary>
        /// Walks an already-curve-flattened <see cref="XGraphicsPath"/>'s raw point/type data (e.g. the
        /// result of <see cref="XGraphicsPath.ClipToRect"/>, whose own <c>CoreGraphicsPath.ClipToRect</c>
        /// flattens every curve to line segments as part of the clip - see that method's own remarks) into
        /// per-contour point lists, so the base <see cref="GraphicsPath"/>'s shared segment list can be
        /// seeded without re-deriving anything. Assumes no Bézier (type 3) points remain - true for
        /// <see cref="ClipToRect"/>'s result, the only caller.
        /// </summary>
        private static List<(IReadOnlyList<(double X, double Y)> Points, bool Closed)> ReadFlatContours(XGraphicsPath graphicsPath)
        {
            var result = new List<(IReadOnlyList<(double X, double Y)> Points, bool Closed)>();
            var points = graphicsPath._corePath.PathPointsSpan;
            var types = graphicsPath._corePath.PathTypesSpan;

            List<(double X, double Y)>? current = null;
            for (var i = 0; i < points.Length; i++)
            {
                var type = types[i] & 0x07;
                var closes = (types[i] & 0x80) != 0;

                if (type == 0)
                {
                    if (current is { Count: > 0 })
                        result.Add((current, false));
                    current = [(points[i].X, points[i].Y)];
                }
                else
                {
                    current ??= [(points[i].X, points[i].Y)];
                    current.Add((points[i].X, points[i].Y));
                }

                if (closes && current is { Count: > 0 })
                {
                    result.Add((current, true));
                    current = null;
                }
            }

            if (current is { Count: > 0 })
                result.Add((current, false));

            return result;
        }

        /// <summary>
        /// The actual PdfSharp graphics path instance.
        /// </summary>
        public XGraphicsPath GraphicsPath
        {
            get { return _graphicsPath; }
        }

        public override void Start(double x, double y)
        {
            base.Start(x, y);
            _lastPoint = new PaintPoint(x, y);
        }

        public override void LineTo(double x, double y)
        {
            base.LineTo(x, y);
            _graphicsPath.AddLine((float)_lastPoint.X, (float)_lastPoint.Y, (float)x, (float)y);
            _lastPoint = new PaintPoint(x, y);
        }

        public override void ArcTo(double x, double y, double radiusX, double radiusY, Corner corner)
        {
            base.ArcTo(x, y, radiusX, radiusY, corner);
            float left = (float)(Math.Min(x, _lastPoint.X) - (corner == Corner.TopRight || corner == Corner.BottomRight ? radiusX : 0));
            float top = (float)(Math.Min(y, _lastPoint.Y) - (corner == Corner.BottomLeft || corner == Corner.BottomRight ? radiusY : 0));
            _graphicsPath.AddArc(left, top, (float)radiusX * 2, (float)radiusY * 2, GetStartAngle(corner), 90);
            _lastPoint = new PaintPoint(x, y);
        }

        public override void AddMove(double x, double y)
        {
            base.AddMove(x, y);
            _graphicsPath.AddMove(x, y);
            _lastPoint = new PaintPoint(x, y);
        }

        public override void AddBezierTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            base.AddBezierTo(x1, y1, x2, y2, x3, y3);
            _graphicsPath.AddBezier(_lastPoint.X, _lastPoint.Y, x1, y1, x2, y2, x3, y3);
            _lastPoint = new PaintPoint(x3, y3);
        }

        public override void AddArc(double x, double y, double radiusX, double radiusY, double rotationAngle, bool isLargeArc, bool sweepClockwise)
        {
            base.AddArc(x, y, radiusX, radiusY, rotationAngle, isLargeArc, sweepClockwise);
            _graphicsPath.AddArc(
                new XPoint(_lastPoint.X, _lastPoint.Y),
                new XPoint(x, y),
                new XSize(radiusX, radiusY),
                rotationAngle,
                isLargeArc,
                sweepClockwise ? XSweepDirection.Clockwise : XSweepDirection.Counterclockwise);
            _lastPoint = new PaintPoint(x, y);
        }

        public override void CloseFigure()
        {
            base.CloseFigure();
            _graphicsPath.CloseFigure();
        }

        public override void Transform(Matrix3x2 matrix)
        {
            base.Transform(matrix);
            // Offsets are applied as-is (no PixelsPerPoint scaling): these path coordinates are the raw
            // user-space values that reach the backend un-scaled via IntersectClip, so a user-space
            // transform composes with them directly - the user-to-point conversion happens later at the
            // ambient-CTM boundary.
            _graphicsPath.Transform(new XMatrix(matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.M31, matrix.M32));
        }

        public override void AddPath(GraphicsPath path)
        {
            base.AddPath(path);
            _graphicsPath.AppendPath(((GraphicsPathAdapter)path).GraphicsPath);
        }

        public override FillMode FillMode
        {
            get => _graphicsPath.FillMode == XFillMode.Winding ? FillMode.Nonzero : FillMode.EvenOdd;
            set => _graphicsPath.FillMode = value == FillMode.Nonzero ? XFillMode.Winding : XFillMode.Alternate;
        }

        public override GraphicsPath ClipToRect(Rect rect)
        {
            var clippedXPath = _graphicsPath.ClipToRect(new XRect(rect.X, rect.Y, rect.Width, rect.Height));
            return new GraphicsPathAdapter(clippedXPath);
        }

        public override void Dispose()
        { }

        /// <summary>
        /// Get arc start angle for the given corner.
        /// </summary>
        private static int GetStartAngle(Corner corner)
        {
            int startAngle;
            switch (corner)
            {
                case Corner.TopLeft:
                    startAngle = 180;
                    break;
                case Corner.TopRight:
                    startAngle = 270;
                    break;
                case Corner.BottomLeft:
                    startAngle = 90;
                    break;
                case Corner.BottomRight:
                    startAngle = 0;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("corner");
            }
            return startAngle;
        }
    }
}
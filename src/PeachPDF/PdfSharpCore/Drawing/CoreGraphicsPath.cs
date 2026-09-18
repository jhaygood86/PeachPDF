#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   Stefan Lange
//
// Copyright (c) 2005-2016 empira Software GmbH, Cologne Area (Germany)
//
// http://www.PdfSharp.com
// http://sourceforge.net/projects/pdfsharp
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
#endregion
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PeachPDF.PdfSharpCore.Drawing
{
    /// <summary>
    /// Represents a graphics path that uses the same notation as GDI+.
    /// </summary>
    internal class CoreGraphicsPath
    {
        // Same values as GDI+ uses.
        const byte PathPointTypeStart = 0;  // move
        const byte PathPointTypeLine = 1;  // line
        const byte PathPointTypeBezier = 3;  // default Bezier (= cubic Bezier)
        const byte PathPointTypePathTypeMask = 0x07;  // type mask (lowest 3 bits).
        const byte PathPointTypeCloseSubpath = 0x80;  // closed flag

        public CoreGraphicsPath()
        {
            _points = new List<XPoint>();
            _types = new List<byte>();
        }

        public CoreGraphicsPath(int capacity)
        {
            _points = new List<XPoint>(capacity);
            _types = new List<byte>(capacity);
        }

        public CoreGraphicsPath(CoreGraphicsPath path)
        {
            _points = new List<XPoint>(path._points);
            _types = new List<byte>(path._types);
        }

        public void MoveOrLineTo(double x, double y)
        {
            // Make a MoveTo if there is no previous subpath or the previous subpath was closed.
            // Otherwise make a LineTo.
            if (_types.Count == 0 || (_types[_types.Count - 1] & PathPointTypeCloseSubpath) == PathPointTypeCloseSubpath)
                MoveTo(x, y);
            else
                LineTo(x, y, false);
        }

        public void MoveTo(double x, double y)
        {
            _points.Add(new XPoint(x, y));
            _types.Add(PathPointTypeStart);
        }

        public void LineTo(double x, double y, bool closeSubpath)
        {
            if (_points.Count > 0 && _points[_points.Count - 1].Equals(new XPoint(x, y)))
                return;

            _points.Add(new XPoint(x, y));
            _types.Add((byte)(PathPointTypeLine | (closeSubpath ? PathPointTypeCloseSubpath : 0)));
        }

        public void BezierTo(double x1, double y1, double x2, double y2, double x3, double y3, bool closeSubpath)
        {
            _points.Add(new XPoint(x1, y1));
            _types.Add(PathPointTypeBezier);
            _points.Add(new XPoint(x2, y2));
            _types.Add(PathPointTypeBezier);
            _points.Add(new XPoint(x3, y3));
            _types.Add((byte)(PathPointTypeBezier | (closeSubpath ? PathPointTypeCloseSubpath : 0)));
        }

        /// <summary>
        /// Adds an arc that fills exactly one quadrant (quarter) of an ellipse.
        /// Just a quick hack to draw rounded rectangles before AddArc is fully implemented.
        /// </summary>
        public void QuadrantArcTo(double x, double y, double width, double height, int quadrant, bool clockwise)
        {
            if (width < 0)
                throw new ArgumentOutOfRangeException("width");
            if (height < 0)
                throw new ArgumentOutOfRangeException("height");

            double w = Const.κ * width;
            double h = Const.κ * height;
            double x1, y1, x2, y2, x3, y3;
            switch (quadrant)
            {
                case 1:
                    if (clockwise)
                    {
                        x1 = x + w;
                        y1 = y - height;
                        x2 = x + width;
                        y2 = y - h;
                        x3 = x + width;
                        y3 = y;
                    }
                    else
                    {
                        x1 = x + width;
                        y1 = y - h;
                        x2 = x + w;
                        y2 = y - height;
                        x3 = x;
                        y3 = y - height;
                    }
                    break;

                case 2:
                    if (clockwise)
                    {
                        x1 = x - width;
                        y1 = y - h;
                        x2 = x - w;
                        y2 = y - height;
                        x3 = x;
                        y3 = y - height;
                    }
                    else
                    {
                        x1 = x - w;
                        y1 = y - height;
                        x2 = x - width;
                        y2 = y - h;
                        x3 = x - width;
                        y3 = y;
                    }
                    break;

                case 3:
                    if (clockwise)
                    {
                        x1 = x - w;
                        y1 = y + height;
                        x2 = x - width;
                        y2 = y + h;
                        x3 = x - width;
                        y3 = y;
                    }
                    else
                    {
                        x1 = x - width;
                        y1 = y + h;
                        x2 = x - w;
                        y2 = y + height;
                        x3 = x;
                        y3 = y + height;
                    }
                    break;

                case 4:
                    if (clockwise)
                    {
                        x1 = x + width;
                        y1 = y + h;
                        x2 = x + w;
                        y2 = y + height;
                        x3 = x;
                        y3 = y + height;
                    }
                    else
                    {
                        x1 = x + w;
                        y1 = y + height;
                        x2 = x + width;
                        y2 = y + h;
                        x3 = x + width;
                        y3 = y;
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException("quadrant");
            }
            BezierTo(x1, y1, x2, y2, x3, y3, false);
        }

        /// <summary>
        /// Closes the current subpath.
        /// </summary>
        public void CloseSubpath()
        {
            int count = _types.Count;
            if (count > 0)
                _types[count - 1] |= PathPointTypeCloseSubpath;
        }

        /// <summary>
        /// Gets or sets the current fill mode (alternate or winding).
        /// </summary>
        XFillMode FillMode
        {
            get { return _fillMode; }
            set { _fillMode = value; }
        }
        XFillMode _fillMode;

        public void AddArc(double x, double y, double width, double height, double startAngle, double sweepAngle)
        {
            XMatrix matrix = XMatrix.Identity;
            List<XPoint> points = GeometryHelper.BezierCurveFromArc(x, y, width, height, startAngle, sweepAngle, PathStart.MoveTo1st, ref matrix);
            int count = points.Count;
            Debug.Assert((count + 2) % 3 == 0);

            MoveOrLineTo(points[0].X, points[0].Y);
            for (int idx = 1; idx < count; idx += 3)
                BezierTo(points[idx].X, points[idx].Y, points[idx + 1].X, points[idx + 1].Y, points[idx + 2].X, points[idx + 2].Y, false);
        }

        public void AddArc(XPoint point1, XPoint point2, XSize size, double rotationAngle, bool isLargeArg, XSweepDirection sweepDirection)
        {
            List<XPoint> points = GeometryHelper.BezierCurveFromArc(point1, point2, size, rotationAngle, isLargeArg,
                sweepDirection == XSweepDirection.Clockwise, PathStart.MoveTo1st);
            int count = points.Count;
            Debug.Assert((count + 2) % 3 == 0);

            MoveOrLineTo(points[0].X, points[0].Y);
            for (int idx = 1; idx < count; idx += 3)
                BezierTo(points[idx].X, points[idx].Y, points[idx + 1].X, points[idx + 1].Y, points[idx + 2].X, points[idx + 2].Y, false);
        }

        public void AddCurve(XPoint[] points, double tension)
        {
            int count = points.Length;
            if (count < 2)
                throw new ArgumentException("AddCurve requires two or more points.", "points");

            tension /= 3;
            MoveOrLineTo(points[0].X, points[0].Y);
            if (count == 2)
            {
                //figure.Segments.Add(GeometryHelper.CreateCurveSegment(points[0], points[0], points[1], points[1], tension));
                ToCurveSegment(points[0], points[0], points[1], points[1], tension);
            }
            else
            {
                //figure.Segments.Add(GeometryHelper.CreateCurveSegment(points[0], points[0], points[1], points[2], tension));
                ToCurveSegment(points[0], points[0], points[1], points[2], tension);
                for (int idx = 1; idx < count - 2; idx++)
                {
                    //figure.Segments.Add(GeometryHelper.CreateCurveSegment(points[idx - 1], points[idx], points[idx + 1], points[idx + 2], tension));
                    ToCurveSegment(points[idx - 1], points[idx], points[idx + 1], points[idx + 2], tension);
                }
                //figure.Segments.Add(GeometryHelper.CreateCurveSegment(points[count - 3], points[count - 2], points[count - 1], points[count - 1], tension));
                ToCurveSegment(points[count - 3], points[count - 2], points[count - 1], points[count - 1], tension);
            }
        }

        ///// <summary>
        ///// Appends a Bézier curve for a cardinal spline through pt1 and pt2.
        ///// </summary>
        //void ToCurveSegment(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, double tension3, bool closeSubpath)
        //{
        //    BezierTo(
        //        x1 + tension3 * (x2 - x0), y1 + tension3 * (y2 - y0),
        //        x2 - tension3 * (x3 - x1), y2 - tension3 * (y3 - y1),
        //        x2, y2, closeSubpath);
        //}

        void ToCurveSegment(XPoint pt0, XPoint pt1, XPoint pt2, XPoint pt3, double tension3)
        {
            BezierTo(
                pt1.X + tension3 * (pt2.X - pt0.X), pt1.Y + tension3 * (pt2.Y - pt0.Y),
                pt2.X - tension3 * (pt3.X - pt1.X), pt2.Y - tension3 * (pt3.Y - pt1.Y),
                pt2.X, pt2.Y,
                false);
        }

        /// <summary>
        /// Applies an affine transform to every point in the path. Arcs are already stored as Bézier
        /// segments (see AddArc), so transforming the control points is an exact transform of the whole
        /// geometry for any affine matrix.
        /// </summary>
        public void Transform(XMatrix matrix)
        {
            for (int i = 0; i < _points.Count; i++)
                _points[i] = matrix.Transform(_points[i]);
        }

        /// <summary>
        /// Appends another path's points and segment types as disjoint subpath(s). The other path's
        /// first point is a MoveTo, so it starts a new subpath rather than connecting to this one.
        /// </summary>
        public void Append(CoreGraphicsPath other)
        {
            _points.AddRange(other._points);
            _types.AddRange(other._types);
        }

        /// <summary>
        /// Returns a new path holding this path's own contours, each flattened to line segments and
        /// clipped to the axis-aligned rectangle [<paramref name="left"/>, <paramref name="top"/>] -
        /// [<paramref name="right"/>, <paramref name="bottom"/>] via Sutherland-Hodgman against the
        /// rectangle's own four half-planes. See <c>RGraphicsPath.ClipToRect</c> (the public entry
        /// point this backs) for why a rectangle-only clip is sufficient here and why Sutherland-Hodgman
        /// - normally associated with convex *subject* polygons - is exact regardless of this path's own
        /// winding or convexity: clipping against a convex window never introduces a self-intersection a
        /// general two-arbitrary-polygon boolean would have to resolve, so each closed contour can be
        /// clipped independently and the results simply concatenated as disjoint subpaths, same fill mode.
        /// </summary>
        public CoreGraphicsPath ClipToRect(double left, double top, double right, double bottom)
        {
            var result = new CoreGraphicsPath();

            foreach (var contour in EnumerateFlattenedContours())
            {
                var clipped = SutherlandHodgman.ClipToRect(contour, left, top, right, bottom);

                // Fewer than 3 points can't enclose any area at all. That alone isn't sufficient,
                // though: clipping against a degenerate (zero-width or zero-height) rectangle - or a
                // subject edge that runs exactly along a clip boundary - can leave 3+ points that are
                // all collinear (every vertex pinned to the same clip edge), which is still zero area.
                // The shoelace-formula check below catches that case too, so a degenerate rectangle
                // never fabricates a visible sliver of "clipped" geometry from nothing.
                if (clipped.Count < 3 || IsNegligibleArea(clipped)) continue;

                result.MoveTo(clipped[0].X, clipped[0].Y);
                for (var i = 1; i < clipped.Count; i++)
                    result.LineTo(clipped[i].X, clipped[i].Y, false);
                result.CloseSubpath();
            }

            return result;
        }

        /// <summary>
        /// Whether <paramref name="polygon"/> encloses effectively zero area (the shoelace formula,
        /// halved and left unrounded - exact sign/magnitude isn't needed, only "is it indistinguishable
        /// from a degenerate sliver or point"). <see cref="ClipToRect(double,double,double,double)"/>'s
        /// own remarks explain why a point count above the 3-vertex minimum isn't sufficient on its own.
        /// </summary>
        private static bool IsNegligibleArea(List<XPoint> polygon)
        {
            double area = 0;
            var n = polygon.Count;
            for (var i = 0; i < n; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % n];
                area += a.X * b.Y - b.X * a.Y;
            }

            return Math.Abs(area) < 1e-6;
        }

        /// <summary>
        /// Walks this path's own point/type arrays and yields one flattened polygon (a closed,
        /// line-segment-only point list) per subpath - a "Start" type begins a new one, a "Line" type
        /// appends a vertex as-is, and a "Bezier" type (always 3 consecutive points: two control
        /// points then the end point, per <see cref="BezierTo"/>) is subdivided into line segments by
        /// <see cref="BezierFlattener.Flatten"/>. Every subpath built by this class' own drawing methods
        /// (<c>AddRectangle</c>, <c>AddRoundedRectangle</c>, glyph outlines from
        /// <c>GraphicsAdapter.GetTextOutline</c>, etc.) is geometrically closed by construction even when
        /// the close-subpath flag was never explicitly set on it - the sole caller of this method
        /// (<see cref="ClipToRect(double,double,double,double)"/>) only ever needs a *filled* area to
        /// clip, so every yielded polygon is treated as implicitly closed regardless of that flag.
        /// </summary>
        private IEnumerable<List<XPoint>> EnumerateFlattenedContours()
        {
            List<XPoint>? current = null;
            var i = 0;
            while (i < _points.Count)
            {
                var baseType = (byte)(_types[i] & PathPointTypePathTypeMask);
                if (baseType == PathPointTypeStart)
                {
                    if (current is { Count: > 0 }) yield return current;
                    current = [_points[i]];
                    i++;
                }
                else if (baseType == PathPointTypeBezier)
                {
                    // Malformed input (a Bezier type with no prior point to start the curve from) -
                    // skip the segment defensively rather than throw, since a clip-shape utility
                    // should degrade to "less clipped" on bad input, not crash the paint pass.
                    if (current is null || current.Count == 0 || i + 2 >= _points.Count)
                    {
                        i++;
                        continue;
                    }

                    var start = current[^1];
                    BezierFlattener.Flatten(start, _points[i], _points[i + 1], _points[i + 2], current);
                    i += 3;
                }
                else
                {
                    current ??= [];
                    current.Add(_points[i]);
                    i++;
                }
            }

            if (current is { Count: > 0 }) yield return current;
        }

        /// <summary>
        /// Gets the path points in GDI+ style.
        /// </summary>
        public XPoint[] PathPoints { get { return _points.ToArray(); } }

        /// <summary>
        /// Gets the path types in GDI+ style.
        /// </summary>
        public byte[] PathTypes { get { return _types.ToArray(); } }

        internal ReadOnlySpan<XPoint> PathPointsSpan => CollectionsMarshal.AsSpan(_points);

        internal ReadOnlySpan<byte> PathTypesSpan => CollectionsMarshal.AsSpan(_types);

        readonly List<XPoint> _points;
        readonly List<byte> _types;
    }
}

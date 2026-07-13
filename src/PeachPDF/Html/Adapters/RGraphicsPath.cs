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

namespace PeachPDF.Html.Adapters
{
    /// <summary>
    /// Adapter for platform specific graphics path object - used to render (draw/fill) path shape.
    /// </summary>
    internal abstract class RGraphicsPath : IDisposable
    {
        /// <summary>
        /// Start path at the given point.
        /// </summary>
        public abstract void Start(double x, double y);

        /// <summary>
        /// Add stright line to the given point from te last point.
        /// </summary>
        public abstract void LineTo(double x, double y);

        /// <summary>
        /// Add elliptical arc with separate horizontal and vertical radii to the given point.
        /// </summary>
        public abstract void ArcTo(double x, double y, double radiusX, double radiusY, Corner corner);

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
        public abstract void AddMove(double x, double y);

        /// <summary>
        /// Add a cubic Bezier curve from the last point through the two given control points to the given end point.
        /// </summary>
        public abstract void AddBezierTo(double x1, double y1, double x2, double y2, double x3, double y3);

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
        public abstract void AddArc(double x, double y, double radiusX, double radiusY, double rotationAngle, bool isLargeArc, bool sweepClockwise);

        /// <summary>
        /// Close the current subpath, connecting its end back to its start.
        /// </summary>
        public abstract void CloseFigure();

        /// <summary>
        /// Gets or sets how the interior of a self-intersecting path is determined for filling.
        /// </summary>
        public abstract RFillMode FillMode { get; set; }

        /// <summary>
        /// Release path resources.
        /// </summary>
        public abstract void Dispose();

        /// <summary>
        /// The 4 corners that are handled in arc rendering.
        /// </summary>
        internal enum Corner
        {
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight,
        }
    }
}
using System;
using System.Collections.Generic;

namespace PeachDrawing.Core.Geometry
{
    /// <summary>
    /// Adds common shapes to a <see cref="GraphicsPath"/>. Every method starts a new subpath, so shapes can be combined in one
    /// path, and closes it (except <see cref="AddWave"/>, which is an open line); coordinates are in the same units as the path's
    /// other methods.
    /// </summary>
    public static class PathShapes
    {
        /// <summary>
        /// Adds a rectangle whose four corners are each rounded by their own elliptical radii (the shape of a CSS
        /// <c>border-radius</c>). A corner with a zero radius on both axes stays square. The caller is responsible for radii that
        /// fit inside <paramref name="rect"/>: they are used as given.
        /// </summary>
        /// <param name="path">the path to add to</param>
        /// <param name="rect">the rectangle's bounds</param>
        /// <param name="topLeftX">the top-left corner's horizontal radius</param>
        /// <param name="topLeftY">the top-left corner's vertical radius</param>
        /// <param name="topRightX">the top-right corner's horizontal radius</param>
        /// <param name="topRightY">the top-right corner's vertical radius</param>
        /// <param name="bottomRightX">the bottom-right corner's horizontal radius</param>
        /// <param name="bottomRightY">the bottom-right corner's vertical radius</param>
        /// <param name="bottomLeftX">the bottom-left corner's horizontal radius</param>
        /// <param name="bottomLeftY">the bottom-left corner's vertical radius</param>
        public static void AddRoundedRectangle(this GraphicsPath path, Rect rect,
            double topLeftX, double topLeftY, double topRightX, double topRightY,
            double bottomRightX, double bottomRightY, double bottomLeftX, double bottomLeftY)
        {
            ArgumentNullException.ThrowIfNull(path);

            path.Start(rect.Left + topLeftX, rect.Top);
            path.LineTo(rect.Right - topRightX, rect.Top);
            if (topRightX > 0 || topRightY > 0)
                path.ArcTo(rect.Right, rect.Top + topRightY, topRightX, topRightY, GraphicsPath.Corner.TopRight);

            path.LineTo(rect.Right, rect.Bottom - bottomRightY);
            if (bottomRightX > 0 || bottomRightY > 0)
                path.ArcTo(rect.Right - bottomRightX, rect.Bottom, bottomRightX, bottomRightY, GraphicsPath.Corner.BottomRight);

            path.LineTo(rect.Left + bottomLeftX, rect.Bottom);
            if (bottomLeftX > 0 || bottomLeftY > 0)
                path.ArcTo(rect.Left, rect.Bottom - bottomLeftY, bottomLeftX, bottomLeftY, GraphicsPath.Corner.BottomLeft);

            // With a square top-left corner the left edge ends where the path started; closing the figure draws it.
            if (topLeftX > 0 || topLeftY > 0)
            {
                path.LineTo(rect.Left, rect.Top + topLeftY);
                path.ArcTo(rect.Left + topLeftX, rect.Top, topLeftX, topLeftY, GraphicsPath.Corner.TopLeft);
            }

            path.CloseFigure();
        }

        /// <summary>Adds a rectangle with the same circular <paramref name="radius"/> on every corner.</summary>
        /// <param name="path">the path to add to</param>
        /// <param name="rect">the rectangle's bounds</param>
        /// <param name="radius">the corner radius</param>
        public static void AddRoundedRectangle(this GraphicsPath path, Rect rect, double radius) =>
            path.AddRoundedRectangle(rect, radius, radius, radius, radius, radius, radius, radius, radius);

        /// <summary>Adds an ellipse, drawn clockwise from its rightmost point as four quarter arcs.</summary>
        /// <param name="path">the path to add to</param>
        /// <param name="centerX">the center's x-coordinate</param>
        /// <param name="centerY">the center's y-coordinate</param>
        /// <param name="radiusX">the horizontal radius</param>
        /// <param name="radiusY">the vertical radius</param>
        public static void AddEllipse(this GraphicsPath path, double centerX, double centerY, double radiusX, double radiusY)
        {
            ArgumentNullException.ThrowIfNull(path);

            path.Start(centerX + radiusX, centerY);
            path.ArcTo(centerX, centerY + radiusY, radiusX, radiusY, GraphicsPath.Corner.BottomRight);
            path.ArcTo(centerX - radiusX, centerY, radiusX, radiusY, GraphicsPath.Corner.BottomLeft);
            path.ArcTo(centerX, centerY - radiusY, radiusX, radiusY, GraphicsPath.Corner.TopLeft);
            path.ArcTo(centerX + radiusX, centerY, radiusX, radiusY, GraphicsPath.Corner.TopRight);
            path.CloseFigure();
        }

        /// <summary>Adds a circle.</summary>
        /// <param name="path">the path to add to</param>
        /// <param name="centerX">the center's x-coordinate</param>
        /// <param name="centerY">the center's y-coordinate</param>
        /// <param name="radius">the radius</param>
        public static void AddCircle(this GraphicsPath path, double centerX, double centerY, double radius) =>
            path.AddEllipse(centerX, centerY, radius, radius);

        /// <summary>
        /// Adds a pie slice: the center, a straight line out to the start of an elliptical arc, the arc, and a straight line back.
        /// A sweep of a full turn or more adds the whole ellipse instead.
        /// </summary>
        /// <param name="path">the path to add to</param>
        /// <param name="centerX">the center's x-coordinate</param>
        /// <param name="centerY">the center's y-coordinate</param>
        /// <param name="radiusX">the horizontal radius</param>
        /// <param name="radiusY">the vertical radius</param>
        /// <param name="startAngleRadians">where the arc starts, measured from the positive x-axis toward positive y</param>
        /// <param name="sweepRadians">how far the arc runs; positive is toward positive y (clockwise on a y-down canvas)</param>
        public static void AddPie(this GraphicsPath path, double centerX, double centerY, double radiusX, double radiusY,
            double startAngleRadians, double sweepRadians)
        {
            ArgumentNullException.ThrowIfNull(path);

            if (Math.Abs(sweepRadians) >= 2 * Math.PI - 1e-12)
            {
                path.AddEllipse(centerX, centerY, radiusX, radiusY);
                return;
            }

            var endAngle = startAngleRadians + sweepRadians;
            path.Start(centerX, centerY);
            path.LineTo(centerX + radiusX * Math.Cos(startAngleRadians), centerY + radiusY * Math.Sin(startAngleRadians));
            path.AddArc(centerX + radiusX * Math.Cos(endAngle), centerY + radiusY * Math.Sin(endAngle),
                radiusX, radiusY, 0, isLargeArc: Math.Abs(sweepRadians) > Math.PI, sweepClockwise: sweepRadians > 0);
            path.CloseFigure();
        }

        /// <summary>Adds a closed polygon through <paramref name="points"/>. Fewer than two points add nothing.</summary>
        /// <param name="path">the path to add to</param>
        /// <param name="points">the polygon's vertices in order</param>
        public static void AddPolygon(this GraphicsPath path, IReadOnlyList<PaintPoint> points)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(points);

            if (points.Count < 2)
                return;

            path.Start(points[0].X, points[0].Y);
            for (var i = 1; i < points.Count; i++)
                path.LineTo(points[i].X, points[i].Y);

            path.CloseFigure();
        }

        /// <summary>Adds a regular polygon whose first vertex points up (toward negative y) before rotation.</summary>
        /// <param name="path">the path to add to</param>
        /// <param name="centerX">the center's x-coordinate</param>
        /// <param name="centerY">the center's y-coordinate</param>
        /// <param name="radius">the distance from the center to each vertex</param>
        /// <param name="sides">the number of sides; at least 3</param>
        /// <param name="rotationRadians">clockwise rotation about the center</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="sides"/> is less than 3</exception>
        public static void AddRegularPolygon(this GraphicsPath path, double centerX, double centerY, double radius, int sides,
            double rotationRadians = 0)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(sides, 3);
            AddRadialPolygon(path, centerX, centerY, radius, radius, sides, rotationRadians, skipInner: true);
        }

        /// <summary>Adds a star whose first tip points up (toward negative y) before rotation.</summary>
        /// <param name="path">the path to add to</param>
        /// <param name="centerX">the center's x-coordinate</param>
        /// <param name="centerY">the center's y-coordinate</param>
        /// <param name="outerRadius">the distance from the center to each tip</param>
        /// <param name="innerRadius">the distance from the center to each notch between tips</param>
        /// <param name="points">the number of tips; at least 3</param>
        /// <param name="rotationRadians">clockwise rotation about the center</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="points"/> is less than 3</exception>
        public static void AddStar(this GraphicsPath path, double centerX, double centerY, double outerRadius, double innerRadius,
            int points, double rotationRadians = 0)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(points, 3);
            AddRadialPolygon(path, centerX, centerY, outerRadius, innerRadius, points, rotationRadians, skipInner: false);
        }

        private static void AddRadialPolygon(GraphicsPath path, double centerX, double centerY, double outerRadius, double innerRadius,
            int points, double rotationRadians, bool skipInner)
        {
            ArgumentNullException.ThrowIfNull(path);

            var vertices = new List<PaintPoint>(points * 2);
            var step = Math.PI / points;
            for (var i = 0; i < points * 2; i++)
            {
                if (skipInner && i % 2 == 1)
                    continue;

                var radius = i % 2 == 0 ? outerRadius : innerRadius;
                var angle = rotationRadians - Math.PI / 2 + i * step;
                vertices.Add(new PaintPoint(centerX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle)));
            }

            path.AddPolygon(vertices);
        }

        /// <summary>
        /// Adds an open wavy line from <paramref name="start"/> to <paramref name="end"/>: a smooth wave built from cubic Béziers,
        /// one full period every <paramref name="wavelength"/> along the line, swinging <paramref name="amplitude"/> to either side
        /// of it. The wave starts on the line and swings to the left of the direction of travel first.
        /// </summary>
        /// <param name="path">the path to add to</param>
        /// <param name="start">where the wave starts</param>
        /// <param name="end">where the wave ends</param>
        /// <param name="amplitude">the distance from the line to each crest</param>
        /// <param name="wavelength">the length of one full period along the line</param>
        public static void AddWave(this GraphicsPath path, PaintPoint start, PaintPoint end, double amplitude, double wavelength)
        {
            ArgumentNullException.ThrowIfNull(path);

            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            path.Start(start.X, start.Y);
            if (length <= 0 || wavelength <= 0)
                return;

            // Unit vectors along the line and to its left ("left" is (dy, -dx) in a y-down space).
            double ux = dx / length, uy = dy / length;
            double nx = uy, ny = -ux;

            // A half period rises to a crest and a half period falls back; each half is one cubic whose control points sit a
            // third of the way across, pushed out by 4/3 of the amplitude so the curve peaks at exactly the amplitude.
            var half = wavelength / 2;
            var sign = 1.0;
            var position = 0.0;
            while (position < length - 1e-9)
            {
                var span = Math.Min(half, length - position);
                var swing = amplitude * 4.0 / 3.0 * sign * (span / half);
                path.AddBezierTo(
                    start.X + ux * (position + span / 3) + nx * swing, start.Y + uy * (position + span / 3) + ny * swing,
                    start.X + ux * (position + span * 2 / 3) + nx * swing, start.Y + uy * (position + span * 2 / 3) + ny * swing,
                    start.X + ux * (position + span), start.Y + uy * (position + span));
                position += span;
                sign = -sign;
            }
        }
    }
}

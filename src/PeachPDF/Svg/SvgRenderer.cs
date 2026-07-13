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

using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Paints a parsed <see cref="SvgDocument"/> into an <see cref="RGraphics"/>, mapping its
    /// viewBox onto a target viewport rectangle (default <c>xMidYMid meet</c> scaling only - the only
    /// <c>preserveAspectRatio</c> mode supported in v1) and walking the scene graph issuing
    /// <c>RGraphics.DrawPath</c> calls for each shape.
    /// </summary>
    internal static class SvgRenderer
    {
        /// <summary>
        /// Clips to <paramref name="viewportRect"/>, pushes the viewBox-to-viewport transform, renders
        /// every root element of <paramref name="document"/>, then pops both. This is the single entry
        /// point shared by <c>CssBoxSvg.PaintImp</c> (inline <c>&lt;svg&gt;</c>) and
        /// <c>CssBoxImage.PaintImp</c> (<c>&lt;img src="x.svg"&gt;</c>).
        /// </summary>
        public static void RenderInto(RGraphics g, SvgDocument document, RRect viewportRect)
        {
            if (viewportRect.Width <= 0 || viewportRect.Height <= 0)
                return;

            var viewBoxWidth = document.ViewBox?.Width ?? document.Width ?? viewportRect.Width;
            var viewBoxHeight = document.ViewBox?.Height ?? document.Height ?? viewportRect.Height;

            if (viewBoxWidth <= 0 || viewBoxHeight <= 0)
                return;

            var viewBoxX = document.ViewBox?.X ?? 0;
            var viewBoxY = document.ViewBox?.Y ?? 0;

            // xMidYMid meet (the SVG/CSS default): uniform scale, centered, letterboxed.
            var scale = Math.Min(viewportRect.Width / viewBoxWidth, viewportRect.Height / viewBoxHeight);
            var offsetX = viewportRect.X + (viewportRect.Width - viewBoxWidth * scale) / 2 - viewBoxX * scale;
            var offsetY = viewportRect.Y + (viewportRect.Height - viewBoxHeight * scale) / 2 - viewBoxY * scale;
            var matrix = new RMatrix(scale, 0, 0, scale, offsetX, offsetY);

            g.PushClip(viewportRect);
            g.PushTransform(matrix);

            foreach (var element in document.Children)
                RenderElement(g, document, element, 1.0);

            g.PopTransform();
            g.PopClip();
        }

        private static void RenderElement(RGraphics g, SvgDocument document, SvgElement element, double inheritedOpacity)
        {
            var opacity = inheritedOpacity * element.Opacity;
            var pushedTransform = false;
            var pushedClip = false;

            if (element.Transform is { } transform)
            {
                g.PushTransform(transform);
                pushedTransform = true;
            }

            RGraphicsPath? clipPath = null;

            if (element.ClipPathRef is { } clipRef && document.ClipPaths.TryGetValue(clipRef, out var clipDefinition))
            {
                clipPath = BuildClipPath(g, clipDefinition);

                if (clipPath is not null)
                {
                    g.PushClip(clipPath);
                    pushedClip = true;
                }
            }

            switch (element)
            {
                case SvgGroupElement group:
                    foreach (var child in group.Children)
                        RenderElement(g, document, child, opacity);
                    break;

                case SvgPathElement path:
                {
                    using var graphicsPath = BuildPath(g, path);
                    PaintShape(g, document, path, graphicsPath, opacity);
                    break;
                }

                case SvgCircleElement circle:
                {
                    using var graphicsPath = BuildCirclePath(g, circle);
                    PaintShape(g, document, circle, graphicsPath, opacity);
                    break;
                }

                case SvgPolygonElement polygon:
                {
                    using var graphicsPath = BuildPolygonPath(g, polygon);
                    PaintShape(g, document, polygon, graphicsPath, opacity);
                    break;
                }

                case SvgUseElement { Target: { } target } use:
                    if (use.X != 0 || use.Y != 0)
                    {
                        g.PushTransform(new RMatrix(1, 0, 0, 1, use.X, use.Y));
                        RenderElement(g, document, target, opacity);
                        g.PopTransform();
                    }
                    else
                    {
                        RenderElement(g, document, target, opacity);
                    }
                    break;
            }

            if (pushedClip) g.PopClip();
            clipPath?.Dispose();
            if (pushedTransform) g.PopTransform();
        }

        private static void PaintShape(RGraphics g, SvgDocument document, SvgElement element, RGraphicsPath path, double opacity)
        {
            if (element.Fill.Kind != SvgPaintKind.None)
            {
                var brush = ResolvePaintBrush(g, document, element.Fill, opacity);
                if (brush is not null)
                    g.DrawPath(brush, path);
            }

            if (element.Stroke.Kind != SvgPaintKind.None && element.StrokeWidth > 0)
            {
                var pen = ResolveStrokePen(g, document, element, opacity);
                if (pen is not null)
                    g.DrawPath(pen, path);
            }
        }

        private static RBrush? ResolvePaintBrush(RGraphics g, SvgDocument document, SvgPaint paint, double opacity)
        {
            return paint.Kind switch
            {
                SvgPaintKind.Solid => g.GetSolidBrush(ApplyOpacity(paint.Color, opacity)),
                SvgPaintKind.GradientRef when paint.GradientId is { } id && document.Gradients.TryGetValue(id, out var gradient)
                    => ResolveGradientBrush(g, gradient, opacity),
                _ => null,
            };
        }

        private static RBrush? ResolveGradientBrush(RGraphics g, SvgGradient gradient, double opacity)
        {
            if (gradient.Stops.Count == 0)
                return null;

            var stops = gradient.Stops
                .Select(s => (Color: ApplyOpacity(s.Color, opacity), Position: s.Offset))
                .ToArray();

            switch (gradient)
            {
                case SvgLinearGradient linear:
                {
                    var p1 = ApplyMatrix(new RPoint(linear.X1, linear.Y1), gradient.GradientTransform);
                    var p2 = ApplyMatrix(new RPoint(linear.X2, linear.Y2), gradient.GradientTransform);
                    return g.GetLinearGradientBrush(p1, p2, stops);
                }

                case SvgRadialGradient radial:
                {
                    var center = ApplyMatrix(new RPoint(radial.Cx, radial.Cy), gradient.GradientTransform);
                    var (radiusX, radiusY) = ApplyMatrixToRadius(radial.R, gradient.GradientTransform);
                    return g.GetRadialGradientBrush(center, radiusX, radiusY, stops);
                }

                default:
                    return null;
            }
        }

        private static RPen? ResolveStrokePen(RGraphics g, SvgDocument document, SvgElement element, double opacity)
        {
            RColor color;

            if (element.Stroke.Kind == SvgPaintKind.Solid)
            {
                color = element.Stroke.Color;
            }
            else if (element.Stroke.Kind == SvgPaintKind.GradientRef &&
                     element.Stroke.GradientId is { } id &&
                     document.Gradients.TryGetValue(id, out var gradient) &&
                     gradient.Stops.Count > 0)
            {
                // Gradient-paint strokes are out of scope for v1 - approximate with the first stop's
                // solid color rather than skipping the stroke entirely.
                color = gradient.Stops[0].Color;
            }
            else
            {
                return null;
            }

            var pen = g.GetPen(ApplyOpacity(color, opacity));
            pen.Width = element.StrokeWidth;
            pen.MiterLimit = element.StrokeMiterLimit;
            return pen;
        }

        private static RColor ApplyOpacity(RColor color, double opacity)
        {
            if (opacity >= 1.0)
                return color;

            var alpha = (int)Math.Round(color.A * Math.Clamp(opacity, 0.0, 1.0));
            return RColor.FromArgb(alpha, color.R, color.G, color.B);
        }

        private static RPoint ApplyMatrix(RPoint p, RMatrix? matrix)
        {
            if (matrix is not { } m)
                return p;

            return new RPoint(p.X * m.M11 + p.Y * m.M21 + m.OffsetX, p.X * m.M12 + p.Y * m.M22 + m.OffsetY);
        }

        /// <summary>
        /// Transforms a radial gradient's radius as a pair of axis vectors (ignoring translation) -
        /// valid for the translate/scale-only <c>gradientTransform</c> subset supported in v1. A
        /// rotated matrix would turn the circle into a rotated ellipse, which
        /// <see cref="RGraphics.GetRadialGradientBrush"/> has no way to express; documented limitation.
        /// </summary>
        private static (double RadiusX, double RadiusY) ApplyMatrixToRadius(double r, RMatrix? matrix)
        {
            if (matrix is not { } m)
                return (r, r);

            return (Math.Abs(r * m.M11), Math.Abs(r * m.M22));
        }

        private static RGraphicsPath BuildPath(RGraphics g, SvgPathElement path)
        {
            var graphicsPath = g.GetGraphicsPath();
            graphicsPath.FillMode = RFillMode.Nonzero;
            AppendPathSegments(graphicsPath, path.Segments);
            return graphicsPath;
        }

        private static RGraphicsPath BuildCirclePath(RGraphics g, SvgCircleElement circle)
        {
            var graphicsPath = g.GetGraphicsPath();
            graphicsPath.FillMode = RFillMode.Nonzero;
            AppendCircleGeometry(graphicsPath, circle);
            return graphicsPath;
        }

        private static RGraphicsPath BuildPolygonPath(RGraphics g, SvgPolygonElement polygon)
        {
            var graphicsPath = g.GetGraphicsPath();
            graphicsPath.FillMode = RFillMode.Nonzero;
            AppendPolygonGeometry(graphicsPath, polygon);
            return graphicsPath;
        }

        private static RGraphicsPath? BuildClipPath(RGraphics g, SvgClipPath clipPath)
        {
            var path = g.GetGraphicsPath();
            path.FillMode = RFillMode.Nonzero;
            var any = false;

            foreach (var shape in clipPath.Shapes)
                any |= AppendClipShapeGeometry(path, shape);

            if (any)
                return path;

            path.Dispose();
            return null;
        }

        private static bool AppendClipShapeGeometry(RGraphicsPath path, SvgElement shape)
        {
            switch (shape)
            {
                case SvgPathElement { Segments.Count: > 0 } p:
                    AppendPathSegments(path, p.Segments);
                    return true;

                case SvgCircleElement { R: > 0 } c:
                    AppendCircleGeometry(path, c);
                    return true;

                case SvgPolygonElement { Points.Length: > 0 } poly:
                    AppendPolygonGeometry(path, poly);
                    return true;

                case SvgUseElement { Target: { } target }:
                    return AppendClipShapeGeometry(path, target);

                case SvgGroupElement group:
                {
                    var any = false;
                    foreach (var child in group.Children)
                        any |= AppendClipShapeGeometry(path, child);
                    return any;
                }

                default:
                    return false;
            }
        }

        /// <summary>
        /// Appends normalized path segments to <paramref name="path"/>. Every subpath start
        /// (<see cref="PathSegmentKind.MoveTo"/>) uses <see cref="RGraphicsPath.AddMove"/> rather than
        /// <see cref="RGraphicsPath.Start"/> - safe even for the very first point of a brand new path
        /// (the underlying core path dedupes the resulting degenerate zero-length "connector" segment
        /// any subsequent draw call would otherwise implicitly add), and required for correctness when
        /// appending more than one subpath/shape into the same <see cref="RGraphicsPath"/> (e.g. a
        /// multi-subpath <c>d</c> attribute, or a clip region built from several shapes).
        /// </summary>
        private static void AppendPathSegments(RGraphicsPath path, IReadOnlyList<PathSegment> segments)
        {
            foreach (var segment in segments)
            {
                switch (segment.Kind)
                {
                    case PathSegmentKind.MoveTo:
                        path.AddMove(segment.X, segment.Y);
                        break;
                    case PathSegmentKind.LineTo:
                        path.LineTo(segment.X, segment.Y);
                        break;
                    case PathSegmentKind.CubicBezierTo:
                        path.AddBezierTo(segment.X1, segment.Y1, segment.X2, segment.Y2, segment.X, segment.Y);
                        break;
                    case PathSegmentKind.ArcTo:
                        path.AddArc(segment.X, segment.Y, segment.RadiusX, segment.RadiusY, segment.RotationAngle, segment.IsLargeArc, segment.SweepClockwise);
                        break;
                    case PathSegmentKind.ClosePath:
                        path.CloseFigure();
                        break;
                }
            }
        }

        /// <summary>Builds a circle as four quarter-circle elliptical arcs (each becomes an accurate bezier approximation, same machinery already used for CSS border-radius corners).</summary>
        private static void AppendCircleGeometry(RGraphicsPath path, SvgCircleElement circle)
        {
            var cx = circle.Cx;
            var cy = circle.Cy;
            var r = Math.Abs(circle.R);

            if (r <= 0)
                return;

            path.AddMove(cx + r, cy);
            path.AddArc(cx, cy + r, r, r, 0, false, true);
            path.AddArc(cx - r, cy, r, r, 0, false, true);
            path.AddArc(cx, cy - r, r, r, 0, false, true);
            path.AddArc(cx + r, cy, r, r, 0, false, true);
            path.CloseFigure();
        }

        private static void AppendPolygonGeometry(RGraphicsPath path, SvgPolygonElement polygon)
        {
            var points = polygon.Points;

            if (points.Length == 0)
                return;

            path.AddMove(points[0].X, points[0].Y);

            for (var i = 1; i < points.Length; i++)
                path.LineTo(points[i].X, points[i].Y);

            path.CloseFigure();
        }
    }
}

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

            var matrix = ComputeViewportTransform(viewportRect, viewBoxX, viewBoxY, viewBoxWidth, viewBoxHeight, document.PreserveAspectRatio);

            g.PushClip(viewportRect);
            g.PushTransform(matrix);

            var viewport = (viewBoxWidth, viewBoxHeight);
            foreach (var element in document.Children)
                RenderElement(g, document, element, 1.0, viewport);

            g.PopTransform();
            g.PopClip();
        }

        /// <summary>
        /// Walks the scene graph purely to compute the final page-space bounding rectangle of every
        /// <c>&lt;a&gt;</c> element's content, for PDF link-annotation registration. Deliberately
        /// separate from <see cref="RenderInto"/>/<see cref="RenderElement"/> - it never touches
        /// <see cref="RGraphics"/> (no painting, just matrix composition + bounding-box math), so it's
        /// safe to call exactly once regardless of how many times the document is actually painted
        /// (e.g. once per output page during pagination - painting is a repeated "scroll and repaint"
        /// pass in this renderer, which would make link rectangles collected *during* paint duplicate
        /// once per page). Callers should gather link rectangles from this method's output instead of
        /// hooking into paint at all.
        /// </summary>
        public static void CollectLinks(SvgDocument document, RRect viewportRect, List<(RRect Rect, string Href)> sink)
        {
            if (viewportRect.Width <= 0 || viewportRect.Height <= 0)
                return;

            var viewBoxWidth = document.ViewBox?.Width ?? document.Width ?? viewportRect.Width;
            var viewBoxHeight = document.ViewBox?.Height ?? document.Height ?? viewportRect.Height;

            if (viewBoxWidth <= 0 || viewBoxHeight <= 0)
                return;

            var viewBoxX = document.ViewBox?.X ?? 0;
            var viewBoxY = document.ViewBox?.Y ?? 0;
            var matrix = ComputeViewportTransform(viewportRect, viewBoxX, viewBoxY, viewBoxWidth, viewBoxHeight, document.PreserveAspectRatio);

            foreach (var element in document.Children)
                CollectLinksFromElement(element, matrix, sink);
        }

        private static void CollectLinksFromElement(SvgElement element, RMatrix ambientMatrix, List<(RRect Rect, string Href)> sink)
        {
            var matrix = element.Transform is { } t ? MultiplyMatrix(t, ambientMatrix) : ambientMatrix;

            if (element is SvgAnchorElement { Href: { Length: > 0 } href } && SvgGeometryBounds.GetBoundingBox(element) is { } localBounds)
                sink.Add((TransformBoundingBox(localBounds, matrix), href));

            switch (element)
            {
                case SvgGroupElement group:
                    foreach (var child in group.Children)
                        CollectLinksFromElement(child, matrix, sink);
                    break;

                case SvgUseElement { Target: { } target } use:
                    var useMatrix = use.X != 0 || use.Y != 0
                        ? MultiplyMatrix(new RMatrix(1, 0, 0, 1, use.X, use.Y), matrix)
                        : matrix;
                    CollectLinksFromElement(target, useMatrix, sink);
                    break;
            }
        }

        /// <summary>Composes two matrices for row-vector point transformation: applies <paramref name="first"/>, then <paramref name="second"/> (i.e. <c>p' = p * first * second</c>).</summary>
        private static RMatrix MultiplyMatrix(RMatrix first, RMatrix second)
        {
            return new RMatrix(
                first.M11 * second.M11 + first.M12 * second.M21,
                first.M11 * second.M12 + first.M12 * second.M22,
                first.M21 * second.M11 + first.M22 * second.M21,
                first.M21 * second.M12 + first.M22 * second.M22,
                first.OffsetX * second.M11 + first.OffsetY * second.M21 + second.OffsetX,
                first.OffsetX * second.M12 + first.OffsetY * second.M22 + second.OffsetY);
        }

        /// <summary>
        /// Transforms an axis-aligned local-space rect by <paramref name="matrix"/> and returns the
        /// axis-aligned bounding box of the four transformed corners - needed since an arbitrary
        /// (possibly rotated/skewed) transform doesn't generally preserve axis-alignment. A documented
        /// approximation for a rotated/skewed <c>&lt;a&gt;</c>: PDF link annotations are themselves
        /// always axis-aligned rectangles, so this is the closest any implementation could get anyway.
        /// </summary>
        private static RRect TransformBoundingBox(RRect localBounds, RMatrix matrix)
        {
            var corners = new[]
            {
                ApplyMatrix(new RPoint(localBounds.X, localBounds.Y), matrix),
                ApplyMatrix(new RPoint(localBounds.X + localBounds.Width, localBounds.Y), matrix),
                ApplyMatrix(new RPoint(localBounds.X, localBounds.Y + localBounds.Height), matrix),
                ApplyMatrix(new RPoint(localBounds.X + localBounds.Width, localBounds.Y + localBounds.Height), matrix),
            };

            var minX = corners.Min(c => c.X);
            var maxX = corners.Max(c => c.X);
            var minY = corners.Min(c => c.Y);
            var maxY = corners.Max(c => c.Y);

            return new RRect(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// Computes the viewBox-to-viewport transform per <paramref name="par"/>'s alignment and
        /// meet/slice mode. <c>xMidYMid meet</c> (the SVG/CSS default) is a uniform scale, centered,
        /// letterboxed; other alignments shift which edge/corner touches the viewport instead of
        /// centering; <c>slice</c> uses the larger of the two axis scales (overflowing and relying on
        /// the caller's viewport clip) instead of the smaller; <c>none</c> stretches each axis
        /// independently, ignoring aspect ratio.
        /// </summary>
        private static RMatrix ComputeViewportTransform(RRect viewportRect, double viewBoxX, double viewBoxY, double viewBoxWidth, double viewBoxHeight, SvgPreserveAspectRatio par)
        {
            if (par.Align == SvgAlign.None)
            {
                var sx = viewportRect.Width / viewBoxWidth;
                var sy = viewportRect.Height / viewBoxHeight;
                return new RMatrix(sx, 0, 0, sy, viewportRect.X - viewBoxX * sx, viewportRect.Y - viewBoxY * sy);
            }

            var scale = par.Slice
                ? Math.Max(viewportRect.Width / viewBoxWidth, viewportRect.Height / viewBoxHeight)
                : Math.Min(viewportRect.Width / viewBoxWidth, viewportRect.Height / viewBoxHeight);

            var alignX = par.Align is SvgAlign.XMinYMin or SvgAlign.XMinYMid or SvgAlign.XMinYMax ? 0.0
                : par.Align is SvgAlign.XMaxYMin or SvgAlign.XMaxYMid or SvgAlign.XMaxYMax ? 1.0
                : 0.5;

            var alignY = par.Align is SvgAlign.XMinYMin or SvgAlign.XMidYMin or SvgAlign.XMaxYMin ? 0.0
                : par.Align is SvgAlign.XMinYMax or SvgAlign.XMidYMax or SvgAlign.XMaxYMax ? 1.0
                : 0.5;

            var offsetX = viewportRect.X + (viewportRect.Width - viewBoxWidth * scale) * alignX - viewBoxX * scale;
            var offsetY = viewportRect.Y + (viewportRect.Height - viewBoxHeight * scale) * alignY - viewBoxY * scale;

            return new RMatrix(scale, 0, 0, scale, offsetX, offsetY);
        }

        /// <summary>
        /// Establishes a new nested viewport (for a nested <c>&lt;svg&gt;</c>, or a <c>&lt;symbol&gt;</c>/
        /// nested-<c>&lt;svg&gt;</c> reached through <c>&lt;use&gt;</c>) at local coordinates
        /// (<paramref name="x"/>, <paramref name="y"/>) sized <paramref name="width"/>x<paramref name="height"/>,
        /// then renders <paramref name="children"/> into it - the same viewBox-transform-then-recurse
        /// shape as <see cref="RenderInto"/>, just relative to whatever transform is already active
        /// rather than the page's own initial (identity) transform.
        /// </summary>
        private static void RenderViewport(RGraphics g, SvgDocument document, double x, double y, double width, double height, RRect? viewBox, SvgPreserveAspectRatio par, IReadOnlyList<SvgElement> children, double opacity)
        {
            if (width <= 0 || height <= 0)
                return;

            var viewBoxWidth = viewBox?.Width ?? width;
            var viewBoxHeight = viewBox?.Height ?? height;

            if (viewBoxWidth <= 0 || viewBoxHeight <= 0)
                return;

            var viewBoxX = viewBox?.X ?? 0;
            var viewBoxY = viewBox?.Y ?? 0;
            var viewportRect = new RRect(x, y, width, height);
            var matrix = ComputeViewportTransform(viewportRect, viewBoxX, viewBoxY, viewBoxWidth, viewBoxHeight, par);

            g.PushClip(viewportRect);
            g.PushTransform(matrix);

            var nestedViewport = (viewBoxWidth, viewBoxHeight);
            foreach (var child in children)
                RenderElement(g, document, child, opacity, nestedViewport);

            g.PopTransform();
            g.PopClip();
        }

        private static void RenderElement(RGraphics g, SvgDocument document, SvgElement element, double inheritedOpacity, (double Width, double Height) viewport)
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
                        RenderElement(g, document, child, opacity, viewport);
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

                case SvgPolylineElement polyline:
                {
                    using var graphicsPath = BuildPolylinePath(g, polyline);
                    PaintShape(g, document, polyline, graphicsPath, opacity);
                    break;
                }

                case SvgRectElement rect:
                {
                    using var graphicsPath = BuildRectPath(g, rect);
                    PaintShape(g, document, rect, graphicsPath, opacity);
                    break;
                }

                case SvgEllipseElement ellipse:
                {
                    using var graphicsPath = BuildEllipsePath(g, ellipse);
                    PaintShape(g, document, ellipse, graphicsPath, opacity);
                    break;
                }

                case SvgLineElement line:
                {
                    using var graphicsPath = BuildLinePath(g, line);
                    PaintShape(g, document, line, graphicsPath, opacity);
                    break;
                }

                case SvgNestedSvgElement nestedSvg:
                    RenderViewport(g, document, nestedSvg.X, nestedSvg.Y, nestedSvg.Width, nestedSvg.Height, nestedSvg.ViewBox, nestedSvg.PreserveAspectRatio, nestedSvg.Children, opacity);
                    break;

                case SvgUseElement { Target: { } target } use:
                {
                    var pushedUseOffset = use.X != 0 || use.Y != 0;
                    if (pushedUseOffset)
                        g.PushTransform(new RMatrix(1, 0, 0, 1, use.X, use.Y));

                    switch (target)
                    {
                        // A <symbol> has no size of its own - it's sized entirely by the referencing
                        // <use>'s width/height, defaulting to the current (ambient) viewport's size
                        // when <use> doesn't specify them (spec's 100% default).
                        case SvgSymbolElement symbol:
                            RenderViewport(g, document, 0, 0, use.Width ?? viewport.Width, use.Height ?? viewport.Height, symbol.ViewBox, symbol.PreserveAspectRatio, symbol.Children, opacity);
                            break;

                        // A nested <svg> target already has its own resolved size; <use>'s width/height
                        // only override it when actually specified.
                        case SvgNestedSvgElement nestedTarget:
                            RenderViewport(g, document, 0, 0, use.Width ?? nestedTarget.Width, use.Height ?? nestedTarget.Height, nestedTarget.ViewBox, nestedTarget.PreserveAspectRatio, nestedTarget.Children, opacity);
                            break;

                        default:
                            RenderElement(g, document, target, opacity, viewport);
                            break;
                    }

                    if (pushedUseOffset)
                        g.PopTransform();
                    break;
                }
            }

            if (pushedClip) g.PopClip();
            clipPath?.Dispose();
            if (pushedTransform) g.PopTransform();
        }

        private static void PaintShape(RGraphics g, SvgDocument document, SvgElement element, RGraphicsPath path, double opacity)
        {
            // Per spec, <line> has no interior region - "fill" never applies to it, regardless of the
            // element's own/inherited fill paint (which otherwise defaults to solid black). Emitting a
            // fill op anyway would be visually harmless (PDF implicitly closes an open subpath before
            // filling, and a straight two-point "path" encloses zero area either way), but issuing a
            // real fill call is still wasted content-stream bytes and not what a real SVG renderer does.
            if (element is not SvgLineElement && element.Fill.Kind != SvgPaintKind.None)
            {
                var brush = ResolvePaintBrush(g, document, element, element.Fill, opacity * element.FillOpacity);
                if (brush is not null)
                    g.DrawPath(brush, path);
            }

            if (element.Stroke.Kind != SvgPaintKind.None && element.StrokeWidth > 0)
            {
                var pen = ResolveStrokePen(g, document, element, opacity * element.StrokeOpacity);
                if (pen is not null)
                    g.DrawPath(pen, path);
            }
        }

        private static RBrush? ResolvePaintBrush(RGraphics g, SvgDocument document, SvgElement owner, SvgPaint paint, double opacity)
        {
            return paint.Kind switch
            {
                SvgPaintKind.Solid => g.GetSolidBrush(ApplyOpacity(paint.Color, opacity)),
                SvgPaintKind.GradientRef when paint.GradientId is { } id && document.Gradients.TryGetValue(id, out var gradient)
                    => ResolveGradientBrush(g, owner, gradient, opacity),
                _ => null,
            };
        }

        private static RBrush? ResolveGradientBrush(RGraphics g, SvgElement owner, SvgGradient gradient, double opacity)
        {
            if (gradient.Stops.Count == 0)
                return null;

            var stops = gradient.Stops
                .Select(s => (Color: ApplyOpacity(s.Color, opacity), Position: s.Offset))
                .ToArray();

            var isRepeating = gradient.SpreadMethod != SvgSpreadMethod.Pad;

            switch (gradient)
            {
                case SvgLinearGradient linear:
                {
                    var (x1, y1) = ResolveGradientPoint(owner, gradient, linear.X1, linear.Y1);
                    var (x2, y2) = ResolveGradientPoint(owner, gradient, linear.X2, linear.Y2);
                    var p1 = ApplyMatrix(new RPoint(x1, y1), gradient.GradientTransform);
                    var p2 = ApplyMatrix(new RPoint(x2, y2), gradient.GradientTransform);
                    return g.GetLinearGradientBrush(p1, p2, stops, isRepeating);
                }

                case SvgRadialGradient radial:
                {
                    var (cx, cy) = ResolveGradientPoint(owner, gradient, radial.Cx, radial.Cy);
                    var (fx, fy) = ResolveGradientPoint(owner, gradient, radial.Fx ?? radial.Cx, radial.Fy ?? radial.Cy);
                    var r = ResolveGradientRadius(owner, gradient, radial.R);
                    var center = ApplyMatrix(new RPoint(cx, cy), gradient.GradientTransform);
                    var focal = ApplyMatrix(new RPoint(fx, fy), gradient.GradientTransform);
                    var (radiusX, radiusY) = ApplyMatrixToRadius(r, gradient.GradientTransform);
                    return g.GetRadialGradientBrush(center, radiusX, radiusY, stops, isRepeating, focal);
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// Resolves one gradient coordinate pair. In <c>userSpaceOnUse</c> mode the raw values are
        /// already absolute user-space coordinates; in <c>objectBoundingBox</c> mode (the spec
        /// default) they're 0-1 fractions of <paramref name="owner"/>'s own bounding box, resolved
        /// here since the same gradient definition can be shared by several differently-sized/
        /// positioned shapes via <c>fill:url(#id)</c>. Falls back to treating the fraction as a raw
        /// coordinate if <paramref name="owner"/> has no computable bounding box (e.g. zero-size).
        /// </summary>
        private static (double X, double Y) ResolveGradientPoint(SvgElement owner, SvgGradient gradient, double rawX, double rawY)
        {
            if (gradient.GradientUnitsUserSpaceOnUse)
                return (rawX, rawY);

            if (SvgGeometryBounds.GetBoundingBox(owner) is not { } bbox)
                return (rawX, rawY);

            return (bbox.X + rawX * bbox.Width, bbox.Y + rawY * bbox.Height);
        }

        /// <summary>Same as <see cref="ResolveGradientPoint"/> but for a single scalar radius, scaled by the bounding box's spec-defined diagonal formula.</summary>
        private static double ResolveGradientRadius(SvgElement owner, SvgGradient gradient, double rawR)
        {
            if (gradient.GradientUnitsUserSpaceOnUse)
                return rawR;

            if (SvgGeometryBounds.GetBoundingBox(owner) is not { } bbox)
                return rawR;

            return rawR * Math.Sqrt((bbox.Width * bbox.Width + bbox.Height * bbox.Height) / 2.0);
        }

        private static RPen? ResolveStrokePen(RGraphics g, SvgDocument document, SvgElement element, double opacity)
        {
            RPen pen;

            if (element.Stroke.Kind == SvgPaintKind.Solid)
            {
                pen = g.GetPen(ApplyOpacity(element.Stroke.Color, opacity));
            }
            else if (element.Stroke.Kind == SvgPaintKind.GradientRef &&
                     element.Stroke.GradientId is { } id &&
                     document.Gradients.TryGetValue(id, out var gradient))
            {
                var brush = ResolveGradientBrush(g, element, gradient, opacity);
                if (brush is null)
                    return null;

                pen = g.GetPen(brush);
            }
            else
            {
                return null;
            }

            pen.Width = element.StrokeWidth;
            pen.MiterLimit = element.StrokeMiterLimit;
            pen.LineCap = element.StrokeLineCap;
            pen.LineJoin = element.StrokeLineJoin;
            pen.SetDashPattern(element.StrokeDashArray, element.StrokeDashOffset);
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
            graphicsPath.FillMode = path.FillRule;
            AppendPathSegments(graphicsPath, path.Segments);
            return graphicsPath;
        }

        private static RGraphicsPath BuildCirclePath(RGraphics g, SvgCircleElement circle)
        {
            var graphicsPath = g.GetGraphicsPath();
            graphicsPath.FillMode = circle.FillRule;
            AppendCircleGeometry(graphicsPath, circle);
            return graphicsPath;
        }

        private static RGraphicsPath BuildPolygonPath(RGraphics g, SvgPolygonElement polygon)
        {
            var graphicsPath = g.GetGraphicsPath();
            graphicsPath.FillMode = polygon.FillRule;
            AppendPolygonGeometry(graphicsPath, polygon);
            return graphicsPath;
        }

        private static RGraphicsPath BuildPolylinePath(RGraphics g, SvgPolylineElement polyline)
        {
            var graphicsPath = g.GetGraphicsPath();
            graphicsPath.FillMode = polyline.FillRule;
            AppendPolylineGeometry(graphicsPath, polyline);
            return graphicsPath;
        }

        private static RGraphicsPath BuildRectPath(RGraphics g, SvgRectElement rect)
        {
            var graphicsPath = g.GetGraphicsPath();
            graphicsPath.FillMode = rect.FillRule;
            AppendRectGeometry(graphicsPath, rect);
            return graphicsPath;
        }

        private static RGraphicsPath BuildEllipsePath(RGraphics g, SvgEllipseElement ellipse)
        {
            var graphicsPath = g.GetGraphicsPath();
            graphicsPath.FillMode = ellipse.FillRule;
            AppendEllipseGeometry(graphicsPath, ellipse);
            return graphicsPath;
        }

        private static RGraphicsPath BuildLinePath(RGraphics g, SvgLineElement line)
        {
            var graphicsPath = g.GetGraphicsPath();
            graphicsPath.FillMode = line.FillRule;
            AppendLineGeometry(graphicsPath, line);
            return graphicsPath;
        }

        private static RGraphicsPath? BuildClipPath(RGraphics g, SvgClipPath clipPath)
        {
            var path = g.GetGraphicsPath();
            path.FillMode = clipPath.ClipRule;
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

                case SvgPolylineElement { Points.Length: > 0 } polyline:
                    AppendPolylineGeometry(path, polyline);
                    return true;

                case SvgRectElement { Width: > 0, Height: > 0 } rect:
                    AppendRectGeometry(path, rect);
                    return true;

                case SvgEllipseElement { Rx: > 0, Ry: > 0 } ellipse:
                    AppendEllipseGeometry(path, ellipse);
                    return true;

                case SvgLineElement line:
                    AppendLineGeometry(path, line);
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
            AppendPolylinePoints(path, polygon.Points);
            path.CloseFigure();
        }

        /// <summary>
        /// Unlike <see cref="AppendPolygonGeometry"/>, deliberately does not close the figure - see
        /// <see cref="SvgPolylineElement"/>'s doc comment for the resulting (documented) fill/stroke
        /// simplification.
        /// </summary>
        private static void AppendPolylineGeometry(RGraphicsPath path, SvgPolylineElement polyline) =>
            AppendPolylinePoints(path, polyline.Points);

        private static void AppendPolylinePoints(RGraphicsPath path, RPoint[] points)
        {
            if (points.Length == 0)
                return;

            path.AddMove(points[0].X, points[0].Y);

            for (var i = 1; i < points.Length; i++)
                path.LineTo(points[i].X, points[i].Y);
        }

        /// <summary>
        /// Appends a (possibly corner-rounded) rectangle. <see cref="SvgRectElement.Rx"/>/<see cref="SvgRectElement.Ry"/>
        /// are assumed already defaulted/clamped by <see cref="SvgTreeBuilder.BuildRect"/>. Rounded
        /// corners reuse the same quarter-ellipse-arc technique as <see cref="AppendCircleGeometry"/>.
        /// </summary>
        private static void AppendRectGeometry(RGraphicsPath path, SvgRectElement rect)
        {
            var x = rect.X;
            var y = rect.Y;
            var width = rect.Width;
            var height = rect.Height;

            if (width <= 0 || height <= 0)
                return;

            var rx = rect.Rx;
            var ry = rect.Ry;

            if (rx <= 0 || ry <= 0)
            {
                path.AddMove(x, y);
                path.LineTo(x + width, y);
                path.LineTo(x + width, y + height);
                path.LineTo(x, y + height);
                path.CloseFigure();
                return;
            }

            path.AddMove(x + rx, y);
            path.LineTo(x + width - rx, y);
            path.AddArc(x + width, y + ry, rx, ry, 0, false, true);
            path.LineTo(x + width, y + height - ry);
            path.AddArc(x + width - rx, y + height, rx, ry, 0, false, true);
            path.LineTo(x + rx, y + height);
            path.AddArc(x, y + height - ry, rx, ry, 0, false, true);
            path.LineTo(x, y + ry);
            path.AddArc(x + rx, y, rx, ry, 0, false, true);
            path.CloseFigure();
        }

        /// <summary>Same four-quarter-arc technique as <see cref="AppendCircleGeometry"/>, with independent x/y radii.</summary>
        private static void AppendEllipseGeometry(RGraphicsPath path, SvgEllipseElement ellipse)
        {
            var cx = ellipse.Cx;
            var cy = ellipse.Cy;
            var rx = Math.Abs(ellipse.Rx);
            var ry = Math.Abs(ellipse.Ry);

            if (rx <= 0 || ry <= 0)
                return;

            path.AddMove(cx + rx, cy);
            path.AddArc(cx, cy + ry, rx, ry, 0, false, true);
            path.AddArc(cx - rx, cy, rx, ry, 0, false, true);
            path.AddArc(cx, cy - ry, rx, ry, 0, false, true);
            path.AddArc(cx + rx, cy, rx, ry, 0, false, true);
            path.CloseFigure();
        }

        /// <summary>An open (unclosed) two-point line - fill has no visible effect since it has zero area.</summary>
        private static void AppendLineGeometry(RGraphicsPath path, SvgLineElement line)
        {
            path.AddMove(line.X1, line.Y1);
            path.LineTo(line.X2, line.Y2);
        }
    }
}

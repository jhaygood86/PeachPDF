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

        /// <summary>
        /// Renders an <c>&lt;image&gt;</c> element - either an embedded raster payload (fit into its
        /// own (x, y, width, height) box per its own <c>preserveAspectRatio</c>, same alignment/meet/
        /// slice math as any other viewport) or an embedded <c>image/svg+xml</c> payload (rendered as
        /// its own self-contained <see cref="SvgDocument"/>, so its <c>url(#id)</c> references resolve
        /// against its own gradient/clip/mask/pattern registries, not the host document's). Does
        /// nothing for an unresolved <c>href</c> (see <see cref="SvgImageElement"/>).
        /// </summary>
        private static void RenderImage(RGraphics g, SvgImageElement image, double opacity)
        {
            if (image.Width <= 0 || image.Height <= 0)
                return;

            var viewportRect = new RRect(image.X, image.Y, image.Width, image.Height);

            if (image.NestedDocument is { } nestedDocument)
            {
                var viewBoxWidth = nestedDocument.ViewBox?.Width ?? nestedDocument.Width ?? image.Width;
                var viewBoxHeight = nestedDocument.ViewBox?.Height ?? nestedDocument.Height ?? image.Height;
                if (viewBoxWidth <= 0 || viewBoxHeight <= 0)
                    return;

                var viewBoxX = nestedDocument.ViewBox?.X ?? 0;
                var viewBoxY = nestedDocument.ViewBox?.Y ?? 0;

                // Per spec, the <image> element's own preserveAspectRatio governs how the referenced
                // document is fit into its box - not the referenced document's own root
                // preserveAspectRatio (only relevant when that document is rendered as a top-level
                // viewport in its own right, e.g. via RenderInto).
                var matrix = ComputeViewportTransform(viewportRect, viewBoxX, viewBoxY, viewBoxWidth, viewBoxHeight, image.PreserveAspectRatio);

                g.PushClip(viewportRect);
                g.PushTransform(matrix);

                var nestedViewport = (viewBoxWidth, viewBoxHeight);
                foreach (var child in nestedDocument.Children)
                    RenderElement(g, nestedDocument, child, opacity, nestedViewport);

                g.PopTransform();
                g.PopClip();
            }
            else if (image.Image is { } raster && raster.Width > 0 && raster.Height > 0)
            {
                var matrix = ComputeViewportTransform(viewportRect, 0, 0, raster.Width, raster.Height, image.PreserveAspectRatio);

                g.PushClip(viewportRect);
                g.PushTransform(matrix);
                g.DrawImage(raster, new RRect(0, 0, raster.Width, raster.Height));
                g.PopTransform();
                g.PopClip();
            }
        }

        /// <summary>
        /// Renders one text run (a <c>&lt;text&gt;</c>, <c>&lt;tspan&gt;</c>, or <c>&lt;tref&gt;</c> - see
        /// <see cref="SvgTextElement"/>), then recurses into its <see cref="SvgTextElement.Spans"/>. The
        /// cursor is threaded by reference through siblings so a span without its own <c>x</c>/<c>y</c>
        /// continues immediately after the previous run's rendered width - ordinary SVG text flow,
        /// approximated to whole-run granularity rather than per-glyph (see the type's doc comment).
        /// </summary>
        private static void RenderTextRun(RGraphics g, SvgTextElement run, double inheritedOpacity, ref double cursorX, ref double cursorY)
        {
            var opacity = inheritedOpacity * run.Opacity;

            if (run.HasOwnX) cursorX = run.X;
            if (run.HasOwnY) cursorY = run.Y;

            var x = cursorX + run.Dx;
            var y = cursorY + run.Dy;

            if (run.Text.Length > 0 && run.Font is { } font)
            {
                var size = g.MeasureString(run.Text, font);

                var drawX = run.TextAnchor switch
                {
                    SvgTextAnchor.Middle => x - size.Width / 2,
                    SvgTextAnchor.End => x - size.Width,
                    _ => x,
                };

                // DrawString positions from the top-left of the line box, not the baseline - shift up
                // by the font's ascent so (x, y) lands on the baseline, per SVG's <text> coordinate
                // semantics.
                var drawY = y - font.Ascent;

                var pushedRotation = run.RotateDegrees != 0;
                if (pushedRotation)
                {
                    var radians = run.RotateDegrees * (Math.PI / 180.0);
                    var cos = Math.Cos(radians);
                    var sin = Math.Sin(radians);
                    var toOrigin = new RMatrix(1, 0, 0, 1, -x, -y);
                    var rotate = new RMatrix(cos, sin, -sin, cos, 0, 0);
                    var fromOrigin = new RMatrix(1, 0, 0, 1, x, y);
                    g.PushTransform(MultiplyMatrix(MultiplyMatrix(toOrigin, rotate), fromOrigin));
                }

                PaintTextRun(g, run, font, drawX, drawY, size, opacity);

                if (pushedRotation)
                    g.PopTransform();

                cursorX = x + size.Width;
                cursorY = y;
            }

            foreach (var span in run.Spans)
                RenderTextRun(g, span, opacity, ref cursorX, ref cursorY);
        }

        /// <summary>
        /// Only a solid <see cref="SvgElement.Fill"/> is painted - see <see cref="SvgTextElement"/>'s
        /// doc comment for why gradient/pattern fill and any stroke on text are out of v1 scope.
        /// </summary>
        private static void PaintTextRun(RGraphics g, SvgTextElement run, RFont font, double drawX, double drawY, RSize size, double opacity)
        {
            if (run.Fill.Kind != SvgPaintKind.Solid)
                return;

            var color = ApplyOpacity(run.Fill.Color, opacity * run.FillOpacity);
            g.DrawString(run.Text, font, color, new RPoint(drawX, drawY), size, rtl: false);
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

            if (element.MaskRef is { } maskRef && document.Masks.TryGetValue(maskRef, out var mask))
                RenderMaskedElementContent(g, document, element, mask, opacity, viewport);
            else if (element.Opacity < 1.0 && element is SvgGroupElement or SvgNestedSvgElement)
                // A container's own opacity needs an isolated transparency-group composite - see
                // RenderContainerOpacityGroup - rather than the plain per-shape alpha multiply the
                // "else" branch below uses for everything else, so overlapping children don't
                // double-blend where they overlap. Masked elements are excluded above since masking
                // already produces its own isolated composite via RenderMaskedElementContent.
                RenderContainerOpacityGroup(g, document, element, inheritedOpacity, viewport);
            else
                RenderElementSwitch(g, document, element, opacity, viewport);

            if (pushedClip) g.PopClip();
            clipPath?.Dispose();
            if (pushedTransform) g.PopTransform();
        }

        /// <summary>
        /// Renders a container element's (<c>&lt;g&gt;</c>/<c>&lt;a&gt;</c>/nested <c>&lt;svg&gt;</c>)
        /// children into an offscreen tile at full local alpha, then composites that tile onto
        /// <paramref name="g"/> as a single flattened result at <paramref name="element"/>'s own
        /// <see cref="SvgElement.Opacity"/> - the same isolated-transparency-group technique
        /// <c>CssBox</c> uses for CSS <c>opacity</c> (see <c>CssBox.PaintWithOpacity</c>), applied here
        /// to fix the double-blend limitation this renderer previously had for SVG group opacity.
        /// </summary>
        private static void RenderContainerOpacityGroup(RGraphics g, SvgDocument document, SvgElement element, double inheritedOpacity, (double Width, double Height) viewport)
        {
            // The tile's content is painted in the SAME raw SVG user-space coordinates the normal
            // (non-tiled) path would use, translated to the tile's own local origin - exactly like
            // RenderMaskedElementContent/BuildMaskTile - relying on whatever ambient transform (viewBox
            // scale, ancestor element transforms) is already active on `g` to correctly project both the
            // tile's placement AND its content back onto the page. A copy of `g`'s current transform is
            // NOT pushed onto the tile: unlike CSS `transform` (a self-contained per-box pivot rotation
            // applied once at the very end - see CssBox.PaintWithOpacity), SVG's ambient transform is a
            // true cumulative CTM that every descendant coordinate number is defined relative to, so
            // "paint at raw coordinates, let the same ambient transform re-apply at placement time" is
            // the only way the numbers stay meaningful.
            //
            // The bounding box (with the same -10%/+20% margin SvgMask's own default region uses, as a
            // stroke-width/curve-control-point safety margin) comes from the same SvgGeometryBounds this
            // renderer already uses for objectBoundingBox gradients/masks - an approximation (it doesn't
            // account for descendants' own nested `transform`), acceptable here for the same reason it's
            // acceptable there.
            if (SvgGeometryBounds.GetBoundingBox(element) is not { } bbox || bbox.Width <= 0 || bbox.Height <= 0)
            {
                // No boundable content (e.g. a group of only <text>, or an empty group) - fall back to
                // the older, double-blend-prone but still-translucent per-shape alpha multiply rather
                // than rendering nothing.
                RenderElementSwitch(g, document, element, inheritedOpacity * element.Opacity, viewport);
                return;
            }

            var x = bbox.X - bbox.Width * 0.1;
            var y = bbox.Y - bbox.Height * 0.1;
            var width = bbox.Width * 1.2;
            var height = bbox.Height * 1.2;

            var tile = g.CreateTile(width, height);
            if (tile is not { } t)
            {
                RenderElementSwitch(g, document, element, inheritedOpacity * element.Opacity, viewport);
                return;
            }

            var pushedOffset = x != 0 || y != 0;
            if (pushedOffset)
                t.Graphics.PushTransform(new RMatrix(1, 0, 0, 1, -x, -y));

            RenderElementSwitch(t.Graphics, document, element, inheritedOpacity, viewport);

            if (pushedOffset)
                t.Graphics.PopTransform();

            t.Graphics.Dispose();

            g.DrawImageWithOpacity(t.Image, new RRect(x, y, width, height), element.Opacity);
        }

        /// <summary>
        /// Renders <paramref name="element"/> (which has its own <c>mask="url(#...)"</c>) into a
        /// fresh tile sized to the mask's resolved region, then composites that tile onto the page in
        /// one atomic placement (<see cref="RGraphics.DrawImageMasked"/>) with the mask's own tile
        /// attached - see <see cref="RGraphics.DrawImageMasked"/>'s doc comment for why this (rather
        /// than a simpler-looking "push the mask as ambient state, render normally, pop it" approach)
        /// is required for the mask to land in the same place as the content it's masking.
        /// </summary>
        private static void RenderMaskedElementContent(RGraphics g, SvgDocument document, SvgElement element, SvgMask mask, double opacity, (double Width, double Height) viewport)
        {
            var (x, y, width, height) = ResolveMaskRect(element, mask);
            if (width <= 0 || height <= 0)
                return;

            var contentTile = g.CreateTile(width, height);
            if (contentTile is not { } content)
                return;

            var maskImage = BuildMaskTile(g, document, element, mask);
            if (maskImage is null)
            {
                content.Graphics.Dispose();
                return;
            }

            var pushedOffset = x != 0 || y != 0;
            if (pushedOffset)
                content.Graphics.PushTransform(new RMatrix(1, 0, 0, 1, -x, -y));

            RenderElementSwitch(content.Graphics, document, element, opacity, viewport);

            if (pushedOffset)
                content.Graphics.PopTransform();

            content.Graphics.Dispose();

            g.DrawImageMasked(content.Image, maskImage, new RRect(x, y, width, height));
        }

        private static void RenderElementSwitch(RGraphics g, SvgDocument document, SvgElement element, double opacity, (double Width, double Height) viewport)
        {
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

                case SvgImageElement image:
                    RenderImage(g, image, opacity);
                    break;

                case SvgTextElement text:
                {
                    double cursorX = text.X, cursorY = text.Y;
                    RenderTextRun(g, text, opacity, ref cursorX, ref cursorY);
                    break;
                }

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
        }

        /// <summary>
        /// Renders <paramref name="mask"/>'s content (a full paint, not just geometry - see
        /// <see cref="SvgMask"/>) into a tile sized to its own resolved region, for use as the
        /// luminosity source in <see cref="RGraphics.DrawImageMasked"/>. Unlike <see cref="RenderViewport"/> (used for
        /// <c>&lt;pattern&gt;</c>/<c>&lt;symbol&gt;</c>/nested <c>&lt;svg&gt;</c>), a mask doesn't
        /// establish its own viewBox-scaled coordinate system - its content is drawn in ordinary
        /// user-space units, just positioned relative to the tile's own local origin rather than the
        /// mask region's <see cref="SvgMask.X"/>/<see cref="SvgMask.Y"/>.
        /// </summary>
        private static RImage? BuildMaskTile(RGraphics g, SvgDocument document, SvgElement owner, SvgMask mask)
        {
            var (x, y, width, height) = ResolveMaskRect(owner, mask);
            if (width <= 0 || height <= 0)
                return null;

            var tile = g.CreateTile(width, height);
            if (tile is not { } t)
                return null;

            var pushedOffset = x != 0 || y != 0;
            if (pushedOffset)
                t.Graphics.PushTransform(new RMatrix(1, 0, 0, 1, -x, -y));

            foreach (var child in mask.Children)
                RenderElement(t.Graphics, document, child, 1.0, (width, height));

            if (pushedOffset)
                t.Graphics.PopTransform();

            t.Graphics.Dispose();
            return t.Image;
        }

        /// <summary>Resolves a mask's region, same objectBoundingBox/userSpaceOnUse handling as <see cref="ResolveGradientPoint"/>/<see cref="ResolvePatternRect"/>.</summary>
        private static (double X, double Y, double Width, double Height) ResolveMaskRect(SvgElement owner, SvgMask mask)
        {
            if (mask.MaskUnitsUserSpaceOnUse)
                return (mask.X, mask.Y, mask.Width, mask.Height);

            if (SvgGeometryBounds.GetBoundingBox(owner) is not { } bbox)
                return (mask.X, mask.Y, mask.Width, mask.Height);

            return (bbox.X + mask.X * bbox.Width, bbox.Y + mask.Y * bbox.Height, mask.Width * bbox.Width, mask.Height * bbox.Height);
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
                if (element.Fill.Kind == SvgPaintKind.PatternRef)
                {
                    PaintPatternFill(g, document, element, path, opacity * element.FillOpacity);
                }
                else
                {
                    var brush = ResolvePaintBrush(g, document, element, element.Fill, opacity * element.FillOpacity);
                    if (brush is not null)
                        g.DrawPath(brush, path);
                }
            }

            if (element.Stroke.Kind != SvgPaintKind.None && element.StrokeWidth > 0)
            {
                var pen = ResolveStrokePen(g, document, element, opacity * element.StrokeOpacity);
                if (pen is not null)
                    g.DrawPath(pen, path);
            }

            PaintMarkers(g, document, element, opacity);
        }

        /// <summary>
        /// Per spec, markers only attach to <c>&lt;path&gt;</c>/<c>&lt;line&gt;</c>/<c>&lt;polyline&gt;</c>/
        /// <c>&lt;polygon&gt;</c> - not basic shapes like <c>&lt;rect&gt;</c>/<c>&lt;circle&gt;</c>/
        /// <c>&lt;ellipse&gt;</c>, which have no defined vertex sequence to attach to.
        /// </summary>
        private static void PaintMarkers(RGraphics g, SvgDocument document, SvgElement element, double opacity)
        {
            if (element.MarkerStartRef is null && element.MarkerMidRef is null && element.MarkerEndRef is null)
                return;

            var vertices = element switch
            {
                SvgPathElement path => SvgMarkerGeometry.ComputeForPath(path.Segments),
                SvgLineElement line => SvgMarkerGeometry.ComputeForLine(line.X1, line.Y1, line.X2, line.Y2),
                SvgPolylineElement polyline => SvgMarkerGeometry.ComputeForPoints(polyline.Points, closed: false),
                SvgPolygonElement polygon => SvgMarkerGeometry.ComputeForPoints(polygon.Points, closed: true),
                _ => null,
            };

            if (vertices is null)
                return;

            foreach (var vertex in vertices)
            {
                var markerRef = vertex.IsStart ? element.MarkerStartRef : vertex.IsEnd ? element.MarkerEndRef : element.MarkerMidRef;

                if (markerRef is not null && document.Markers.TryGetValue(markerRef, out var marker))
                    PaintMarker(g, document, marker, vertex, element.StrokeWidth, opacity);
            }
        }

        /// <summary>
        /// Places one marker instance: establishes its own (markerWidth x markerHeight, optionally
        /// scaled by the host shape's stroke-width) viewport, rotated per <see cref="SvgMarkerElement.OrientAuto"/>/
        /// <see cref="SvgMarkerElement.OrientAngle"/> and positioned so (refX, refY) - resolved through
        /// the marker's own viewBox, if any - lands exactly on <paramref name="vertex"/>.
        /// </summary>
        private static void PaintMarker(RGraphics g, SvgDocument document, SvgMarkerElement marker, MarkerVertex vertex, double strokeWidth, double opacity)
        {
            if (marker.MarkerWidth <= 0 || marker.MarkerHeight <= 0)
                return;

            var scale = marker.MarkerUnitsStrokeWidth ? strokeWidth : 1.0;
            if (scale <= 0)
                return;

            var rotation = marker.OrientAuto || marker.OrientAutoStartReverse
                ? vertex.AngleDegrees + (marker.OrientAutoStartReverse && vertex.IsStart ? 180 : 0)
                : marker.OrientAngle;

            // Where does (refX, refY) land within a (markerWidth x markerHeight) viewport anchored at
            // the local origin, per the marker's own viewBox (if any)? That point must become the
            // rotation/scale pivot (i.e. sit exactly at the vertex once placed) - using the same
            // viewport-transform math RenderViewport itself will independently redo below.
            double refLocalX = marker.RefX, refLocalY = marker.RefY;
            var viewBoxWidth = marker.ViewBox?.Width ?? marker.MarkerWidth;
            var viewBoxHeight = marker.ViewBox?.Height ?? marker.MarkerHeight;

            if (viewBoxWidth > 0 && viewBoxHeight > 0)
            {
                var probeMatrix = ComputeViewportTransform(new RRect(0, 0, marker.MarkerWidth, marker.MarkerHeight), marker.ViewBox?.X ?? 0, marker.ViewBox?.Y ?? 0, viewBoxWidth, viewBoxHeight, marker.PreserveAspectRatio);
                var refPoint = ApplyMatrix(new RPoint(marker.RefX, marker.RefY), probeMatrix);
                refLocalX = refPoint.X;
                refLocalY = refPoint.Y;
            }

            var radians = rotation * (Math.PI / 180.0);
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);

            var preShift = new RMatrix(1, 0, 0, 1, -refLocalX, -refLocalY);
            var rotateScale = new RMatrix(cos * scale, sin * scale, -sin * scale, cos * scale, 0, 0);
            var toVertex = new RMatrix(1, 0, 0, 1, vertex.X, vertex.Y);
            var placement = MultiplyMatrix(MultiplyMatrix(preShift, rotateScale), toVertex);

            g.PushTransform(placement);
            RenderViewport(g, document, 0, 0, marker.MarkerWidth, marker.MarkerHeight, marker.ViewBox, marker.PreserveAspectRatio, marker.Children, opacity);
            g.PopTransform();
        }

        private static RBrush? ResolvePaintBrush(RGraphics g, SvgDocument document, SvgElement owner, SvgPaint paint, double opacity)
        {
            return paint.Kind switch
            {
                SvgPaintKind.Solid => g.GetSolidBrush(ApplyOpacity(paint.Color, opacity)),
                SvgPaintKind.GradientRef when paint.ReferenceId is { } id && document.Gradients.TryGetValue(id, out var gradient)
                    => ResolveGradientBrush(g, owner, gradient, opacity),
                _ => null,
            };
        }

        /// <summary>
        /// Fills <paramref name="path"/> with a tiled <c>&lt;pattern&gt;</c>: renders the pattern's own
        /// content once into a small Form XObject "tile" (via <see cref="RGraphics.CreateTile"/>), then
        /// clips to the shape's own fill geometry and draws that SAME tile repeatedly across its
        /// bounding box. Each repeated draw is a reference to the one already-vector tile content, so
        /// this stays fully vector - never rasterizes, matching this renderer's core design principle
        /// - unlike a "render once to a bitmap, then repeat the bitmap" approach would.
        /// </summary>
        private static void PaintPatternFill(RGraphics g, SvgDocument document, SvgElement element, RGraphicsPath path, double opacity)
        {
            if (element.Fill.ReferenceId is not { } id || !document.Patterns.TryGetValue(id, out var pattern))
                return;

            var (x, y, width, height) = ResolvePatternRect(element, pattern);
            if (width <= 0 || height <= 0)
                return;

            var tile = g.CreateTile(width, height);
            if (tile is not { } t)
                return;

            RenderViewport(t.Graphics, document, 0, 0, width, height, pattern.ViewBox, pattern.PreserveAspectRatio, pattern.Children, opacity);
            t.Graphics.Dispose();

            var bounds = SvgGeometryBounds.GetBoundingBox(element) ?? new RRect(x, y, width, height);

            // One tile of margin on every side absorbs any shift introduced by patternTransform below,
            // which the col/row computation itself (deliberately kept simple) doesn't account for -
            // any surplus tiles are clipped away, so this only costs a few harmless extra draw calls.
            var startCol = Math.Floor((bounds.X - x) / width) - 1;
            var endCol = Math.Ceiling((bounds.X + bounds.Width - x) / width) + 1;
            var startRow = Math.Floor((bounds.Y - y) / height) - 1;
            var endRow = Math.Ceiling((bounds.Y + bounds.Height - y) / height) + 1;

            const int maxTiles = 10_000;
            if ((endCol - startCol) * (endRow - startRow) is <= 0 or > maxTiles)
                return;

            g.PushClip(path);

            var pushedPatternTransform = pattern.PatternTransform is not null;
            if (pushedPatternTransform)
                g.PushTransform(pattern.PatternTransform!.Value);

            for (var row = startRow; row < endRow; row++)
            {
                for (var col = startCol; col < endCol; col++)
                {
                    g.DrawImage(t.Image, new RRect(x + col * width, y + row * height, width, height));
                }
            }

            if (pushedPatternTransform)
                g.PopTransform();

            g.PopClip();
        }

        /// <summary>Resolves a pattern's tile rect, same objectBoundingBox/userSpaceOnUse handling as <see cref="ResolveGradientPoint"/>.</summary>
        private static (double X, double Y, double Width, double Height) ResolvePatternRect(SvgElement owner, SvgPattern pattern)
        {
            if (pattern.PatternUnitsUserSpaceOnUse)
                return (pattern.X, pattern.Y, pattern.Width, pattern.Height);

            if (SvgGeometryBounds.GetBoundingBox(owner) is not { } bbox)
                return (pattern.X, pattern.Y, pattern.Width, pattern.Height);

            return (bbox.X + pattern.X * bbox.Width, bbox.Y + pattern.Y * bbox.Height, pattern.Width * bbox.Width, pattern.Height * bbox.Height);
        }

        private static RBrush? ResolveGradientBrush(RGraphics g, SvgElement owner, SvgGradient gradient, double opacity)
        {
            if (gradient.Stops.Count == 0)
                return null;

            var stops = gradient.Stops
                .Select(s => (Color: ApplyOpacity(s.Color, opacity), Position: s.Offset))
                .ToArray();

            var isRepeating = gradient.SpreadMethod != SvgSpreadMethod.Pad;
            var reflect = gradient.SpreadMethod == SvgSpreadMethod.Reflect;

            switch (gradient)
            {
                case SvgLinearGradient linear:
                {
                    var (x1, y1) = ResolveGradientPoint(owner, gradient, linear.X1, linear.Y1);
                    var (x2, y2) = ResolveGradientPoint(owner, gradient, linear.X2, linear.Y2);
                    var p1 = new RPoint(x1, y1);
                    var p2 = new RPoint(x2, y2);

                    // Unlike CSS's repeating-linear-gradient (whose axis is already sized to span the
                    // whole background box before the stop list is ever built), SVG's x1/y1/x2/y2
                    // define just ONE cycle - spreadMethod="repeat"/"reflect" must tile that cycle
                    // outward to actually cover the shape, or (as originally implemented) nothing
                    // paints outside that one short segment at all: IsRepeating only ever toggled the
                    // PDF shading's /Extend to false, with no tiling behind it, so most of a typical
                    // fill silently stayed unpainted.
                    if (isRepeating)
                        (p1, p2, stops) = ExpandLinearSpread(owner, p1, p2, stops, reflect);

                    p1 = ApplyMatrix(p1, gradient.GradientTransform);
                    p2 = ApplyMatrix(p2, gradient.GradientTransform);
                    return g.GetLinearGradientBrush(p1, p2, stops, isRepeating);
                }

                case SvgRadialGradient radial:
                {
                    var (cx, cy) = ResolveGradientPoint(owner, gradient, radial.Cx, radial.Cy);
                    var (fx, fy) = ResolveGradientPoint(owner, gradient, radial.Fx ?? radial.Cx, radial.Fy ?? radial.Cy);
                    var r = ResolveGradientRadius(owner, gradient, radial.R);

                    // Radial counterpart: tiles concentric rings outward from the center to cover the
                    // shape's bounding box, rather than extending along a linear axis.
                    if (isRepeating)
                        (r, stops) = ExpandRadialSpread(owner, new RPoint(cx, cy), r, stops, reflect);

                    var center = ApplyMatrix(new RPoint(cx, cy), gradient.GradientTransform);
                    var focal = ApplyMatrix(new RPoint(fx, fy), gradient.GradientTransform);
                    var (radiusX, radiusY) = ApplyMatrixToRadius(r, gradient.GradientTransform);
                    return g.GetRadialGradientBrush(center, radiusX, radiusY, stops, isRepeating, focal);
                }

                default:
                    return null;
            }
        }

        /// <summary>Safety cap on how many spreadMethod cycles get tiled - see <see cref="ExpandLinearSpread"/>/<see cref="ExpandRadialSpread"/>.</summary>
        private const int MaxSpreadCycles = 500;

        /// <summary>
        /// Extends a linear gradient's axis (<paramref name="p1"/>..<paramref name="p2"/>, one cycle)
        /// to cover <paramref name="owner"/>'s bounding box, and replicates <paramref name="stops"/>
        /// across the extended range - one copy per cycle, each positioned at its own integer offset
        /// along the original gradient direction. For <paramref name="reflect"/>, odd-numbered cycles
        /// use mirrored stop positions (1-position) so adjacent cycle boundaries share a color (no
        /// hard seam); for plain "repeat", every cycle uses the same direction (a seam appears at each
        /// boundary wherever the first/last stop colors differ, which is spec-correct for "repeat").
        /// Falls back to the original (unexpanded) axis/stops if the shape has no computable bounding
        /// box or the axis is degenerate (zero length) - the caller's <c>/Extend=false</c> then simply
        /// paints one cycle and leaves the rest of the shape unpainted, same as before this existed.
        /// </summary>
        private static (RPoint P1, RPoint P2, (RColor Color, double Position)[] Stops) ExpandLinearSpread(
            SvgElement owner, RPoint p1, RPoint p2, (RColor Color, double Position)[] stops, bool reflect)
        {
            if (stops.Length < 2 || SvgGeometryBounds.GetBoundingBox(owner) is not { } bbox)
                return (p1, p2, stops);

            var dx = p2.X - p1.X;
            var dy = p2.Y - p1.Y;
            var len2 = dx * dx + dy * dy;
            if (len2 < 1e-9)
                return (p1, p2, stops);

            var corners = new[]
            {
                new RPoint(bbox.X, bbox.Y),
                new RPoint(bbox.X + bbox.Width, bbox.Y),
                new RPoint(bbox.X, bbox.Y + bbox.Height),
                new RPoint(bbox.X + bbox.Width, bbox.Y + bbox.Height),
            };

            var tMin = double.MaxValue;
            var tMax = double.MinValue;
            foreach (var corner in corners)
            {
                var t = ((corner.X - p1.X) * dx + (corner.Y - p1.Y) * dy) / len2;
                tMin = Math.Min(tMin, t);
                tMax = Math.Max(tMax, t);
            }

            var kMin = (int)Math.Floor(tMin);
            var kMax = (int)Math.Ceiling(tMax);
            if (kMin >= 0 && kMax <= 1)
                return (p1, p2, stops);

            if (kMax - kMin > MaxSpreadCycles)
                kMax = kMin + MaxSpreadCycles;

            var cycles = kMax - kMin;
            var newP1 = new RPoint(p1.X + kMin * dx, p1.Y + kMin * dy);
            var newP2 = new RPoint(p1.X + kMax * dx, p1.Y + kMax * dy);

            var expanded = new List<(RColor Color, double Position)>(stops.Length * cycles);
            for (var k = kMin; k < kMax; k++)
            {
                var reflectedCycle = reflect && PositiveMod(k, 2) != 0;
                foreach (var stop in stops)
                {
                    var localPos = reflectedCycle ? 1 - stop.Position : stop.Position;
                    var newPos = (k - kMin + localPos) / cycles;
                    expanded.Add((stop.Color, Math.Clamp(newPos, 0.0, 1.0)));
                }
            }

            expanded.Sort((a, b) => a.Position.CompareTo(b.Position));
            return (newP1, newP2, expanded.ToArray());
        }

        /// <summary>Radial counterpart of <see cref="ExpandLinearSpread"/> - tiles concentric rings outward from <paramref name="center"/> to cover <paramref name="owner"/>'s bounding box.</summary>
        private static (double R, (RColor Color, double Position)[] Stops) ExpandRadialSpread(
            SvgElement owner, RPoint center, double r, (RColor Color, double Position)[] stops, bool reflect)
        {
            if (stops.Length < 2 || r < 1e-9 || SvgGeometryBounds.GetBoundingBox(owner) is not { } bbox)
                return (r, stops);

            var corners = new[]
            {
                new RPoint(bbox.X, bbox.Y),
                new RPoint(bbox.X + bbox.Width, bbox.Y),
                new RPoint(bbox.X, bbox.Y + bbox.Height),
                new RPoint(bbox.X + bbox.Width, bbox.Y + bbox.Height),
            };

            var maxDist = 0.0;
            foreach (var corner in corners)
            {
                var ddx = corner.X - center.X;
                var ddy = corner.Y - center.Y;
                maxDist = Math.Max(maxDist, Math.Sqrt(ddx * ddx + ddy * ddy));
            }

            var cycles = (int)Math.Ceiling(maxDist / r);
            if (cycles <= 1)
                return (r, stops);

            cycles = Math.Min(cycles, MaxSpreadCycles);

            var newR = r * cycles;
            var expanded = new List<(RColor Color, double Position)>(stops.Length * cycles);
            for (var k = 0; k < cycles; k++)
            {
                var reflectedCycle = reflect && k % 2 != 0;
                foreach (var stop in stops)
                {
                    var localPos = reflectedCycle ? 1 - stop.Position : stop.Position;
                    var newPos = (k + localPos) / cycles;
                    expanded.Add((stop.Color, Math.Clamp(newPos, 0.0, 1.0)));
                }
            }

            expanded.Sort((a, b) => a.Position.CompareTo(b.Position));
            return (newR, expanded.ToArray());
        }

        private static int PositiveMod(int a, int m) => ((a % m) + m) % m;

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
                     element.Stroke.ReferenceId is { } id &&
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
                any |= AppendClipShapeGeometry(g, path, shape, null);

            if (any)
                return path;

            path.Dispose();
            return null;
        }

        /// <summary>
        /// Appends one clip shape's geometry into the combined clip <paramref name="path"/>, baking in
        /// any <c>transform</c> along the way. A clip region is a single union path (not the
        /// graphics-state clip stack, which intersects), so a shape's own <c>transform</c> - and the
        /// <c>translate</c>/<c>transform</c> a wrapping <c>&lt;use&gt;</c>/<c>&lt;g&gt;</c> contribute -
        /// cannot be pushed onto the CTM the way the normal render path does; it must be composed
        /// (<see cref="MultiplyMatrix"/>, innermost first) and applied directly to the shape's points.
        /// When a transform is in effect the shape is built into its own sub-path, transformed, then
        /// merged; the common no-transform case appends straight into <paramref name="path"/> unchanged.
        /// </summary>
        private static bool AppendClipShapeGeometry(RGraphics g, RGraphicsPath path, SvgElement shape, RMatrix? ambient)
        {
            var m = shape.Transform is { } t ? MultiplyMatrix(t, ambient ?? RMatrix.Identity) : ambient;

            switch (shape)
            {
                case SvgPathElement { Segments.Count: > 0 } p:
                    return AppendClipLeaf(g, path, m, sub => AppendPathSegments(sub, p.Segments));

                case SvgCircleElement { R: > 0 } c:
                    return AppendClipLeaf(g, path, m, sub => AppendCircleGeometry(sub, c));

                case SvgPolygonElement { Points.Length: > 0 } poly:
                    return AppendClipLeaf(g, path, m, sub => AppendPolygonGeometry(sub, poly));

                case SvgPolylineElement { Points.Length: > 0 } polyline:
                    return AppendClipLeaf(g, path, m, sub => AppendPolylineGeometry(sub, polyline));

                case SvgRectElement { Width: > 0, Height: > 0 } rect:
                    return AppendClipLeaf(g, path, m, sub => AppendRectGeometry(sub, rect));

                case SvgEllipseElement { Rx: > 0, Ry: > 0 } ellipse:
                    return AppendClipLeaf(g, path, m, sub => AppendEllipseGeometry(sub, ellipse));

                case SvgLineElement line:
                    return AppendClipLeaf(g, path, m, sub => AppendLineGeometry(sub, line));

                case SvgUseElement { Target: { } target } use:
                {
                    // <use> contributes its own transform (already folded into m above) plus its
                    // x/y translation; the target's own transform is folded when it's processed below.
                    var um = use.X != 0 || use.Y != 0
                        ? MultiplyMatrix(new RMatrix(1, 0, 0, 1, use.X, use.Y), m ?? RMatrix.Identity)
                        : m;
                    return AppendClipShapeGeometry(g, path, target, um);
                }

                case SvgGroupElement group:
                {
                    var any = false;
                    foreach (var child in group.Children)
                        any |= AppendClipShapeGeometry(g, path, child, m);
                    return any;
                }

                default:
                    return false;
            }
        }

        /// <summary>
        /// Emits one leaf clip shape's geometry. With no active transform (<paramref name="matrix"/> is
        /// null) the geometry goes straight into the combined <paramref name="path"/> - producing the
        /// same output as the untransformed path, with no extra sub-path allocation. Otherwise the shape
        /// is built into a fresh sub-path, transformed by <paramref name="matrix"/>, and merged as a
        /// disjoint subpath.
        /// </summary>
        private static bool AppendClipLeaf(RGraphics g, RGraphicsPath path, RMatrix? matrix, Action<RGraphicsPath> build)
        {
            if (matrix is not { } m)
            {
                build(path);
                return true;
            }

            var sub = g.GetGraphicsPath();
            build(sub);
            sub.Transform(m);
            path.AddPath(sub);
            sub.Dispose();
            return true;
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

using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Paint
{
    /// <summary>
    /// The box-decoration half of the painter: backgrounds, <c>box-shadow</c>, text decoration,
    /// multi-column rules and generated content images. Every method here is static and takes the box
    /// whose computed style it reads — decorations depend on style plus a rectangle, never on which
    /// page is being painted.
    /// </summary>
    internal sealed partial class FragmentPainter
    {
        /// <summary>
        /// Paints <paramref name="box"/>'s own background at <paramref name="rect"/> instead of the
        /// box's own laid-out rect - used by <c>PdfGenerator.AddPdfPages</c> to fill the whole page
        /// canvas with the <c>&lt;body&gt;</c>/<c>&lt;html&gt;</c> background per CSS2.1 §14.2, reusing
        /// the exact same background-image/-position/-size/-repeat/-origin/-clip resolution the box's
        /// own normal paint path uses so canvas-fill behavior matches per-box behavior exactly.
        /// </summary>
        internal static void PaintCanvasBackground(RGraphics g, CssBox box, RRect rect) =>
            PaintBackground(g, box, BoxDecorationGeometry.Unbroken(rect));

        /// <summary>
        /// Paints the background of a box.
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="box">the box whose background style is painted</param>
        /// <param name="geometry">
        /// where this background resolves, per <c>box-decoration-break</c> — see
        /// <see cref="BoxDecorationGeometry"/>. The caller pushes
        /// <see cref="BoxDecorationGeometry.ClipRect"/> when the geometry says so; this method paints the
        /// whole of <see cref="BoxDecorationGeometry.DecorationRect"/> and lets that clip cut it.
        /// </param>
        /// <param name="firstLineStyle">
        /// When set, this rect is on the target's first formatted line under a <c>::first-line</c>
        /// rule - its resolved <c>background-color</c> is used instead of the box's own. Only
        /// <c>background-color</c> is first-line-aware, not <c>background-image</c>/-position/-size/
        /// -repeat/-origin/-clip layers (a documented narrowing - see docs/html-css-support.md);
        /// those, like border/padding/border-radius (genuine box-model properties CSS2.1 never allows
        /// on <c>::first-line</c> at all), always come from the box's own resolved style.
        /// </param>
        internal static void PaintBackground(RGraphics g, CssBox box, in BoxDecorationGeometry geometry, CssBox? firstLineStyle = null)
        {
            var rect = geometry.DecorationRect;

            if (rect is not { Width: > 0, Height: > 0 }) return;

            RRect BoxModelRect(string value) => value switch
            {
                Keywords.BorderBox => rect,
                Keywords.ContentBox => new RRect(
                    rect.X + box.ActualBorderLeftWidth + box.ActualPaddingLeft,
                    rect.Y + box.ActualBorderTopWidth  + box.ActualPaddingTop,
                    rect.Width  - box.ActualBorderLeftWidth - box.ActualBorderRightWidth  - box.ActualPaddingLeft - box.ActualPaddingRight,
                    rect.Height - box.ActualBorderTopWidth  - box.ActualBorderBottomWidth - box.ActualPaddingTop  - box.ActualPaddingBottom),
                _ => new RRect(
                    rect.X + box.ActualBorderLeftWidth,
                    rect.Y + box.ActualBorderTopWidth,
                    rect.Width  - box.ActualBorderLeftWidth - box.ActualBorderRightWidth,
                    rect.Height - box.ActualBorderTopWidth  - box.ActualBorderBottomWidth),
            };

            // background-origin/background-clip are themselves comma-list (per-layer) properties,
            // just like background-image/-position/-size - resolved per layer inside the loop below,
            // not once for the whole box.
            var originLayers = BackgroundLayerResolver.SplitLayers(box.BackgroundOrigin);
            var clipLayers   = BackgroundLayerResolver.SplitLayers(box.BackgroundClip);
            var viewportRect = box.HtmlContainer!.PageBoxRect;

            var actualBackgroundColor = firstLineStyle?.ActualBackgroundColor ?? box.ActualBackgroundColor;
            RBrush? solidBrush = RenderUtils.IsColorVisible(actualBackgroundColor)
                ? g.GetSolidBrush(actualBackgroundColor)
                : null;

            // background-color is always the bottom-most layer (CSS Backgrounds 3 §3.6/§3.8) -
            // painted BEFORE any background-image/gradient layer so those layers appear on top of
            // it, not hidden beneath an opaque color.
            if (solidBrush != null)
            {
                // background-color is not itself a layered property: when background-clip has
                // multiple values, the solid fill uses the LAST (bottom-most) one, independent of
                // how many background-image layers exist - including zero.
                var colorClipRect = BoxModelRect(clipLayers[^1]);

                RGraphicsPath? colorRoundedClipPath = null;
                if (box.IsRounded)
                {
                    var radii = box.ComputeRadii(colorClipRect);
                    colorRoundedClipPath = RenderUtils.GetRoundRect(g, colorClipRect,
                        radii.TLX, radii.TLY, radii.TRX, radii.TRY,
                        radii.BRX, radii.BRY, radii.BLX, radii.BLY);
                }

                PaintClippedBrush(g, box, solidBrush, colorClipRect, colorRoundedClipPath);
                colorRoundedClipPath?.Dispose();
            }

            // Then paint image/gradient layers back-to-front (last in the comma-list = bottom-most
            // image layer, but still on top of the solid color) so the first-declared layer ends up
            // visually on top.
            var layersToPaint = box.BackgroundImages != null
                ? Enumerable.Range(0, box.BackgroundImages.Count).Reverse()
                : Enumerable.Empty<int>();

            foreach (int layerIndex in layersToPaint)
            {
                var originRect = BoxModelRect(BackgroundLayerResolver.LayerAt(originLayers, layerIndex));
                var clipRect   = BoxModelRect(BackgroundLayerResolver.LayerAt(clipLayers, layerIndex));

                RGraphicsPath? roundedClipPath = null;
                if (box.IsRounded)
                {
                    var radii = box.ComputeRadii(clipRect);
                    roundedClipPath = RenderUtils.GetRoundRect(g, clipRect,
                        radii.TLX, radii.TLY, radii.TRX, radii.TRY,
                        radii.BRX, radii.BRY, radii.BLX, radii.BLY);
                }

                void DrawBrush(RBrush brush) => PaintClippedBrush(g, box, brush, clipRect, roundedClipPath);

                CssImagePainter.Paint(g, box.BackgroundImages![layerIndex], layerIndex, originRect, clipRect,
                    roundedClipPath, box.BackgroundPosition, box.BackgroundSize, box.BackgroundRepeat, box.BackgroundAttachment,
                    viewportRect, box, DrawBrush);

                roundedClipPath?.Dispose();
            }
        }

        /// <summary>
        /// Fills <paramref name="brush"/> clipped to <paramref name="roundedClipPath"/> (border-radius
        /// case) or <paramref name="clipRect"/> (rectangular case), and disposes the brush afterward.
        /// Shared by every per-layer background-image/gradient draw and the final solid-color fill.
        /// </summary>
        private static void PaintClippedBrush(RGraphics g, CssBox box, RBrush brush, RRect clipRect, RGraphicsPath? roundedClipPath)
        {
            // TODO:a handle it correctly (tables background)
            object? prevMode = null;
            if (box.HtmlContainer is { AvoidGeometryAntialias: false } && box.IsRounded)
                prevMode = g.SetAntiAliasSmoothingMode();

            if (roundedClipPath != null)
                g.DrawPath(brush, roundedClipPath);
            else
                g.DrawRectangle(brush, clipRect.X, clipRect.Y, clipRect.Width, clipRect.Height);

            g.ReturnPreviousSmoothingMode(prevMode);
            brush.Dispose();
        }

        /// <summary>
        /// Paints a box's <c>box-shadow</c> layers of one kind (outset or inset) for a single border-box
        /// fragment (CSS Backgrounds &amp; Borders Level 3 §5). PDF has no native blur, so a blurred shadow is
        /// approximated with vector geometry: a stack of concentric, overlapping rounded-rect fills each at a
        /// small constant alpha, so their source-over accumulation ramps the shadow color's alpha from zero at
        /// the outer blur edge to the full color where every layer overlaps (the interior). Because the layers
        /// overlap rather than abut, there are no partial-alpha shared edges to double-blend into seam lines,
        /// and the corners round off automatically over the blur radius. Layers paint last-listed first so the
        /// first-declared shadow ends up on top.
        /// <para>
        /// Under <c>box-decoration-break: slice</c> the shadow belongs to the unbroken box and "no
        /// box-shadow is drawn at a broken edge" (css-break-3 §6.2), so the shape is built over
        /// <see cref="BoxDecorationGeometry.DecorationRect"/> and cut at the break edges only. It is
        /// deliberately <b>not</b> clipped to this fragment's own rectangle the way the background is: a
        /// shadow legitimately falls outside the box it belongs to, so clipping it there would erase it.
        /// </para>
        /// </summary>
        private static void PaintBoxShadows(RGraphics g, CssBox box, in BoxDecorationGeometry geometry, bool inset)
        {
            var layers = BoxShadowGrammar.TryParse(CssValueParser.GetCssTokens(box.BoxShadow));
            if (layers is null || layers.Count == 0) return;

            // Both kinds are painted in separate passes over the same layer list, so a box carrying only the
            // other kind has nothing to do here - and must not push a clip around it.
            var paints = false;
            foreach (var layer in layers)
            {
                if (layer.Inset == inset) { paints = true; break; }
            }

            if (!paints) return;

            var borderBox = geometry.DecorationRect;
            var sliced = PushBreakEdgeClip(g, box, geometry, layers);

            for (var i = layers.Count - 1; i >= 0; i--)
            {
                var layer = layers[i];
                if (layer.Inset != inset) continue;

                var dx = CssValueParser.ParseLength(layer.OffsetX, 0, box);
                var dy = CssValueParser.ParseLength(layer.OffsetY, 0, box);
                var blur = CssValueParser.ParseLength(layer.Blur, 0, box);
                var spread = CssValueParser.ParseLength(layer.Spread, 0, box);
                var color = ResolveShadowColor(box, layer.Color);

                if (inset)
                    PaintInsetShadow(g, box, borderBox, dx, dy, blur, spread, color);
                else
                    PaintOutsetShadow(g, box, borderBox, dx, dy, blur, spread, color);
            }

            if (sliced)
                g.PopClip();
        }

        /// <summary>
        /// Confines a sliced shadow to this fragment, cutting it at the fragmentation breaks and nowhere
        /// else: an edge the box really owns keeps the room the shadow needs to spill into, an edge that is
        /// a break stops exactly at the fragment. Returns whether a clip was pushed.
        /// </summary>
        /// <remarks>
        /// The room to spill is measured from the shadow layers themselves rather than taken from the
        /// current clip: <c>RGraphics.GetClip</c> reports the last rectangle pushed, which for an
        /// unclipped surface is unbounded, and inflating that produces meaningless coordinates.
        /// </remarks>
        private static bool PushBreakEdgeClip(
            RGraphics g, CssBox box, in BoxDecorationGeometry geometry, IReadOnlyList<BoxShadowGrammar.ShadowLayer> layers)
        {
            if (geometry is { HasLeftEdge: true, HasRightEdge: true, HasTopEdge: true, HasBottomEdge: true })
                return false;

            var bleed = 0d;

            foreach (var layer in layers)
            {
                var dx = Math.Abs(CssValueParser.ParseLength(layer.OffsetX, 0, box));
                var dy = Math.Abs(CssValueParser.ParseLength(layer.OffsetY, 0, box));
                var blur = CssValueParser.ParseLength(layer.Blur, 0, box);
                var spread = CssValueParser.ParseLength(layer.Spread, 0, box);

                bleed = Math.Max(bleed, Math.Max(dx, dy) + Math.Max(0, blur) + Math.Max(0, spread));
            }

            var shape = geometry.DecorationRect;

            g.PushClip(RRect.FromLTRB(
                geometry.HasLeftEdge ? shape.Left - bleed : geometry.ClipRect.Left,
                geometry.HasTopEdge ? shape.Top - bleed : geometry.ClipRect.Top,
                geometry.HasRightEdge ? shape.Right + bleed : geometry.ClipRect.Right,
                geometry.HasBottomEdge ? shape.Bottom + bleed : geometry.ClipRect.Bottom));

            return true;
        }

        /// <summary>Resolves a shadow layer's authored color string to an <see cref="RColor"/>; a null/omitted
        /// or <c>currentColor</c> value uses the element's own text color (CSS Backgrounds 3 §5).</summary>
        private static RColor ResolveShadowColor(CssBox box, string? color) =>
            string.IsNullOrEmpty(color) || color.Equals(Keywords.CurrentColor, StringComparison.OrdinalIgnoreCase)
                ? box.ActualColor
                : box.HtmlContainer!.CssParser.ParseColor(color);

        private static void PaintOutsetShadow(RGraphics g, CssBox box, RRect borderBox, double dx, double dy, double blur, double spread, RColor color)
        {
            // The shadow shape is the border box, translated by the offset and expanded by spread on all
            // sides (a negative spread shrinks it).
            var shadowRect = new RRect(
                borderBox.X + dx - spread,
                borderBox.Y + dy - spread,
                borderBox.Width + 2 * spread,
                borderBox.Height + 2 * spread);

            if (shadowRect.Width <= 0 || shadowRect.Height <= 0) return;

            // The shadow shape's own corner radii: the box's border radius grown by the spread (a sharp,
            // zero-radius corner stays sharp).
            var baseRadii = ShadowCornerRadii(box, borderBox, spread);

            if (blur <= 0)
            {
                var brush = g.GetSolidBrush(color);
                if (baseRadii.IsRounded)
                {
                    var path = BuildLayerRoundRect(g, shadowRect, baseRadii, 0);
                    g.DrawPath(brush, path);
                    path.Dispose();
                }
                else
                {
                    g.DrawRectangle(brush, shadowRect.X, shadowRect.Y, shadowRect.Width, shadowRect.Height);
                }
                brush.Dispose();
                return;
            }

            var steps = BlurSteps(blur);
            var layerColors = ComputeBlurLayerColors(color, steps);
            if (layerColors.Length == 0) return;

            // Draw outermost (largest) to innermost. A point at signed outward distance x is covered by
            // layers 0..K(x); painting them in this order accumulates to the target alpha ramp for that point.
            for (var k = 0; k < steps; k++)
            {
                var d = blur - 2 * blur * k / (steps - 1); // +blur (outer) .. -blur (inner)
                var rect = new RRect(shadowRect.X - d, shadowRect.Y - d, shadowRect.Width + 2 * d, shadowRect.Height + 2 * d);
                if (rect.Width <= 0 || rect.Height <= 0) continue;

                var brush = g.GetSolidBrush(layerColors[k]);
                var path = BuildLayerRoundRect(g, rect, baseRadii, d);
                g.DrawPath(brush, path);
                path.Dispose();
                brush.Dispose();
            }
        }

        private static void PaintInsetShadow(RGraphics g, CssBox box, RRect borderBox, double dx, double dy, double blur, double spread, RColor color)
        {
            var paddingBox = new RRect(
                borderBox.X + box.ActualBorderLeftWidth,
                borderBox.Y + box.ActualBorderTopWidth,
                borderBox.Width - box.ActualBorderLeftWidth - box.ActualBorderRightWidth,
                borderBox.Height - box.ActualBorderTopWidth - box.ActualBorderBottomWidth);

            if (paddingBox.Width <= 0 || paddingBox.Height <= 0) return;

            // The lit "hole" = the padding box, translated by the offset and shrunk by spread. The shadow is
            // the inverse (the region between the padding box and this inner shape), clipped to the padding box.
            var inner = new RRect(
                paddingBox.X + dx + spread,
                paddingBox.Y + dy + spread,
                Math.Max(0, paddingBox.Width - 2 * spread),
                Math.Max(0, paddingBox.Height - 2 * spread));

            RGraphicsPath? clipPath = null;
            if (box.IsRounded)
            {
                clipPath = BuildLayerRoundRect(g, paddingBox, ShadowCornerRadii(box, borderBox, spread: 0), 0);
                g.PushClip(clipPath);
            }
            else
            {
                g.PushClip(paddingBox);
            }

            if (blur <= 0)
            {
                // Solid ring = padding box minus the inner hole.
                FillRingRects(g, paddingBox, inner, color);
            }
            else
            {
                var steps = BlurSteps(blur);
                var layerColors = ComputeBlurLayerColors(color, steps);

                // Each layer is the padding box with a rectangular hole punched out (via an even-odd fill,
                // since the PDF backend has no clip-subtract primitive). The hole grows from
                // inner-deflated-by-blur (drawn first) to inner-inflated-by-blur, so a point out toward the
                // padding edge is covered by every layer (full color) and one deep in the hole by none
                // (transparent) - the inset falloff, fading inward over the blur radius. The already-pushed
                // padding-box clip trims each ring to the (possibly rounded) padding box.
                for (var k = 0; k < layerColors.Length; k++)
                {
                    var d = -blur + 2 * blur * k / (steps - 1);
                    var hole = new RRect(inner.X - d, inner.Y - d, inner.Width + 2 * d, inner.Height + 2 * d);
                    var brush = g.GetSolidBrush(layerColors[k]);
                    var ring = BuildRingPath(g, paddingBox, hole);
                    g.DrawPath(brush, ring);
                    ring.Dispose();
                    brush.Dispose();
                }
            }

            g.PopClip();
            clipPath?.Dispose();
        }

        /// <summary>The shadow shape's per-corner radii: the box's <c>border-radius</c> grown by
        /// <paramref name="spread"/> where non-zero, with sharp corners staying sharp.</summary>
        private static BorderRadii ShadowCornerRadii(CssBox box, RRect borderBox, double spread)
        {
            var r = box.ComputeRadii(borderBox);
            double Adj(double v) => v > 0 ? Math.Max(0, v + spread) : 0;
            return new BorderRadii(Adj(r.TLX), Adj(r.TLY), Adj(r.TRX), Adj(r.TRY),
                                   Adj(r.BRX), Adj(r.BRY), Adj(r.BLX), Adj(r.BLY));
        }

        /// <summary>Number of concentric fills used to approximate a blur of the given radius (in points).</summary>
        private static int BlurSteps(double blur) => Math.Clamp((int)Math.Round(blur * 2), 6, 40);

        /// <summary>
        /// Per-layer colors for the <paramref name="steps"/> concentric blur fills (index 0 = outermost,
        /// faintest). The alpha of each layer is chosen so that the running source-over accumulation of
        /// layers 0..k reaches a linear target ramp - <c>alpha[k] = 1 - (1 - T_k)/(1 - T_{k-1})</c> with
        /// <c>T_k = Amax·(k+1)/steps</c> - so the interior reaches the shadow color's <b>full</b> alpha even
        /// when it is fully opaque (a constant per-layer alpha would collapse an opaque color to a hard edge).
        /// Empty (paints nothing) for a fully-transparent shadow color.
        /// </summary>
        private static RColor[] ComputeBlurLayerColors(RColor color, int steps)
        {
            var amax = color.A / 255.0;
            if (amax <= 0) return [];

            var colors = new RColor[steps];
            var prevTarget = 0.0;

            for (var k = 0; k < steps; k++)
            {
                var target = amax * (k + 1) / steps;
                var a = 1 - (1 - target) / (1 - prevTarget);
                prevTarget = target;

                var alpha = Math.Clamp((int)Math.Round(a * 255), 1, 255);
                colors[k] = RColor.FromArgb(alpha, color.R, color.G, color.B);
            }

            return colors;
        }

        /// <summary>
        /// Builds a rounded-rect path for one concentric shadow layer at signed outward distance
        /// <paramref name="d"/> from the shadow's sharp edge. Each corner's radius is the base (sharp-shape)
        /// radius plus <paramref name="d"/>, clamped non-negative - so outer layers of even a square box round
        /// off over the blur radius, matching how a real blurred shadow's corners soften.
        /// </summary>
        private static RGraphicsPath BuildLayerRoundRect(RGraphics g, RRect rect, BorderRadii baseRadii, double d)
        {
            double R(double b) => Math.Max(0, b + d);
            return RenderUtils.GetRoundRect(g, rect,
                R(baseRadii.TLX), R(baseRadii.TLY), R(baseRadii.TRX), R(baseRadii.TRY),
                R(baseRadii.BRX), R(baseRadii.BRY), R(baseRadii.BLX), R(baseRadii.BLY));
        }

        /// <summary>Fills the region of <paramref name="outer"/> that lies outside <paramref name="hole"/>
        /// with a solid color, as four axis-aligned rectangles (the hole is clamped to the outer bounds).</summary>
        private static void FillRingRects(RGraphics g, RRect outer, RRect hole, RColor color)
        {
            var hl = Math.Max(outer.Left, hole.Left);
            var ht = Math.Max(outer.Top, hole.Top);
            var hr = Math.Min(outer.Right, hole.Right);
            var hb = Math.Min(outer.Bottom, hole.Bottom);
            if (hr < hl) hr = hl;
            if (hb < ht) hb = ht;

            var brush = g.GetSolidBrush(color);
            FillRect(g, brush, outer.Left, outer.Top, outer.Width, ht - outer.Top);          // top
            FillRect(g, brush, outer.Left, hb, outer.Width, outer.Bottom - hb);              // bottom
            FillRect(g, brush, outer.Left, ht, hl - outer.Left, hb - ht);                    // left
            FillRect(g, brush, hr, ht, outer.Right - hr, hb - ht);                           // right
            brush.Dispose();
        }

        private static void FillRect(RGraphics g, RBrush brush, double x, double y, double width, double height)
        {
            if (width > 0 && height > 0)
                g.DrawRectangle(brush, x, y, width, height);
        }

        /// <summary>Builds an even-odd fill path of <paramref name="outer"/> with a rectangular
        /// <paramref name="hole"/> punched out - a filled ring. Used for inset-shadow falloff layers, since the
        /// PDF backend has no clip-subtract primitive.</summary>
        private static RGraphicsPath BuildRingPath(RGraphics g, RRect outer, RRect hole)
        {
            var path = g.GetGraphicsPath();

            path.Start(outer.Left, outer.Top);
            path.LineTo(outer.Right, outer.Top);
            path.LineTo(outer.Right, outer.Bottom);
            path.LineTo(outer.Left, outer.Bottom);
            path.CloseFigure();

            path.AddMove(hole.Left, hole.Top);
            path.LineTo(hole.Right, hole.Top);
            path.LineTo(hole.Right, hole.Bottom);
            path.LineTo(hole.Left, hole.Bottom);
            path.CloseFigure();

            path.FillMode = RFillMode.EvenOdd;
            return path;
        }

        /// <summary>
        /// Paints the text decoration (underline/strike-through/over-line)
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="box">the box whose decoration style is painted</param>
        /// <param name="rectangle">the decoration rectangle</param>
        /// <param name="hasLeftEdge">
        /// whether the box's leading padding and border belong to this rectangle, and so inset the
        /// decoration's start. False at a fragmentation break under <c>box-decoration-break: slice</c>, where
        /// no padding is inserted (css-break-3 §6.2), and always true under <c>clone</c>.
        /// </param>
        /// <param name="hasRightEdge">whether the box's trailing padding and border belong to this rectangle</param>
        /// <param name="firstLineStyle">
        /// When set, this rectangle is on the target's first formatted line under a <c>::first-line</c>
        /// rule - its resolved text-decoration/color/font (for underline-offset) are used instead of
        /// the box's own.
        /// </param>
        private static void PaintDecoration(RGraphics g, CssBox box, RRect rectangle, bool hasLeftEdge, bool hasRightEdge, CssBox? firstLineStyle = null)
        {
            // The `text-decoration` shorthand is expanded into these longhands by the CSS-OM (Layer A) before
            // it ever reaches the box, so the painter reads the longhands directly. text-decoration-line may
            // carry several space-separated line keywords (e.g. "underline overline"); each is drawn.
            var styleSource = firstLineStyle ?? box;
            var textDecorationLine = styleSource.TextDecorationLine;
            var textDecorationStyle = styleSource.TextDecorationStyle;
            var textDecorationColor = styleSource.TextDecorationColor;

            if (string.IsNullOrEmpty(textDecorationLine) || textDecorationLine == Keywords.None)
                return;

            if (string.IsNullOrEmpty(textDecorationStyle))
            {
                textDecorationStyle = Keywords.Solid;
            }

            if (!string.IsNullOrEmpty(textDecorationColor) && !box.HtmlContainer!.CssParser.IsColorValid(textDecorationColor))
            {
                textDecorationColor = string.Empty;
            }

            var textDecorationActualColor = string.IsNullOrEmpty(textDecorationColor) ? styleSource.ActualColor : box.HtmlContainer!.CssParser.ParseColor(textDecorationColor);

            double x1 = rectangle.X;
            if (hasLeftEdge)
                x1 += box.ActualPaddingLeft + box.ActualBorderLeftWidth;

            double x2 = rectangle.Right;
            if (hasRightEdge)
                x2 -= box.ActualPaddingRight + box.ActualBorderRightWidth;

            var dashStyle = textDecorationStyle switch
            {
                Keywords.Solid => RDashStyle.Solid,
                Keywords.Double => RDashStyle.Solid,
                Keywords.Dotted => RDashStyle.Dot,
                Keywords.Dashed => RDashStyle.Dash,
                Keywords.Wavy => RDashStyle.Solid,
                _ => RDashStyle.Solid
            };

            var pen = g.GetPen(textDecorationActualColor);
            pen.Width = 1;
            pen.DashStyle = dashStyle;

            // text-decoration-line may list several keywords (e.g. "underline overline"); draw each.
            var bottomInset = box.ActualPaddingBottom - box.ActualBorderBottomWidth;
            foreach (var line in textDecorationLine.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                double y = line switch
                {
                    Keywords.Underline => Math.Round(rectangle.Top + styleSource.ActualFont.UnderlineOffset),
                    Keywords.LineThrough => rectangle.Top + rectangle.Height / 2f,
                    Keywords.Overline => rectangle.Top,
                    _ => double.NaN
                };

                if (double.IsNaN(y)) continue;

                y -= bottomInset;
                g.DrawLine(pen, x1, y, x2, y);
            }
        }

        /// <summary>
        /// Draws the vertical rule lines between columns of a multi-column container, one segment per
        /// gap per page-row (see <see cref="CssBox.ColumnRuleSegments"/>).
        /// </summary>
        private static void PaintColumnRules(RGraphics g, CssBox box, double originY, RRect clip)
        {
            var pen = g.GetPen(box.ActualColumnRuleColor);
            pen.Width = box.ActualColumnRuleWidth;
            pen.DashStyle = box.ColumnRuleStyle.Value switch
            {
                LineStyle.Dashed => RDashStyle.Dash,
                LineStyle.Dotted => RDashStyle.Dot,
                _ => RDashStyle.Solid,
            };

            // Column rules are recorded by the columns engine in document space, so they need the same
            // origin shift the builder already applied to every other rectangle on this page.
            foreach (var (x, top, bottom) in box.ColumnRuleSegments!)
            {
                var visualX = x;
                var visualTop = top - originY;
                var visualBottom = bottom - originY;

                if (!IsRectVisible(new RRect(visualX - 1, visualTop, 2, visualBottom - visualTop), clip)) continue;

                g.DrawLine(pen, visualX, visualTop, visualX, visualBottom);
            }
        }

        /// <summary>
        /// Draws a box's resolved <c>content: url(...)</c> image, once per decoration rectangle.
        /// </summary>
        private static void PaintContentImage(RGraphics g, CssBox box, BoxFragment fragment)
        {
            if (box.ContentImage == null) return;

            foreach (var line in fragment.Lines)
            {
                var rect = line.Rect;
                if (rect.Width <= 0 || rect.Height <= 0) continue;
                CssImagePainter.Paint(g, box.ContentImage, layerIndex: 0,
                    originRect: rect, clipRect: rect, roundedClipPath: null,
                    positionList: "0% 0%", sizeList: Keywords.Auto, repeatList: "no-repeat",
                    attachmentList: Keywords.Scroll, viewportRect: rect, box: box,
                    drawBrush: brush =>
                    {
                        g.DrawRectangle(brush, rect.X, rect.Y, rect.Width, rect.Height);
                        brush.Dispose();
                    });
            }
        }
    }
}

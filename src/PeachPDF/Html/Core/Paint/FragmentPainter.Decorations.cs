using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Text;
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
        /// <param name="fragment">
        /// this box's own fragment, when one is in scope - the source of the laid-out words a
        /// <c>background-clip: text</c> layer clips to (issue #1117; see <see cref="BuildTextClipPath"/>).
        /// Null for the three call sites with no word content of their own to clip to (the page-canvas
        /// background, a replaced element's chrome, a form field's chrome) - <c>text</c> degrades to
        /// <c>border-box</c> there, same as an unsupported font/writing-mode does below.
        /// </param>
        internal static void PaintBackground(RGraphics g, CssBox box, in BoxDecorationGeometry geometry, CssBox? firstLineStyle = null, BoxFragment? fragment = null)
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

            // The clip rectangle's own curve - border-box uses the box's declared radius as-is, while
            // padding-box/content-box reduce it by the border width (and, for content-box, the padding
            // too) per CSS Backgrounds and Borders Level 3 §5.5 before clipping the smaller rectangle.
            BorderRadii ClipRadii(string clipValue, RRect clipRect) => clipValue switch
            {
                Keywords.BorderBox => box.ComputeRadii(clipRect),
                Keywords.ContentBox => box.ComputeInnerRadii(rect, clipRect,
                    box.ActualBorderLeftWidth + box.ActualPaddingLeft,
                    box.ActualBorderTopWidth + box.ActualPaddingTop,
                    box.ActualBorderRightWidth + box.ActualPaddingRight,
                    box.ActualBorderBottomWidth + box.ActualPaddingBottom),
                _ => box.ComputeInnerRadii(rect, clipRect,
                    box.ActualBorderLeftWidth, box.ActualBorderTopWidth,
                    box.ActualBorderRightWidth, box.ActualBorderBottomWidth),
            };

            // background-origin/background-clip are themselves comma-list (per-layer) properties,
            // just like background-image/-position/-size - resolved per layer inside the loop below,
            // not once for the whole box.
            var originLayers = BackgroundLayerResolver.SplitLayers(box.BackgroundOrigin);
            var clipLayers   = BackgroundLayerResolver.SplitLayers(box.BackgroundClip);
            // background-attachment:fixed's positioning area is this page's own window, not the base
            // page box - PageClipOverride is set per-slot by PdfGenerator.AddPdfPages (the same override
            // FragmentPainter's own PushClip and PdfGenerator.HandleLinks already prefer over PageBoxRect)
            // and reflects a `@page :first`/named/margin-overridden page's own area; issue #146.
            var viewportRect = box.HtmlContainer!.PageClipOverride ?? box.HtmlContainer!.PageBoxRect;

            // A `text` clip layer's shape (issue #1117) is the union of every laid-out glyph outline
            // reachable from `fragment`'s subtree - built lazily, at most once per call, and shared by
            // every layer that resolves to `text` (the solid-color layer and/or any number of
            // background-image layers), rather than once per layer like a rounded-rect clip is. Disposed
            // once below, after every layer that might have used it has painted.
            RGraphicsPath? textClipPath = null;
            var textClipPathBuilt = false;
            RGraphicsPath? TextClipPath()
            {
                if (!textClipPathBuilt)
                {
                    textClipPathBuilt = true;
                    if (fragment is not null) textClipPath = BuildTextClipPath(g, fragment);
                }
                return textClipPath;
            }

            // Resolves one layer's clip value to its painting rectangle and (when the box needs a
            // non-rectangular clip) the path to paint through. `text` reuses `TextClipPath()`'s shared
            // path when one could be built; when it can't (no fragment in scope, no clippable words, an
            // outline-less font run, or a vertical writing mode - BuildTextClipPath's own null cases),
            // it falls back to exactly `border-box`'s own treatment, rounded corners included, matching
            // this engine's pre-#1117 behavior for an unrecognized `background-clip` value instead of
            // silently losing the rounding too.
            (RRect ClipRect, RGraphicsPath? ClipShape, bool Shared) ResolveClip(string clipValue)
            {
                if (clipValue == Keywords.Text)
                {
                    if (TextClipPath() is { } textPath) return (rect, textPath, true);
                    clipValue = Keywords.BorderBox;
                }

                var clipRect = BoxModelRect(clipValue);
                if (!box.IsRounded) return (clipRect, null, false);

                var radii = ClipRadii(clipValue, clipRect);
                var roundedPath = RenderUtils.GetRoundRect(g, clipRect,
                    radii.TLX, radii.TLY, radii.TRX, radii.TRY,
                    radii.BRX, radii.BRY, radii.BLX, radii.BLY);
                return (clipRect, roundedPath, false);
            }

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
                var colorClipValue = clipLayers[^1];
                var (colorClipRect, colorClipShape, colorShapeShared) = ResolveClip(colorClipValue);

                PaintClippedBrush(g, box, solidBrush, colorClipRect, colorClipShape);
                if (!colorShapeShared) colorClipShape?.Dispose();
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
                var clipValue  = BackgroundLayerResolver.LayerAt(clipLayers, layerIndex);
                var (clipRect, clipShape, shapeShared) = ResolveClip(clipValue);

                void DrawBrush(RBrush brush) => PaintClippedBrush(g, box, brush, clipRect, clipShape);

                CssImagePainter.Paint(g, box.BackgroundImages![layerIndex], layerIndex, originRect, clipRect,
                    clipShape, box.BackgroundPosition, box.BackgroundSize, box.BackgroundRepeat, box.BackgroundAttachment,
                    viewportRect, box, DrawBrush);

                if (!shapeShared) clipShape?.Dispose();
            }

            textClipPath?.Dispose();
        }

        /// <summary>
        /// Fills <paramref name="brush"/> clipped to <paramref name="roundedClipPath"/> (border-radius,
        /// or a <c>background-clip: text</c> glyph-outline union - see
        /// <c>PaintBackground</c>'s local <c>ResolveClip</c>/<see cref="BuildTextClipPath"/>) or <paramref name="clipRect"/>
        /// (plain rectangular case), and disposes the brush afterward. Shared by every per-layer
        /// background-image/gradient draw and the final solid-color fill.
        /// </summary>
        private static void PaintClippedBrush(RGraphics g, CssBox box, RBrush brush, RRect clipRect, RGraphicsPath? roundedClipPath)
        {
            // TODO:a handle it correctly (tables background)
            object? prevMode = null;
            if (box.HtmlContainer is { AvoidGeometryAntialias: false } && roundedClipPath != null)
                prevMode = g.SetAntiAliasSmoothingMode();

            if (roundedClipPath != null)
                g.DrawPath(brush, roundedClipPath);
            else
                g.DrawRectangle(brush, clipRect.X, clipRect.Y, clipRect.Width, clipRect.Height);

            g.ReturnPreviousSmoothingMode(prevMode);
            brush.Dispose();
        }

        /// <summary>
        /// The style/font/baseline/shaping facts a word needs before either a glyph-outline union
        /// (<see cref="BuildTextClipPath"/>'s <c>CollectVerticalWords</c>) or an ink-crossing measurement
        /// (<see cref="AddInkExclusions"/>) can proceed - the exact same resolution
        /// <see cref="DrawWordGlyphs"/> uses to paint the word, so a fallback face or a synthesized
        /// small-caps run is measured/outlined against the same font and baseline shift it is actually
        /// painted with, not the box's own <c>ActualFont</c>.
        /// </summary>
        private readonly record struct WordFontContext(CssBox StyleSource, RFont Font, double BaselineAdjust, TextShapingFeatures Features);

        /// <summary>See <see cref="WordFontContext"/>.</summary>
        /// <param name="word">the word to resolve</param>
        /// <param name="owner">the box whose style the word falls back to when it has no <c>::first-line</c> override of its own</param>
        private static WordFontContext ResolveWordFontContext(CssRect word, CssBox owner)
        {
            var styleSource = word.FirstLineStyle ?? owner;
            var font = CssBox.ResolveWordFont(word, styleSource);
            var baselineAdjust = styleSource.ActualFont.Ascent - font.Ascent;
            var features = styleSource.ResolveWordShapingFeatures(word);
            return new WordFontContext(styleSource, font, baselineAdjust, features);
        }

        /// <summary>
        /// Builds the union of every laid-out word's glyph outline reachable from <paramref name="fragment"/>'s
        /// own subtree - this box's own words plus descendants', stopping at an atomic inline - for a
        /// <c>background-clip: text</c> layer (issue #1117). Membership mirrors
        /// <see cref="DecorationContent"/>'s own walk (out-of-flow descendants and atomic inlines are
        /// skipped, line breaks/images/leaders carry no ink), but this is a smaller, purpose-built walk
        /// rather than a reuse of that class: <see cref="DecorationContent"/>'s own shape is keyed by
        /// <see cref="CssLineBox"/> for decoration-line purposes irrelevant here, and it has no way to
        /// hand back the flat rect/word/font triples a glyph-outline union needs.
        /// </summary>
        /// <returns>
        /// An empty (non-null) path when the subtree has no clippable words at all - correctly clips
        /// the layer to nothing. Null when any reached <i>word</i> - <see cref="RGraphics.GetTextOutline"/>'s
        /// own granularity - produced no usable glyph outline at all (every glyph in it failed: a
        /// CID-keyed CFF or otherwise outline-less font) or the word's own box is set to
        /// <c>writing-mode: sideways-rl</c>/<c>sideways-lr</c> - <c>PaintBackground</c>'s local
        /// <c>ResolveClip</c>'s cue to fall back to the box's ordinary <c>border-box</c> clip instead of
        /// a shape with glyphs missing from it. A word whose glyphs only *partially* decode (e.g. one
        /// character using an escape/seac operator
        /// <see cref="PeachPDF.Fonts.OpenType.Type2CharstringInterpreter"/> doesn't implement, amid
        /// otherwise-decodable sibling glyphs) is accepted as-is rather than triggering
        /// this fallback - <c>GetTextOutline</c> itself has no per-glyph granularity to report that
        /// distinction, so the clip can end up missing just that one glyph's shape. Narrow in practice
        /// (real-world non-CID CFF fonts essentially never use the legacy seac form on ordinary text
        /// glyphs), not separately tracked as an accepted gap.
        /// </returns>
        /// <remarks>
        /// A <c>vertical-rl</c>/<c>vertical-lr</c> box (<see cref="IsVerticalDecorationGeometry"/>) is fully
        /// supported (issue #1123): an upright run's outline is built character-by-character, translating
        /// each character's own outline to the same cell <see cref="EnumerateUprightGlyphPlacements"/>
        /// already resolves for paint (so the two can never disagree on where a character sits), while a
        /// rotated/sideways run's outline is built once for the whole word in its natural (pre-rotation)
        /// frame and then transformed by the same <see cref="SidewaysRotation"/> matrix
        /// <see cref="DrawWordGlyphs"/> paints it through. <c>writing-mode: sideways-rl</c>/<c>sideways-lr</c>
        /// - a different, out-of-scope writing-mode value entirely (<see cref="IsHorizontalWritingMode"/>'s
        /// own remarks) - stays on the unsupported/fallback path exactly as before this issue.
        /// </remarks>
        private static RGraphicsPath? BuildTextClipPath(RGraphics g, BoxFragment fragment)
        {
            RGraphicsPath? union = null;
            var anyUnsupportedRun = false;

            void AddOutline(RGraphicsPath outline)
            {
                if (union is null)
                {
                    union = g.GetGraphicsPath();
                    // Matches GetTextOutline's own per-glyph path (GraphicsAdapter.GetTextOutline
                    // sets this on each outline it builds) - required for a glyph with a nested
                    // counter (the hole in "e"/"o"/"a"/...) to fill correctly regardless of its
                    // contours' winding direction, rather than an even-odd default that happens to
                    // agree only for the simplest single-nested-contour case.
                    union.FillMode = RFillMode.Nonzero;
                }
                union.AddPath(outline);
                outline.Dispose();
            }

            void CollectHorizontalWords(BoxFragment f)
            {
                foreach (var wordFragment in f.Words)
                {
                    var word = wordFragment.Word;
                    if (word.IsLineBreak || word.IsImage || word is CssRectLeader) continue;

                    var text = word.FirstLineText ?? word.Text;
                    if (string.IsNullOrEmpty(text)) continue;

                    var styleSource = word.FirstLineStyle ?? f.Box;
                    var font = CssBox.ResolveWordFont(word, styleSource);
                    var baselineAdjust = styleSource.ActualFont.Ascent - font.Ascent;
                    var wordPoint = new RPoint(wordFragment.Rect.X, wordFragment.Rect.Y + baselineAdjust);
                    var features = styleSource.ResolveWordShapingFeatures(word);

                    // GetTextOutline places the baseline directly, unlike DrawString/wordPoint above
                    // (top-left of the word's own box) - shift down by the font's own ascent, the same
                    // correction SvgRenderer.PaintTextGlyphs already makes for the identical mismatch.
                    var baselineOrigin = new RPoint(wordPoint.X, wordPoint.Y + font.Ascent);
                    var outline = g.GetTextOutline(text, font, baselineOrigin, styleSource.ActualLetterSpacing, features);
                    if (outline is null)
                    {
                        anyUnsupportedRun = true;
                        continue;
                    }

                    AddOutline(outline);
                }
            }

            // An upright run stacks each character down the column (see PaintUprightVerticalRun) - its
            // outline is unioned the same way, one GetTextOutline call per character, translated to that
            // character's own EnumerateUprightGlyphPlacements cell rather than the word's as a whole (no
            // single natural horizontal layout exists to reuse for it, unlike a rotated run below).
            //
            // A font with real vhea/vmtx or VORG metrics (RFont.HasVerticalMetrics/HasVerticalOrigin,
            // issue #1194) needs one more step before a character's outline is unioned in:
            // PaintUprightVerticalRun clips each such character's *paint* to its own reserved cell
            // (rect.X, placement.CellTop, rect.Width, placement.Advance) precisely because a real vmtx
            // advance is routinely narrower than the font's line height (see that method's own remarks),
            // so what is actually painted is smaller than GetTextOutline's raw per-character result.
            // RGraphicsPath.ClipToRect reproduces that same per-cell clip at the path level - the raw
            // outline is intersected with the identical cell before it is added to the union, so the two
            // can never disagree about which pixels are actually inked.
            void CollectUprightWord(BoxFragment f, CssRect word, RRect rect, string text, CssBox styleSource, RFont font, double baselineAdjust, TextShapingFeatures features)
            {
                var needsCellClip = font.HasVerticalMetrics || font.HasVerticalOrigin;

                foreach (var placement in EnumerateUprightGlyphPlacements(g, text, font, rect, baselineAdjust, styleSource.ActualLetterSpacing, features))
                {
                    var baselineOrigin = new RPoint(placement.X, placement.Y + font.Ascent);
                    var outline = g.GetTextOutline(placement.CharText, font, baselineOrigin, styleSource.ActualLetterSpacing, features);
                    if (outline is null)
                    {
                        anyUnsupportedRun = true;
                        continue;
                    }

                    if (needsCellClip)
                    {
                        var cell = new RRect(rect.X, placement.CellTop, rect.Width, placement.Advance);
                        var clipped = outline.ClipToRect(cell);
                        outline.Dispose();
                        outline = clipped;
                    }

                    AddOutline(outline);
                }
            }

            // A rotated (sideways) run is one natural horizontal glyph run reoriented as a whole (see
            // DrawWordGlyphs's own sideways branch) - so its outline is built once, in that same natural
            // (pre-rotation) frame, then carried into the word's actual physical footprint by the exact
            // rotation matrix paint uses, rather than rebuilt per character.
            void CollectRotatedWord(RRect rect, string text, CssBox styleSource, RFont font, double baselineAdjust, TextShapingFeatures features)
            {
                var naturalBaselineOrigin = new RPoint(0, baselineAdjust + font.Ascent);
                var outline = g.GetTextOutline(text, font, naturalBaselineOrigin, styleSource.ActualLetterSpacing, features);
                if (outline is null)
                {
                    anyUnsupportedRun = true;
                    return;
                }

                outline.Transform(SidewaysRotation(rect));
                AddOutline(outline);
            }

            void CollectVerticalWords(BoxFragment f)
            {
                foreach (var wordFragment in f.Words)
                {
                    var word = wordFragment.Word;
                    if (word.IsLineBreak || word.IsImage || word is CssRectLeader) continue;

                    var text = word.FirstLineText ?? word.Text;
                    if (string.IsNullOrEmpty(text)) continue;

                    var ctx = ResolveWordFontContext(word, f.Box);

                    if (IsUprightWordOrientation(f.Box, word))
                        CollectUprightWord(f, word, wordFragment.Rect, text, ctx.StyleSource, ctx.Font, ctx.BaselineAdjust, ctx.Features);
                    else
                        CollectRotatedWord(wordFragment.Rect, text, ctx.StyleSource, ctx.Font, ctx.BaselineAdjust, ctx.Features);
                }
            }

            void CollectWords(BoxFragment f)
            {
                if (IsVerticalDecorationGeometry(f.Box))
                {
                    CollectVerticalWords(f);
                    return;
                }

                if (!IsHorizontalWritingMode(f.Box))
                {
                    // writing-mode: sideways-rl/-lr - a different, out-of-scope writing-mode value
                    // (see this method's own remarks and IsHorizontalWritingMode).
                    anyUnsupportedRun = true;
                    return;
                }

                CollectHorizontalWords(f);
            }

            // The out-of-flow/atomic-inline skip below is a fact about a *descendant*, per
            // DecorationContent.Collect's own identical rule - it must not apply to `fragment` itself,
            // or a box that IS an atomic inline or out-of-flow (an inline-block "badge", a float, an
            // absolutely-positioned element) with its own background-clip: text would never reach its
            // own words at all, leaving `union` null/`anyUnsupportedRun` false and returning an empty
            // (not a null/fallback) path - silently clipping its own background to nothing rather than
            // to its own text or to border-box. Mirrors DecorationContent.Of's own split: Collect only
            // ever recurses into fragment.Children, while the root's own words are gathered separately
            // and unconditionally.
            void Collect(BoxFragment f)
            {
                if (f.Box.IsOutOfFlow) return;
                if (DomUtils.IsAtomicInline(f.Box)) return;

                CollectWords(f);

                foreach (var child in f.Children) Collect(child);
            }

            CollectWords(fragment);
            foreach (var child in fragment.Children) Collect(child);

            if (anyUnsupportedRun)
            {
                union?.Dispose();
                return null;
            }

            return union ?? g.GetGraphicsPath();
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
            List<BoxShadowGrammar.ShadowLayer> layers;
            using (var pooledTokens = CssValueParser.GetCssTokensPooled(box.BoxShadow))
            {
                List<Token> tokens = pooledTokens;
                layers = BoxShadowGrammar.TryParse(tokens);
            }
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
        /// Paints <c>filter: drop-shadow()</c>'s approximation - the same concentric-alpha-ramped-fill
        /// technique <see cref="PaintOutsetShadow"/> already implements for <c>box-shadow</c>'s own blur
        /// (accepted gap: non-Gaussian, see that method's remarks), reused wholesale rather than
        /// re-derived, but keyed off <paramref name="box"/>'s own border-box rectangle rather than a true
        /// per-pixel alpha silhouette of its actual rendered content (text glyphs, image cutouts, etc.) -
        /// an approximation on the same accepted-gap footing as <c>box-shadow</c>'s own non-Gaussian blur.
        /// A box can carry more than one <c>drop-shadow()</c> in its filter list (each painted here in
        /// authored order, all keyed off the same border-box); real per-primitive filter chaining - where a
        /// later <c>drop-shadow()</c> would see the *previous* filter step's output, not the original box -
        /// is out of scope for this same reason.
        /// </summary>
        private static void PaintFilterDropShadows(RGraphics g, CssBox box, in BoxDecorationGeometry geometry)
        {
            var functions = box.ActualFilterFunctions;
            if (functions.Count == 0) return;

            var borderBox = geometry.DecorationRect;

            foreach (var function in functions)
            {
                if (function.Name != "drop-shadow") continue;

                var dx = CssValueParser.ParseLength(function.Arguments[0], 0, box);
                var dy = CssValueParser.ParseLength(function.Arguments[1], 0, box);
                var blur = CssValueParser.ParseLength(function.Arguments[2], 0, box);
                var color = ResolveShadowColor(box, function.Arguments[3]);

                PaintOutsetShadow(g, box, borderBox, dx, dy, blur, spread: 0, color);
            }
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

            var steps = BlurSteps(blur, g.PixelsPerPoint);
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
                var steps = BlurSteps(blur, g.PixelsPerPoint);
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

        /// <summary>
        /// Number of concentric fills used to approximate a blur of the given radius (in points).
        /// <paramref name="blur"/> is the caller's raw, un-divided layout-space (PixelsPerInch-inflated)
        /// value - unlike each ring's own position/size (already divided by PixelsPerPoint in
        /// BuildRingPath/BuildLayerRoundRect), so the step count needs the same correction here or the
        /// blur approximation gets smoother/coarser depending on an unrelated internal scaling knob.
        /// </summary>
        private static int BlurSteps(double blur, double pixelsPerPoint) =>
            Math.Clamp((int)Math.Round(blur / pixelsPerPoint * 2), 6, 40);

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
        /// PDF backend has no clip-subtract primitive. <paramref name="outer"/>/<paramref name="hole"/> are in
        /// raw layout-space coordinates, like every other box-geometry <see cref="RGraphicsPath"/> builder -
        /// divided by <see cref="RGraphics.PixelsPerPoint"/> here since the path itself has no ambient
        /// transform to divide it back down (issue #812; see <c>RenderUtils.GetRoundRect</c>'s remarks).</summary>
        private static RGraphicsPath BuildRingPath(RGraphics g, RRect outer, RRect hole)
        {
            var ppp = g.PixelsPerPoint;
            var path = g.GetGraphicsPath();

            path.Start(outer.Left / ppp, outer.Top / ppp);
            path.LineTo(outer.Right / ppp, outer.Top / ppp);
            path.LineTo(outer.Right / ppp, outer.Bottom / ppp);
            path.LineTo(outer.Left / ppp, outer.Bottom / ppp);
            path.CloseFigure();

            path.AddMove(hole.Left / ppp, hole.Top / ppp);
            path.LineTo(hole.Right / ppp, hole.Top / ppp);
            path.LineTo(hole.Right / ppp, hole.Bottom / ppp);
            path.LineTo(hole.Left / ppp, hole.Bottom / ppp);
            path.CloseFigure();

            path.FillMode = RFillMode.EvenOdd;
            return path;
        }

        /// <summary>
        /// Paints the text decoration of a box whose own decoration area is a single rectangle - its
        /// border box - over the in-flow inline content that rectangle contains, one span per line box.
        /// </summary>
        /// <remarks>
        /// <see href="https://www.w3.org/TR/css-text-decor-3/#line-decoration">css-text-decor-3 §2.4</see>:
        /// a text decoration declared on (or propagated to) a block container propagates to an anonymous
        /// inline box wrapping that block's in-flow inline-level content, and through its in-flow
        /// block-level descendants to theirs. It is therefore drawn across that content - not across the
        /// block's full width, which is what painting it over the block's own border box did, and what
        /// made a <c>display: block</c> link's underline run to the right page margin.
        /// <para>
        /// The spans come from the fragment tree rather than the box tree, so a block broken across pages
        /// decorates exactly the lines that landed on the page being painted. Each descendant that is
        /// hosted on a line box already carries its content as per-line rectangles - an inline box's own,
        /// including its padding and border, which §2.4 requires ("the margins, border, and padding of
        /// descendant inline boxes are not" skipped, unlike the decorating box's own) - so such a
        /// fragment contributes its rectangles and is not descended into. Anything else is a block-level
        /// box whose own rectangle is its border box again, and is descended into for the same reason
        /// this method exists. Out-of-flow descendants are skipped outright, per the same section.
        /// </para>
        /// <para>
        /// An atomic inline is excluded from the line rather than covered by it, per the same section:
        /// "Atomic inlines, such as images and inline blocks, are not decorated." Its rectangle is kept
        /// out of the span and subtracted from what is drawn, so one span can become several segments -
        /// see <see cref="DecorationSegments"/>. What exactly is subtracted is its margin box; §2.4 does
        /// not say which box, so that is this engine's choice, taken because a margin authored around an
        /// atomic inline reads as part of the space it occupies on the line.
        /// </para>
        /// </remarks>
        private static void PaintPropagatedDecoration(RGraphics g, CssBox box, BoxFragment fragment, RRect clip)
        {
            if (!DecorationsWorthCollecting(box)) return;

            var content = DecorationContent.Of(fragment, collectSpans: true);

            foreach (var lineBox in content.Order)
            {
                var rect = content.SpanOf(lineBox);

                if (IsRectVisible(rect, clip))
                {
                    PaintDecoration(g, box, rect, hasLeftEdge: false, hasRightEdge: false,
                        GetFirstLineStyleForRect(lineBox), ownDecorationArea: false, content, lineBox);
                }
            }
        }

        /// <summary>
        /// Whether <paramref name="box"/> declares a <c>text-decoration-line</c> other than <c>none</c>.
        /// </summary>
        private static bool DeclaresADecorationLine(CssBox? box) =>
            box is not null && !string.IsNullOrEmpty(box.TextDecorationLine) && box.TextDecorationLine != Keywords.None;

        /// <summary>
        /// Whether it is worth walking <paramref name="box"/>'s fragment subtree to find what its
        /// decoration covers. The overwhelming majority of boxes declare no decoration at all and so never
        /// start the walk; a <c>::first-line</c> rule can declare one the box itself doesn't, so it is
        /// asked too, and which of the two applies to a given line stays
        /// <see cref="PaintDecoration"/>'s own decision.
        /// </summary>
        private static bool DecorationsWorthCollecting(CssBox box) =>
            DeclaresADecorationLine(box) || DeclaresADecorationLine(box.ResolvedFirstLineStyle);

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
        /// <param name="ownDecorationArea">
        /// whether <paramref name="rectangle"/> is the box's own decoration area, and so is measured from
        /// its border edges. False for a span <see cref="PaintPropagatedDecoration"/> found in a
        /// descendant, which is inline content geometry already inside the box's padding - compensating
        /// for padding the rectangle never included would only displace the line.
        /// </param>
        /// <param name="content">
        /// what the line covers, as gathered from the fragment subtree - the atomic inlines it must break
        /// around (css-text-decor-3 §2.4) and the words whose ink it may have to skip
        /// (<c>text-decoration-skip-ink</c>, css-text-decor-4 §2.5). Null draws one unbroken line per
        /// keyword, exactly as before either rule existed.
        /// </param>
        /// <param name="lineBox">the line box <paramref name="rectangle"/> belongs to; null for a whole-box rectangle</param>
        private static void PaintDecoration(RGraphics g, CssBox box, RRect rectangle, bool hasLeftEdge, bool hasRightEdge,
            CssBox? firstLineStyle = null, bool ownDecorationArea = true,
            DecorationContent? content = null, CssLineBox? lineBox = null)
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

            // A true vertical writing mode (vertical-rl/-lr) lays its lines out along the physical Y
            // axis - rectangle.Width is the column's thickness, rectangle.Height its extent along the
            // line - the inverse of horizontal-tb. A sideways-rl/-lr box is deliberately excluded here
            // (matching WritingModeFrame.ForContentBox's own IsVertical check): its own line boxes are
            // still laid out horizontally, only the glyphs are rotated for painting (see
            // FragmentPainter.Text.cs's SidewaysRotation), so its decoration geometry is horizontal too.
            var isVertical = IsVerticalDecorationGeometry(box);

            // The canonical block-start resolver (also what WritingModeFrame.BlockStartIsRight and
            // every logical box-model longhand call) rather than a second, inline VerticalRl comparison
            // - see CLAUDE.md's "don't write two independent parsers for the same CSS value grammar
            // across layers" convention. Reading it this way also means this stays correct for free if
            // IsVerticalDecorationGeometry is ever widened to include sideways-rl/-lr (issue #766):
            // LogicalPropertyResolver.BlockStart already maps those to the same physical sides as
            // vertical-rl/vertical-lr respectively.
            var blockStartIsRight = LogicalPropertyResolver.BlockStart(box.WritingMode.Value) == PhysicalSide.Right;

            // The inline-axis span - physical X normally, physical Y under a true vertical mode.
            // Leading/trailing edge trims follow inline-start/inline-end, which is physical top for
            // BOTH vertical-rl and vertical-lr (css-writing-modes-4 §6.4 - only the block axis differs
            // between them), so this needs only a two-way branch rather than a three-way one.
            double spanStart, spanEnd;
            if (isVertical)
            {
                spanStart = rectangle.Y;
                if (ownDecorationArea && hasLeftEdge)
                    spanStart += box.ActualPaddingTop + box.ActualBorderTopWidth;

                spanEnd = rectangle.Bottom;
                if (ownDecorationArea && hasRightEdge)
                    spanEnd -= box.ActualPaddingBottom + box.ActualBorderBottomWidth;
            }
            else
            {
                spanStart = rectangle.X;
                if (ownDecorationArea && hasLeftEdge)
                    spanStart += box.ActualPaddingLeft + box.ActualBorderLeftWidth;

                spanEnd = rectangle.Right;
                if (ownDecorationArea && hasRightEdge)
                    spanEnd -= box.ActualPaddingRight + box.ActualBorderRightWidth;
            }

            // Captured once, rather than read back from pen.Width for the rest of this method: pen is a
            // cached instance keyed only by color (RAdapter.GetPen), and WavyDecorationRenderer.
            // StrokeWavyLine below calls g.GetPen(color) itself for the SAME color - the very same cached
            // pen - and sets its own Width for that draw. Re-reading pen.Width afterward for a later
            // keyword or segment would see that leftover value instead of this decoration's own
            // thickness.
            var thickness = ResolveDecorationThickness(styleSource.TextDecorationThickness, styleSource, g.PixelsPerPoint);
            var pen = g.GetPen(textDecorationActualColor);
            pen.Width = thickness;
            pen.DashStyle = TextDecorationStyleMapper.ToDashStyle(textDecorationStyle);

            var span = new DecorationInterval(spanStart, spanEnd);

            // The atomic-inline exclusion is inline-axis-band reasoning that only understands a
            // horizontal band today (a margin box measured left to right) - and, separately,
            // CssLayoutEngine's vertical path records no per-line rectangle for an atomic inline at all,
            // so there is no layout fact to exclude around even if the band math were made axis-aware
            // (see .claude/accepted-gaps/text-decoration-skip-ink-and-atomic-inline-exclusion-are-horizontal-only.md).
            // This stays confined to a horizontal writing mode, unchanged since before #1075 (sideways-*
            // included) - do not remove this guard as apparent dead code without first making both the
            // band math AND vertical layout's own line-box recording axis-aware.
            var horizontal = IsHorizontalWritingMode(box);

            // The atomic inlines to break around are the same for every keyword: they are a fact about
            // the line's content, not about where a particular line sits on it.
            var boxExclusions = lineBox is null || !horizontal ? null : content?.ExclusionsFor(lineBox);

            // Ink-crossing exclusion, by contrast, IS reachable under a true vertical writing mode for a
            // rotated (sideways) run: it is one ordinary horizontal glyph run reoriented as a whole (see
            // DrawWordGlyphs's sideways branch), which GetInkCrossings can measure once AddInkExclusions
            // maps the band into that run's own pre-rotation frame - see its own remarks. An upright run
            // has no such natural horizontal layout to fall back on and stays unskipped (issue #1145).
            // Ink differs per keyword - an underline and an overline cross different parts of the same
            // glyphs - so it is measured inside the loop, against that line's own band.
            var inkWords = (horizontal || isVertical) && SkipsInk(styleSource) ? content?.InkWordsOn(lineBox) : null;

            // Where an automatic underline hangs from. Only the underline needs it, but it is a fact about
            // the line rather than about a keyword, so it is resolved once rather than per keyword.
            //
            // The line reports its baseline the way layout measured it - a whole (rounded) ascent above
            // each word's rectangle - while glyphs are painted from the unrounded TextBaselineOffset, so
            // the decorating box's own font supplies that correction. css-text-decor-3 §2.5 leaves the
            // exact position UA-defined, but requires "a single thickness and position on each line for
            // the decorations deriving from a single decorating box", which is what taking it from the
            // line rather than from each rectangle's own top is what guarantees. Its note is the reason
            // to prefer the line: "since line decorations can span elements with varying font sizes and
            // vertical alignments, the best position for a line decoration is not necessarily the ideal
            // position dictated by the decorating box".
            //
            // For a line set entirely in the decorating box's font the two steps cancel back to
            // `rectangle.Top + TextBaselineOffset`, which is what this expression used to be and what it
            // still paints. Meaningless under a true vertical mode (no alphabetic baseline runs along a
            // column), so only consulted in the horizontal branch below.
            var font = styleSource.ActualFont;
            var baseline = content?.AlphabeticBaselineOn(lineBox, box) is { } lineBaseline
                ? lineBaseline - (font.Ascent - font.TextBaselineOffset)
                : rectangle.Top + font.TextBaselineOffset;

            // +1 when the cross-axis coordinate increases moving from the "over" side toward "under"
            // (horizontal-tb and vertical-lr); -1 when it decreases (vertical-rl). css-writing-modes-4
            // §6.3 defines over/under as the ascender-/descender-relative sides - block-start/block-end
            // respectively - regardless of writing mode.
            var underSign = blockStartIsRight ? -1 : 1;

            double overPos, underPos, throughPos;
            if (isVertical)
            {
                overPos = blockStartIsRight ? rectangle.Right : rectangle.Left;
                underPos = blockStartIsRight ? rectangle.Left : rectangle.Right;
                throughPos = rectangle.X + rectangle.Width / 2;
            }
            else
            {
                overPos = rectangle.Top;
                underPos = rectangle.Bottom;
                throughPos = rectangle.Top + rectangle.Height / 2f;
            }

            var offset = ResolveDecorationOffset(styleSource.TextUnderlineOffset, styleSource, g.PixelsPerPoint);

            // css-text-decor-4 §2.5's left/right half of text-underline-position (issue #1146): pins the
            // underline to a literal physical edge instead of the writing mode's own logical "under" side.
            // Meaningful only under a true vertical writing mode - inert under horizontal-tb, where there
            // is no physical left/right edge distinct from the line's own inline extent.
            var underlineSide = isVertical ? styleSource.TextUnderlineSide.Value : TextUnderlineSide.Auto;
            var pinnedUnderlineEdge = underlineSide switch
            {
                TextUnderlineSide.Left => rectangle.Left,
                TextUnderlineSide.Right => rectangle.Right,
                _ => (double?)null
            };

            // "If this causes the underline to be drawn on the 'over' side of the text, then an overline
            // also switches sides and is drawn on the 'under' side" (§2.5) - so when the pinned edge above
            // lands where the overline would otherwise draw, the overline moves to the opposite physical
            // edge instead of overlapping it. Conditioned on an underline actually being one of this box's
            // declared decoration lines: the note describes what happens when the underline "is drawn" -
            // a box with only "overline" has no underline to collide with, so its overline must not be
            // moved just because text-underline-position happens to name the conflicting side.
            var overlineSwitchesSides = pinnedUnderlineEdge == overPos
                && ContainsDecorationLineKeyword(textDecorationLine, Keywords.Underline);

            double ResolveUnderlineCross()
            {
                var clearance = ResolveAutomaticUnderlineClearance(thickness, g.PixelsPerPoint);

                if (pinnedUnderlineEdge is { } pinned)
                {
                    // Inset from the pinned edge toward the column's interior by the same clearance every
                    // other vertical-mode underline keeps off its own edge - left's interior direction is
                    // +X, right's is -X. text-underline-offset moves the line further from the text
                    // instead - the opposite direction from clearance, mirroring the non-pinned formula
                    // below's own clearance/offset sign relationship (ResolveDecorationOffset's remarks:
                    // "a positive value moves the underline further from the text").
                    var inwardSign = underlineSide == TextUnderlineSide.Left ? 1 : -1;
                    return pinned + inwardSign * clearance - inwardSign * offset;
                }

                // No real vertical baseline runs along a column (a true vertical mode's glyphs are
                // either upright and individually placed, or a rotated horizontal run - neither has one
                // alphabetic line the way horizontal text does), so `from-font`'s real-metric offset
                // and `auto`'s baseline-relative clearance both fall back to the same rectangle-relative
                // approximation there: inset from the "under" edge by the ordinary clearance amount,
                // which keeps the line close to (but inside) that edge - visually verified by
                // rasterizing rather than assumed exact against real vertical font metrics (a larger,
                // separate undertaking - see .claude/accepted-gaps/no-vertical-writing-mode-layout.md).
                if (isVertical)
                    return underPos - underSign * clearance + underSign * offset;

                var basePos = styleSource.TextUnderlinePosition.Value switch
                {
                    // OpenType's underlinePosition is itself negative-from-baseline by convention, so
                    // subtracting it moves the line down (away from the baseline) exactly as expected.
                    TextUnderlinePosition.FromFont => baseline - font.UnderlinePosition,
                    // "Under" edge: below the line's own lowest descender, per css-text-decor-4 §2.5 -
                    // rectangle already spans the full line content (ascent through descent), so its own
                    // bottom edge is that basis.
                    TextUnderlinePosition.Under => underPos,
                    _ => baseline + clearance
                };

                return basePos + offset;
            }

            // text-decoration-line may list several keywords (e.g. "underline overline"); draw each.
            // The block-end physical padding/border - bottom in horizontal-tb, and whichever physical
            // side is block-end for the vertical mode in play (left for vertical-rl, right for
            // vertical-lr, per css-writing-modes-4 §6.4) - insets a keyword drawn there. Under a true
            // vertical mode a keyword can now sit on either physical edge (an underline pinned via
            // text-underline-position: left/right, or an overline that switched to avoid colliding with
            // one - issue #1146), so the block-start side's own inset is resolved too; horizontal-tb has
            // no such pinning, so it keeps exactly its pre-#1146 single block-end-based inset applied to
            // every keyword uniformly.
            double blockStartInset = 0, blockEndInset = 0;
            if (ownDecorationArea)
            {
                if (isVertical)
                {
                    blockStartInset = blockStartIsRight
                        ? box.ActualPaddingRight - box.ActualBorderRightWidth
                        : box.ActualPaddingLeft - box.ActualBorderLeftWidth;
                    blockEndInset = blockStartIsRight
                        ? box.ActualPaddingLeft - box.ActualBorderLeftWidth
                        : box.ActualPaddingRight - box.ActualBorderRightWidth;
                }
                else
                {
                    blockEndInset = box.ActualPaddingBottom - box.ActualBorderBottomWidth;
                }
            }

            foreach (var line in textDecorationLine.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                double cross = line switch
                {
                    Keywords.Underline => ResolveUnderlineCross(),
                    Keywords.LineThrough => throughPos,
                    Keywords.Overline => overlineSwitchesSides ? underPos : overPos,
                    _ => double.NaN
                };

                if (double.IsNaN(cross)) continue;

                // Which physical edge this keyword's line actually landed on, after any pinning/switching
                // above - an unpinned underline is always at block-end ("under"), a non-switched overline
                // always at block-start ("over"); a line-through has no edge of its own. pinnedUnderlineEdge
                // is only ever non-null under a true vertical mode, so this is exactly the pre-#1146
                // line == Overline test under horizontal-tb (where overlineSwitchesSides is always false) -
                // needed unconditionally (not gated on isVertical) because StrokeDecorationSegment's
                // double-style growSign relies on this exact keyword-based split for horizontal-tb too.
                var isAtOverEdge = line switch
                {
                    Keywords.Overline => !overlineSwitchesSides,
                    Keywords.Underline => pinnedUnderlineEdge is { } pinnedEdge && pinnedEdge == overPos,
                    _ => false
                };

                // The inset itself, unlike growSign above, stays exactly at its pre-#1146 single
                // block-end-based treatment under horizontal-tb (applied to every keyword uniformly,
                // overline included) - only a true vertical mode's now-position-dependent keywords need
                // the block-start side's own value at all.
                var insetAtBlockStart = isVertical && isAtOverEdge;
                cross += (insetAtBlockStart ? underSign : -underSign) * (insetAtBlockStart ? blockStartInset : blockEndInset);

                var exclusions = boxExclusions;

                // css-text-decor-4 §2.5 skips ink under an underline or an overline only - a
                // line-through is never skipped, since a strike is meant to cross the glyphs.
                if (inkWords is not null && line is Keywords.Underline or Keywords.Overline)
                {
                    List<DecorationInterval> combined = boxExclusions is null ? [] : [.. boxExclusions];
                    AddInkExclusions(g, styleSource, inkWords, cross, thickness, isVertical, combined);
                    exclusions = combined;
                }

                foreach (var segment in DecorationSegments.Subtract(span, exclusions ?? []))
                {
                    StrokeDecorationSegment(g, pen, thickness, textDecorationActualColor, textDecorationStyle, line,
                        segment.Start, segment.End, cross, isVertical, underSign, isAtOverEdge);
                }
            }
        }

        /// <summary>
        /// Strokes one segment of a decoration line, in the <c>text-decoration-style</c> it is drawn with.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>solid</c>/<c>dotted</c>/<c>dashed</c> are one stroke each, differing only in the pen's dash
        /// pattern, which <see cref="TextDecorationStyleMapper.ToDashStyle"/> has already set. <c>double</c>
        /// and <c>wavy</c> are not a single pen stroke at all: the former is two strokes, the latter a
        /// stroked <see cref="Html.Adapters.RGraphicsPath"/> built by
        /// <see cref="WavyDecorationRenderer.StrokeWavyLine"/> - see that class's remarks for the wave's
        /// geometry and why its phase is anchored to this segment's own start rather than the whole line's.
        /// </para>
        /// <para>
        /// <see href="https://www.w3.org/TR/css-text-decor-3/#text-decoration-style-property">css-text-decor-3
        /// §2.2</see> does not define <c>double</c> itself - it says its value has "the same meaning as for
        /// the border-style properties", which for
        /// <see href="https://www.w3.org/TR/css-backgrounds-3/#border-style">css-backgrounds-3</see>'s
        /// <c>double</c> means "two lines ... the sum of the two lines and the space between them equals
        /// the value of border-width". Each stroke here is <c>Max(thickness / 3, one CSS pixel)</c> and
        /// the gap between them matches - so the resolved <c>text-decoration-thickness</c> is honoured as
        /// a literal total (three equal thirds) once it is wide enough for each third to still read as a
        /// visible line, and only exceeded below that floor (the initial 1px <c>auto</c> thickness would
        /// otherwise divide into three one-third-pixel hairlines fainter than the solid underline
        /// <c>double</c> is meant to be a heavier version of). <c>wavy</c>, by contrast, is defined in
        /// its own words ("Draw a wavy line") rather than by that cross-reference - the one style
        /// css-text-decor-3 §2.2 does not leave to <c>border-style</c> at all.
        /// </para>
        /// <para>
        /// Which way <c>double</c>'s second stroke - and <c>wavy</c>'s centerline, via
        /// <see cref="WavyDecorationRenderer.GrowthDirection"/> - grows is measured against Chrome 141
        /// rather than reasoned about: at 300dpi it keeps a single stroke exactly where <c>solid</c> puts
        /// it and adds the second toward the "over" side for an overline and toward "under" for both an
        /// underline and a line-through (css-writing-modes-4 §6.3's ascender-/descender-relative sides -
        /// physical up/down under horizontal-tb, <paramref name="underSign"/> giving the correct physical
        /// direction under a true vertical mode). That also suits an underline's own anchor, whose near
        /// edge is kept on the "under" side of the baseline (see
        /// <see cref="ResolveAutomaticUnderlineClearance"/>) and would be pushed back through the glyphs
        /// by growing toward "over" instead. <c>wavy</c> is not yet extended to a true vertical writing
        /// mode - <see cref="WavyDecorationRenderer"/> always reasons in physical X/Y, so it falls back
        /// to a plain solid line there instead of a mispositioned wave.
        /// </para>
        /// </remarks>
        /// <param name="g">the device to draw into</param>
        /// <param name="pen">the pen to stroke with - <c>double</c> temporarily narrows its width, then restores <paramref name="thickness"/></param>
        /// <param name="thickness">the resolved total <c>text-decoration-thickness</c>, captured once by the caller rather than read back from <paramref name="pen"/>.Width (see <see cref="PaintDecoration"/>'s own remarks on why)</param>
        /// <param name="color">the decoration's resolved color - only <c>wavy</c> needs it directly, to fetch its own cached pen for its stroked path</param>
        /// <param name="style">the resolved <c>text-decoration-style</c></param>
        /// <param name="line">which decoration keyword this segment belongs to (<c>underline</c>/<c>overline</c>/<c>line-through</c>)</param>
        /// <param name="x1">the inline-axis span start - physical X normally, physical Y under a true vertical mode</param>
        /// <param name="x2">the inline-axis span end</param>
        /// <param name="cross">the cross-axis position - physical Y normally, physical X under a true vertical mode</param>
        /// <param name="isVertical">whether the cross axis is physical X (a true vertical writing mode) rather than physical Y</param>
        /// <param name="underSign">+1 when increasing <paramref name="cross"/> moves toward "under" (horizontal-tb, vertical-lr); -1 when it moves toward "over" instead (vertical-rl)</param>
        /// <param name="atBlockStart">
        /// whether this segment's own keyword physically landed on the block-start ("over") edge rather
        /// than block-end ("under") - horizontal-tb's overline always does, matching this parameter's
        /// pre-#1146 behavior exactly; under a true vertical mode, text-underline-position: left/right can
        /// pin an underline there instead, or move an overline off it to avoid colliding with a pinned
        /// underline (<c>PaintDecoration</c>'s own <c>overlineSwitchesSides</c>) - see its remarks.
        /// </param>
        private static void StrokeDecorationSegment(RGraphics g, RPen pen, double thickness, RColor color, string? style, string line,
            double x1, double x2, double cross, bool isVertical, int underSign, bool atBlockStart)
        {
            void Draw(double at)
            {
                if (isVertical) g.DrawLine(pen, at, x1, at, x2);
                else g.DrawLine(pen, x1, at, x2, at);
            }

            switch (style)
            {
                case Keywords.Double:
                {
                    var strokeWidth = DoubleStrokeWidth(thickness, g.PixelsPerPoint);
                    var separation = 2 * strokeWidth;

                    // A keyword at the block-start ("over") edge grows further toward "over"; one at
                    // block-end ("under") - underline, line-through, or an overline that switched there
                    // (issue #1146) - grows further toward "under" instead, so it continues outward past
                    // wherever it actually ended up rather than growing back through the glyphs (or into a
                    // pinned underline already occupying the edge it left).
                    var growSign = atBlockStart ? -underSign : underSign;

                    // pen is a shared, per-color-cached RPen (RAdapter.GetPen) - the try/finally
                    // guarantees the temporary narrower width is undone even if a Draw call throws, so a
                    // paint-time failure here can never leave a later, unrelated stroke of the same
                    // color (including a subsequent wavy segment, which fetches this same cached pen for
                    // its own Width assignment) rendered at this segment's width instead of its own.
                    pen.Width = strokeWidth;
                    try
                    {
                        Draw(cross);
                        Draw(cross + growSign * separation);
                    }
                    finally
                    {
                        pen.Width = thickness;
                    }
                    return;
                }

                case Keywords.Wavy when !isVertical:
                    WavyDecorationRenderer.StrokeWavyLine(g, color, line, x1, x2, cross, thickness);
                    return;

                default:
                    Draw(cross);
                    return;
            }
        }

        /// <summary>
        /// The narrowest a single <c>double</c> stroke is allowed to render, regardless of how thin
        /// <c>text-decoration-thickness / 3</c> would otherwise be - one CSS pixel, the same hairline
        /// floor <see cref="ResolveAutomaticUnderlineClearance"/> already uses elsewhere in this file.
        /// </summary>
        private static double MinimumVisibleDoubleStrokeWidth(double pixelsPerPoint) =>
            Length.PointsPerPx * pixelsPerPoint;

        /// <summary>
        /// One <c>double</c> decoration's per-stroke width, given the resolved total
        /// <c>text-decoration-thickness</c> - shared by <see cref="StrokeDecorationSegment"/> (paint)
        /// and <see cref="DoubleOverlineExtraReachAbove"/> (layout, issue #1124's reserved headroom) so
        /// the two always agree on what the painter will actually draw.
        /// </summary>
        private static double DoubleStrokeWidth(double totalThickness, double pixelsPerPoint) =>
            Math.Max(totalThickness / 3, MinimumVisibleDoubleStrokeWidth(pixelsPerPoint));

        /// <summary>
        /// How far above its own single-line position (<c>rectangle.Top</c>) a <c>double</c> overline's
        /// outer stroke reaches - the extra ascent-side headroom <c>CssLayoutEngine.LineBoxContributionOf</c>
        /// reserves (issue #1124) so ordinary pagination leaves room for it rather than the page clip
        /// silently discarding the stroke. Zero for anything other than an overline drawn <c>double</c>.
        /// </summary>
        internal static double DoubleOverlineExtraReachAbove(CssBox box, double pixelsPerPoint)
        {
            if (box.TextDecorationStyle != Keywords.Double) return 0;

            var line = box.TextDecorationLine;
            if (string.IsNullOrEmpty(line) || line == Keywords.None) return 0;
            if (!ContainsDecorationLineKeyword(line, Keywords.Overline)) return 0;

            var totalThickness = ResolveDecorationThickness(box.TextDecorationThickness, box, pixelsPerPoint);
            var strokeWidth = DoubleStrokeWidth(totalThickness, pixelsPerPoint);
            return 2 * strokeWidth + strokeWidth / 2; // separation (2x) + the outer stroke's own half-width
        }

        /// <summary>
        /// Whether <paramref name="textDecorationLine"/>'s space-separated keyword list contains
        /// <paramref name="keyword"/> as a whole token, without <see cref="string.Split(char[])"/>'s
        /// array allocation - <see cref="DoubleOverlineExtraReachAbove"/> calls this once per word (and
        /// per inline ancestor) during layout (<c>CssLayoutEngine.LineBoxContributionOf</c>), a hot path
        /// where <c>PaintDecoration</c>'s own per-line-box <c>Split</c> (which needs the tokens
        /// themselves, not just a membership test) would be wasteful to repeat.
        /// </summary>
        private static bool ContainsDecorationLineKeyword(string textDecorationLine, string keyword)
        {
            var span = textDecorationLine.AsSpan();
            int start = 0;
            while (start < span.Length)
            {
                var spaceOffset = span[start..].IndexOf(' ');
                var end = spaceOffset < 0 ? span.Length : start + spaceOffset;
                if (span[start..end].SequenceEqual(keyword)) return true;
                if (spaceOffset < 0) break;
                start = end + 1;
            }

            return false;
        }

        /// <summary>
        /// The maximum <c>text-decoration-skip-ink</c> clearance on either side of a skipped glyph, in
        /// CSS pixels. Chromium dilates each ink crossing by the resolved decoration thickness, capped at
        /// this value so extremely thick lines do not create unbounded horizontal gaps.
        /// </summary>
        private const double MaximumInkSkipClearanceCssPixels = 13;

        /// <summary>
        /// Whether <paramref name="styleSource"/> asks for <c>text-decoration-skip-ink</c>
        /// (<see href="https://www.w3.org/TR/css-text-decor-4/#text-decoration-skip-ink-property">css-text-decor-4
        /// §2.5</see>) to interrupt its underlines and overlines where they cross glyph ink.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>auto</c>, the initial value, is defined as UA discretion, and PeachPDF exercises it by
        /// skipping - which is what browsers do, and so what an author who never writes the property
        /// expects to see. <c>none</c> is the opt-out. <c>all</c> asks for the same skipping this does;
        /// the spec's stronger "must" is honoured wherever the ink is decodable at all (see
        /// <see cref="RGraphics.GetInkCrossings"/> for the font shapes where it is not).
        /// </para>
        /// </remarks>
        internal static bool SkipsInk(CssBox styleSource) =>
            styleSource.TextDecorationSkipInk.Value != TextDecorationSkipInk.None;

        /// <summary>
        /// Resolves the center of an automatically positioned underline relative to the alphabetic
        /// baseline it hangs from. The underline's top edge stays below that baseline by at least one CSS
        /// pixel, with the gap growing to half the stroke thickness (rounded up to a CSS pixel), matching
        /// browser behavior for a thick line. Since <see cref="RGraphics.DrawLine"/> centers its stroke on
        /// the supplied coordinate, half the thickness is added once more to obtain that center.
        /// </summary>
        private static double ResolveAutomaticUnderlineClearance(double thickness, double pixelsPerPoint)
        {
            var cssPixel = Length.PointsPerPx * pixelsPerPoint;
            var gap = Math.Max(cssPixel, Math.Ceiling(thickness / (2 * cssPixel)) * cssPixel);
            return gap + thickness / 2;
        }

        /// <summary>
        /// Whether <paramref name="box"/>'s lines run left to right, which is what both decoration
        /// subtractions assume. See <see cref="PaintDecoration"/> for why neither applies otherwise -
        /// this is deliberately not the same test as <see cref="IsVerticalDecorationGeometry"/> (a
        /// sideways-rl/-lr box answers false to both, since its own inline-axis band math is still
        /// horizontal even though its glyphs are rotated).
        /// </summary>
        private static bool IsHorizontalWritingMode(CssBox box) =>
            box.WritingMode.Value is WritingMode.HorizontalTb;

        /// <summary>
        /// Whether <paramref name="box"/>'s own line boxes are laid out along the physical Y axis
        /// (<c>vertical-rl</c>/<c>vertical-lr</c>) rather than physical X - the geometry
        /// <see cref="PaintDecoration"/>'s span/over/under math branches on (issue #1075). Deliberately
        /// excludes <c>sideways-rl</c>/<c>sideways-lr</c>, matching
        /// <see cref="Utils.WritingModeFrame.ForContentBox"/>'s own <c>IsVertical</c> check: a sideways
        /// box's own line boxes are still laid out horizontally (rectangle.Width is the line's inline
        /// extent, not a column's thickness) - only the glyphs painted on them are rotated 90° (see
        /// <see cref="SidewaysRotation"/>) - so its decoration geometry is horizontal too.
        /// </summary>
        private static bool IsVerticalDecorationGeometry(CssBox box) =>
            box.WritingMode.Value is WritingMode.VerticalRl or WritingMode.VerticalLr;

        /// <summary>
        /// Appends the ink crossings of <paramref name="words"/> against a decoration line whose
        /// cross-axis position is <paramref name="cross"/> to <paramref name="into"/>, each already
        /// dilated by the clearance the line keeps from the ink it skips.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The band measured is exactly what the line covers - its own thickness - so a glyph the line
        /// would not actually touch is not skipped. The visible gap either side comes from dilating each
        /// crossing instead, which is what keeps a skip from looking like the line merely grazing a
        /// descender.
        /// </para>
        /// <para>
        /// The clearance follows the line thickness, matching Chromium: a heavy underline needs more room
        /// around a descender than a hairline does. It is capped so unusually thick decorations do not
        /// erase disproportionate lengths of the line.
        /// </para>
        /// <para>
        /// <paramref name="isVertical"/> (issue #1145) picks which physical axis <paramref name="cross"/>
        /// and each returned <see cref="DecorationInterval"/> are measured on, matching
        /// <see cref="PaintDecoration"/>'s own convention - physical Y (inline axis) with <paramref name="cross"/>
        /// itself on physical X under a true vertical writing mode, physical X with <paramref name="cross"/>
        /// on physical Y otherwise. Under a true vertical mode only a <b>rotated</b> word - one ordinary
        /// horizontal glyph run reoriented as a whole by <see cref="SidewaysRotation"/>, exactly as
        /// <see cref="DrawWordGlyphs"/> paints it - reduces to something <see cref="RGraphics.GetInkCrossings"/>
        /// can measure at all: it is measured in that same pre-rotation ("natural") frame, against a band
        /// built by mapping <paramref name="cross"/> through the word's own <see cref="SidewaysRotation"/>
        /// inverse, and the resulting natural-frame crossings are mapped back to physical Y the same way
        /// (<c>physicalY = rect.Y + naturalX</c> - <see cref="SidewaysRotation"/>'s own derivation). An
        /// <b>upright</b> word - stacked character-by-character down the column, with no single natural
        /// horizontal layout to reduce to - has no equivalent reduction and is left unskipped, same as
        /// before this issue (tracked in
        /// .claude/accepted-gaps/text-decoration-skip-ink-and-atomic-inline-exclusion-are-horizontal-only.md).
        /// </para>
        /// </remarks>
        private static void AddInkExclusions(RGraphics g, CssBox styleSource, IReadOnlyList<DecorationWord> words,
            double cross, double thickness, bool isVertical, List<DecorationInterval> into)
        {
            var half = thickness / 2;
            var maximumClearance = MaximumInkSkipClearanceCssPixels * Length.PointsPerPx * g.PixelsPerPoint;
            var clearance = Math.Min(thickness, maximumClearance);

            var isAuto = styleSource.TextDecorationSkipInk.Value == TextDecorationSkipInk.Auto;

            foreach (var placed in words)
            {
                var word = placed.Word;
                var text = word.FirstLineText ?? word.Text;
                if (string.IsNullOrEmpty(text)) continue;

                // css-text-decor-4 §2.10.5: under 'auto' (UA discretion - "may interrupt"), a UA
                // "should consider the script of the text" and specifically "should refrain" from
                // ink-skipping CJK-script text. 'all' has no such carve-out ("must interrupt"
                // unconditionally), so this only ever short-circuits the auto case.
                if (isAuto && word is CssRectWord { ScriptTag: "hani" or "kana" or "hang" })
                    continue;

                // Exactly the resolution DrawWordGlyphs uses to paint this word: a per-codepoint fallback
                // face or a synthesized small-caps run has its own font and its own baseline shift, and
                // measuring ink against the box's own font instead would name the wrong glyphs at the
                // wrong height. See FragmentPainter.Text.cs.
                var (wordStyle, font, baselineAdjust, features) = ResolveWordFontContext(word, placed.Owner);

                if (isVertical)
                {
                    // Upright text has no natural horizontal layout to measure ink against at all (see
                    // EnumerateUprightGlyphPlacements's own remarks on why an upright run has no single
                    // natural layout the way a rotated one does) - not yet reachable, so left unskipped.
                    if (IsUprightWordOrientation(placed.Owner, word)) continue;

                    var rect = placed.Rect;

                    // The same natural (pre-rotation) origin DrawWordGlyphs's sideways branch hands
                    // DrawString - see AddInkExclusions' own remarks and BuildTextClipPath's
                    // CollectRotatedWord for the identical derivation used to build this word's outline.
                    var naturalOrigin = new RPoint(0, baselineAdjust);

                    // SidewaysRotation maps natural (x, y) to physical (rect.Right - y, rect.Y + x) - so
                    // the natural Y that corresponds to this decoration's physical cross position is
                    // rect.Right - cross, and the band around it inverts the same way a physical band
                    // would (± half the line's own thickness).
                    var naturalCenter = rect.Right - cross;

                    var crossings = g.GetInkCrossings(text, font, naturalOrigin,
                        naturalCenter - half, naturalCenter + half, wordStyle.ActualLetterSpacing, features);

                    if (crossings is null) continue;

                    foreach (var crossing in crossings)
                    {
                        var physicalStart = rect.Y + crossing.Start;
                        var physicalEnd = rect.Y + crossing.End;
                        into.Add(new DecorationInterval(physicalStart, physicalEnd).Dilated(clearance));
                    }

                    continue;
                }

                // The word's own draw origin, not its baseline: DrawWordGlyphs hands DrawString exactly
                // this point, and GetInkCrossings places the baseline from the font's own metrics the way
                // the text-drawing path does. Deriving a baseline here instead would use RFont.Ascent,
                // which is rounded to a whole unit - half the height of a default-thickness band.
                var origin = new RPoint(placed.Rect.X, placed.Rect.Y + baselineAdjust);

                var horizontalCrossings = g.GetInkCrossings(text, font, origin,
                    cross - half, cross + half, wordStyle.ActualLetterSpacing, features);

                if (horizontalCrossings is null) continue;

                foreach (var crossing in horizontalCrossings)
                {
                    into.Add(new DecorationInterval(crossing.Start, crossing.End).Dilated(clearance));
                }
            }
        }

        /// <summary>
        /// Resolves CSS Text Decoration 4 §3.3 <c>text-decoration-thickness</c> to a concrete pen width,
        /// in the same internal pixel space every other geometry value in <see cref="PaintDecoration"/>
        /// already uses. <c>auto</c> (the initial value) deliberately preserves this engine's
        /// pre-existing fixed decoration-line thickness exactly, rather than deriving one from the font,
        /// so a declaration with no <c>text-decoration-thickness</c> at all paints byte-for-byte as it
        /// did before this property existed. A percentage resolves against the element's own font size,
        /// per the spec's own percentage-basis rule for this property (not line-height, unlike
        /// <c>vertical-align</c>'s superficially similar length-or-percentage grammar).
        /// </summary>
        private static double ResolveDecorationThickness(
            CssProperty<CssKeywordOrValue<TextDecorationThicknessKeyword, LengthOrCalc>> thickness,
            CssBox styleSource, double pixelsPerPoint) =>
            ResolveKeywordOrLength(thickness, styleSource, pixelsPerPoint, keyword => keyword switch
            {
                TextDecorationThicknessKeyword.FromFont => styleSource.ActualFont.UnderlineThickness,
                // Auto (the initial value), and any unresolved/global-keyword state - this engine's
                // pre-existing fixed thickness, unchanged.
                _ => 1
            });

        /// <summary>
        /// Resolves css-text-decor-4 §2.8 <c>text-underline-offset</c> to a concrete distance, in the
        /// same internal pixel space every other geometry value in <see cref="PaintDecoration"/> already
        /// uses - the same shape as <see cref="ResolveDecorationThickness"/>. <c>auto</c> (the initial
        /// value) is zero: the offset only ever <i>adds to</i> whatever <c>text-underline-position</c>
        /// already establishes, it never has an independent zero position of its own. A percentage
        /// resolves against the element's own font size ("1em"), per §2.8.
        /// </summary>
        private static double ResolveDecorationOffset(
            CssProperty<CssKeywordOrValue<TextUnderlineOffsetKeyword, LengthOrCalc>> offset,
            CssBox styleSource, double pixelsPerPoint) =>
            ResolveKeywordOrLength(offset, styleSource, pixelsPerPoint, _ => 0); // Auto is zero.

        /// <summary>
        /// The <c>&lt;value&gt; | keyword</c> grammar shape <see cref="ResolveDecorationThickness"/> and
        /// <see cref="ResolveDecorationOffset"/> share: a length/percentage (resolved against the
        /// element's own font size) when a value was authored, otherwise <paramref name="resolveKeyword"/>
        /// decides what each of the property's own keywords means.
        /// </summary>
        private static double ResolveKeywordOrLength<TKeyword>(
            CssProperty<CssKeywordOrValue<TKeyword, LengthOrCalc>> property,
            CssBox styleSource, double pixelsPerPoint, Func<TKeyword, double> resolveKeyword)
            where TKeyword : struct, Enum
        {
            var value = property.Value;

            if (value is { IsValue: true, Value: { } lengthOrCalc })
            {
                var fontSizePx = styleSource.ActualFont.Size * pixelsPerPoint;
                return CssValueParser.ParseLength(lengthOrCalc, fontSizePx, styleSource);
            }

            return resolveKeyword(value.Keyword.GetValueOrDefault());
        }

        /// <summary>
        /// Draws the vertical rule lines between columns of a multi-column container, one segment per
        /// gap per page-row (see <see cref="CssBox.ColumnRuleSegments"/>).
        /// </summary>
        private static void PaintColumnRules(RGraphics g, CssBox box, double originY, RRect clip)
        {
            // `none` and `hidden` draw nothing. DerivedStyle.ActualColumnRuleWidth already zeroes both,
            // and the call site's `> 0` check would therefore keep us out - but the dash-style switch
            // below folds every unhandled style into Solid, so a width that ever reached here non-zero
            // would paint a solid rule. That is exactly what `column-rule: 16pt hidden` used to do
            // before the width was zeroed, so state the rule here too rather than inferring it from a
            // cached width that has no invalidator.
            if (box.ColumnRuleStyle.Value is LineStyle.None or LineStyle.Hidden) return;

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
        /// Draws a <c>border-collapse: collapse</c> table's resolved borders - see
        /// <see cref="CssBox.CollapsedBorderSegments"/> and the call site's own remarks for why this runs
        /// where it does.
        /// </summary>
        private static void PaintCollapsedTableBorders(RGraphics g, CssBox box, double originY, RRect clip)
        {
            // Segments are recorded in document space, same as ColumnRuleSegments - see PaintColumnRules.
            foreach (var segment in box.CollapsedBorderSegments!)
            {
                var visualRect = new RRect(segment.Rect.X, segment.Rect.Y - originY, segment.Rect.Width, segment.Rect.Height);

                if (!IsRectVisible(visualRect, clip)) continue;

                // side: null - a grid line is shared by the boxes on both sides of it and so is not any
                // one box's edge; that is what gives a bevelled segment both of its faces.
                BordersDrawHandler.DrawCollapsedSegment(
                    g, segment.IsHorizontal, visualRect, segment.Style, segment.Color, segment.Width, side: null);
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

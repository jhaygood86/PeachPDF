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

using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Text;
using PeachPDF.Text.Bidi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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

            var matrix = ComputePaintViewportTransform(g, viewportRect, viewBoxX, viewBoxY, viewBoxWidth, viewBoxHeight, document.PreserveAspectRatio);

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
        /// <see cref="ComputeViewportTransform"/>, adjusted for painting through <paramref name="g"/>:
        /// that method's own math (shared with <see cref="CollectLinks"/>, which needs it unmodified)
        /// resolves entirely in <paramref name="g"/>'s own coordinate space, where <paramref name="viewportRect"/>
        /// lives - but <c>viewBoxWidth</c>/<c>viewBoxHeight</c> (and the resulting linear "scale") are
        /// plain SVG user-unit numbers, never scaled by <see cref="RGraphics.PixelsPerPoint"/> the way
        /// <paramref name="viewportRect"/> itself already is. <see cref="RGraphics.PushTransform"/> only
        /// divides a matrix's translation by <c>PixelsPerPoint</c> before handing it to the backend, not
        /// its linear part - correct for an ordinary CSS <c>transform: scale()</c> (already scale-neutral),
        /// wrong for this transform's scale (a ratio of a <c>PixelsPerPoint</c>-scaled length over a
        /// never-scaled one), which would otherwise land <c>PixelsPerPoint</c> times too large (issue
        /// #814: an inline SVG icon's content overflowing its own, correctly-sized clip). Pre-dividing
        /// the linear part here - leaving the translation untouched, since that still wants
        /// <see cref="RGraphics.PushTransform"/>'s own single division - is why this exists as a distinct
        /// helper from <see cref="ComputeViewportTransform"/> rather than a change to it directly.
        /// </summary>
        private static RMatrix ComputePaintViewportTransform(RGraphics g, RRect viewportRect, double viewBoxX, double viewBoxY, double viewBoxWidth, double viewBoxHeight, SvgPreserveAspectRatio par)
        {
            var matrix = ComputeViewportTransform(viewportRect, viewBoxX, viewBoxY, viewBoxWidth, viewBoxHeight, par);
            var pixelsPerPoint = g.PixelsPerPoint;
            return pixelsPerPoint == 1.0
                ? matrix
                : new RMatrix(matrix.M11 / pixelsPerPoint, matrix.M12 / pixelsPerPoint,
                    matrix.M21 / pixelsPerPoint, matrix.M22 / pixelsPerPoint, matrix.OffsetX, matrix.OffsetY);
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
            var matrix = ComputePaintViewportTransform(g, viewportRect, viewBoxX, viewBoxY, viewBoxWidth, viewBoxHeight, par);

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
                var matrix = ComputePaintViewportTransform(g, viewportRect, viewBoxX, viewBoxY, viewBoxWidth, viewBoxHeight, image.PreserveAspectRatio);

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
                var matrix = ComputePaintViewportTransform(g, viewportRect, 0, 0, raster.Width, raster.Height, image.PreserveAspectRatio);

                g.PushClip(viewportRect);
                g.PushTransform(matrix);
                g.DrawImage(raster, new RRect(0, 0, raster.Width, raster.Height));
                g.PopTransform();
                g.PopClip();
            }
        }

        /// <summary>
        /// One addressable character of a <c>&lt;text&gt;</c> subtree during flatten/layout: the glyph, its
        /// owning run (font/paint/anchor) and accumulated opacity, its assigned per-character position/
        /// rotation (null = unset ⇒ flow/inherit), and the layout results (<see cref="Px"/>/<see cref="Py"/>/
        /// <see cref="Advance"/>).
        /// </summary>
        private sealed class GlyphInfo
        {
            /// <summary>Settable (not <c>init</c>) so bidi L4 mirroring can rewrite an RTL glyph's
            /// character to its mirror-image codepoint in place (see <c>ApplyBidiReordering</c>).</summary>
            public required string Glyph { get; set; }

            /// <summary>
            /// This glyph's true logical-order source character, when <see cref="Glyph"/> was rewritten
            /// to its mirror image by <see cref="ApplyBidiReordering"/> (L4) - null (the overwhelming
            /// common case: never mirrored) means <see cref="Glyph"/> itself is already the logical
            /// source. Read by <see cref="PaintGlyphs"/>/<see cref="PaintUprightGlyph"/>/
            /// <see cref="PaintRotatedGlyph"/> to build each painted string's positionally-aligned
            /// ToUnicode logical source (see <c>PeachPDF.Fonts.CMapInfo.AddShapedText</c>'s own remarks
            /// on that contract) - unlike HTML's whole-word reversal, SVG's bidi pass physically reorders
            /// individual <see cref="GlyphInfo"/> instances, so each glyph already carries its own
            /// correct logical value directly; nothing needs recomputing from a run-wide position formula.
            /// </summary>
            public string? LogicalGlyph { get; set; }
            public required SvgTextElement Run { get; init; }
            public required RFont Font { get; init; }
            public double Opacity { get; init; }
            public double? X { get; set; }
            public double? Y { get; set; }
            public double? Dx { get; set; }
            public double? Dy { get; set; }
            public double? Rotate { get; set; }
            public double Px { get; set; }
            public double Py { get; set; }
            public double Advance { get; set; }

            /// <summary>This glyph's own measured size, set once by <see cref="LayoutGlyphs"/> and read
            /// back by <see cref="PaintUprightGlyph"/>/<see cref="PaintRotatedGlyph"/> instead of
            /// re-measuring (real glyph shaping) a second time at paint - the same "measure once, reuse"
            /// discipline <see cref="Advance"/> already follows. Like <see cref="Advance"/>, not
            /// recomputed if bidi mirroring later rewrites <see cref="Glyph"/> (see
            /// <c>ApplyBidiReordering</c>'s own remarks) - a mirror pair's two glyphs are practically
            /// always the same width in any real font.</summary>
            public RSize Size { get; set; }

            /// <summary>Set by <see cref="LayoutGlyphs"/> - whether this glyph paints upright (unrotated)
            /// rather than rotated, under a vertical writing mode. Always false when the text root's
            /// <c>writing-mode</c> is <c>horizontal-tb</c>.</summary>
            public bool IsUpright { get; set; }

            /// <summary>Set by <see cref="LayoutGlyphs"/> for an upright glyph whose font carries a real
            /// <c>VORG</c> table (<see cref="RFont.HasVerticalOrigin"/>, issue #775) - the anchor
            /// correction <see cref="PaintUprightGlyph"/> applies, <c>GetVerticalOriginY(rune) -
            /// Font.Ascent</c>. Zero for a font without a real <c>VORG</c> table, reproducing the plain
            /// top-of-cell anchor exactly.</summary>
            public double OriginYOffset { get; set; }

            /// <summary>Every ancestor run (including <see cref="Run"/> itself) whose own
            /// <c>text-decoration-line</c> is not <c>none</c> and so paints a line across this glyph -
            /// lazily allocated (most text has none). Set by <see cref="FlattenRun"/>; survives
            /// <see cref="ApplyBidiReordering"/>'s physical list reordering automatically, since that
            /// moves <see cref="GlyphInfo"/> instances themselves, not indices into a separate array.</summary>
            public List<SvgTextElement>? Decorators { get; set; }
        }

        /// <summary>
        /// Renders a whole <c>&lt;text&gt;</c> element: its subtree is flattened to an addressable-character
        /// stream (SVG 1.1 §10.4), laid out (per-character x/y/dx/dy/rotate lists, text chunks, per-chunk
        /// <c>text-anchor</c>), and painted - consecutive same-run, unrotated, in-flow characters as one
        /// selectable <see cref="RGraphics.DrawString(string, RFont, RColor, RPoint, RSize, double, RFontPalette?, TextShapingFeatures?)"/>, anything positioned/rotated/gradient/stroked per
        /// glyph. A <c>&lt;textPath&gt;</c> descendant lays out independently along its path.
        /// </summary>
        private static void RenderText(RGraphics g, SvgDocument document, SvgTextElement text, double opacity)
        {
            var glyphs = new List<GlyphInfo>();
            var textPaths = new List<(SvgTextElement Run, double ParentOpacity)>();
            var overrides = new List<BidiIsolateOverride>();
            FlattenRun(text, 1.0, glyphs, textPaths, overrides);

            if (glyphs.Count > 0)
            {
                // writing-mode is resolved once from the <text> root, not per descendant run: unlike
                // text-orientation (genuinely meaningful per nested <tspan>, see IsUprightGlyph), a
                // change of pen-advance axis mid-text has no defined real-world meaning to preserve.
                var isVertical = IsVerticalWritingMode(text.WritingMode);
                LayoutGlyphs(g, glyphs, isVertical);
                ApplyBidiReordering(text, glyphs, overrides, isVertical);
                PaintGlyphs(g, document, glyphs, opacity, isVertical);

                // v1 scope: horizontal-tb, straight-baseline text only - see PaintTextDecorations' own
                // remarks on why vertical writing modes and <textPath> (RenderTextPath, a separate
                // method entirely) are excluded for now.
                if (!isVertical)
                    PaintTextDecorations(g, glyphs, opacity);
            }

            // A <textPath> positions itself entirely along its path; render each in document order after
            // the straight-baseline glyphs (mixing straight text and a textPath in one <text> is rare).
            foreach (var (run, parentOpacity) in textPaths)
                RenderTextPath(g, document, run, opacity * parentOpacity);
        }

        /// <summary>
        /// Real UAX#9 resolution (<see cref="BidiResolver"/>) for one <c>&lt;text&gt;</c> element's
        /// flattened character stream, matching how CSS text integrates bidi (CSS Writing Modes Level 3
        /// §5.2) - SVG text is defined to follow the same <c>direction</c>/<c>unicode-bidi</c> properties
        /// and the same algorithm (SVG 2 §11.3.1). Must run <b>after</b> <see cref="LayoutGlyphs"/>, not
        /// before: <see cref="LayoutGlyphs"/> starts a new text chunk wherever it sees an explicit
        /// <c>x</c>/<c>y</c> - always true of a chunk's own first (logical-order) glyph, from the
        /// element's own <c>x</c>/<c>y</c> attribute - so reordering the list first would carry that
        /// marker to a different list position and fool it into starting a spurious new chunk. Each run's
        /// glyphs are repositioned by reflecting the run about its own content span - each glyph's own
        /// <see cref="GlyphInfo.Advance"/> and its own offset from the run's start (the same fix
        /// <c>CssLayoutEngine.ApplyBidiReordering</c> needed for HTML) - rather than reusing the
        /// logical-order position <see cref="LayoutGlyphs"/> assigned to whichever glyph used to occupy
        /// that list index: that only produced correct output when every glyph in a run happened to
        /// share the same advance, and overlapped or gapped otherwise. Reorders along whichever axis is
        /// the pen's own advance axis (<see cref="GlyphInfo.Px"/> for horizontal-tb,
        /// <see cref="GlyphInfo.Py"/> for a vertical writing mode) - the cross axis is never reassigned
        /// by reordering, since it already belongs to its own glyph, not to a list position.
        /// </summary>
        private static void ApplyBidiReordering(SvgTextElement text, List<GlyphInfo> glyphs, List<BidiIsolateOverride> overrides, bool isVertical)
        {
            var paragraphText = string.Concat(glyphs.Select(gi => gi.Glyph));
            var direction = Map.DirectionModes.GetValueOrDefault(text.Direction, DirectionMode.Ltr) == DirectionMode.Rtl
                ? BidiParagraphDirection.Rtl
                : BidiParagraphDirection.Ltr;

            // paragraphText is a UTF-16 string (a surrogate pair - any astral character, e.g. U+10800
            // and above - is two code units), but glyphs is exactly one GlyphInfo per Rune (FlattenRun),
            // so paragraphText.Length can exceed glyphs.Count. BidiResolver.Resolve returns one level
            // per UTF-16 code unit of its input, and overrides (built by FlattenRun) are expressed in
            // glyph ordinals - both need translating against a per-glyph UTF-16 start-offset map before/
            // after crossing into BidiResolver's own code-unit-indexed world, or everything from the
            // first astral character onward misindexes (issue #555). The equivalent HTML path never hits
            // this because it keys levels to UTF-16 string indices consistently throughout, never
            // re-indexing into a separately-counted glyph list.
            var utf16Starts = new int[glyphs.Count + 1];
            for (var i = 0; i < glyphs.Count; i++)
                utf16Starts[i + 1] = utf16Starts[i] + glyphs[i].Glyph.Length;

            var codeUnitOverrides = overrides.Count == 0
                ? overrides
                : overrides.Select(o => o with
                {
                    Start = utf16Starts[o.Start],
                    Length = utf16Starts[o.Start + o.Length] - utf16Starts[o.Start],
                }).ToList();

            var result = BidiResolver.Resolve(paragraphText, direction, codeUnitOverrides);

            var glyphLevels = new byte[glyphs.Count];
            for (var i = 0; i < glyphs.Count; i++)
                glyphLevels[i] = result.Levels[utf16Starts[i]];

            var runs = BidiResolver.ReorderLine(glyphLevels, 0, glyphs.Count);

            if (runs.Count == 1 && !runs[0].IsRtl) return;

            // Reordering operates along whichever axis LayoutGlyphs actually advanced the pen on - Px
            // for horizontal-tb, Py for vertical-rl/vertical-lr (see LayoutGlyphs's own remarks); the
            // cross axis is untouched by reordering either way, same as GlyphInfo.Py is never reassigned
            // in the horizontal case below.
            Func<GlyphInfo, double> getPos = isVertical ? gi => gi.Py : gi => gi.Px;
            Action<GlyphInfo, double> setPos = isVertical ? (gi, v) => gi.Py = v : (gi, v) => gi.Px = v;

            var originalPos = glyphs.Select(getPos).ToList();
            var runNewStart = originalPos[0];

            var reordered = new List<GlyphInfo>(glyphs.Count);
            foreach (var run in runs)
            {
                var runOldStart = originalPos[run.Start];
                var lastIndexInRun = run.Start + run.Length - 1;
                var runContentWidth = originalPos[lastIndexInRun] + glyphs[lastIndexInRun].Advance - runOldStart;

                if (run.IsRtl)
                {
                    for (var k = run.Length - 1; k >= 0; k--)
                    {
                        var idx = run.Start + k;
                        var gi = glyphs[idx];
                        if (System.Text.Rune.DecodeFromUtf16(gi.Glyph, out var rune, out _) == System.Buffers.OperationStatus.Done
                            && BidiMirroring.TryGetMirror(rune.Value, out var mirrored))
                        {
                            // The pre-mirror value is this glyph's true logical-order source - captured
                            // before Glyph itself is overwritten below.
                            gi.LogicalGlyph = gi.Glyph;
                            gi.Glyph = char.ConvertFromUtf32(mirrored);
                            // LayoutGlyphs classified IsUpright from the pre-mirror codepoint; a mirror
                            // pair could in principle have differing Vertical_Orientation classes (most
                            // real mirror pairs - brackets, parens - don't, but nothing guarantees it),
                            // so re-classify against what's actually going to be painted.
                            gi.IsUpright = isVertical && IsUprightGlyph(gi);

                            // Same reasoning for OriginYOffset (issue #775): it was only ever computed
                            // for a pre-mirror upright glyph against the pre-mirror codepoint, so an
                            // IsUpright reclassification above needs a fresh VORG lookup against the
                            // mirrored codepoint too - unlike Advance/Size, which deliberately stay
                            // stale across mirroring (their own remarks - "a mirror pair's two glyphs
                            // are practically always the same width"), a newly-upright glyph's offset
                            // was never computed at all, not just outdated, so leaving it would silently
                            // drop real VORG positioning for exactly the reordering case this file
                            // already re-derives IsUpright to handle.
                            gi.OriginYOffset = gi.IsUpright && gi.Font.HasVerticalOrigin
                                ? gi.Font.GetVerticalOriginY(new System.Text.Rune(mirrored)) - gi.Font.Ascent
                                : 0;
                        }

                        var offsetFromRunStart = originalPos[idx] - runOldStart;
                        setPos(gi, runNewStart + runContentWidth - (offsetFromRunStart + gi.Advance));
                        reordered.Add(gi);
                    }
                }
                else
                {
                    for (var k = 0; k < run.Length; k++)
                    {
                        var idx = run.Start + k;
                        var gi = glyphs[idx];
                        setPos(gi, runNewStart + (originalPos[idx] - runOldStart));
                        reordered.Add(gi);
                    }
                }

                runNewStart += runContentWidth;
            }

            foreach (var gi in reordered)
            {
                // X/Y/Dx/Dy already did their one job - marking a logical-order chunk start for
                // LayoutGlyphs's pen-advance algorithm, now fully resolved into Px above. Left in place,
                // they would misdirect PaintGlyphs's own merge-adjacency check (which treats any
                // "explicitly positioned" glyph as its own paint call) into breaking a visually
                // contiguous run apart at whichever glyph originally carried the element's own explicit
                // x/y - typically the chunk's first LOGICAL character, which after an RTL reorder is no
                // longer first in the visual sequence PaintGlyphs actually walks.
                gi.X = null;
                gi.Y = null;
                gi.Dx = null;
                gi.Dy = null;
            }

            glyphs.Clear();
            glyphs.AddRange(reordered);
        }

        /// <summary>
        /// Flattens a run's subtree into <paramref name="glyphs"/> in document order (a <c>&lt;textPath&gt;</c>
        /// descendant is collected into <paramref name="textPaths"/> instead), then assigns this run's own
        /// per-character position lists to the characters it contributed - innermost-wins, since a nested
        /// run's own <see cref="FlattenRun"/> runs (and assigns) before this outer assignment. A run whose
        /// own <c>unicode-bidi</c> isn't <c>normal</c> contributes a synthetic explicit push
        /// (<see cref="CssUnicodeBidiMapping"/>) over the glyph range it (including its own descendants)
        /// contributed, appended to <paramref name="overrides"/> after recursing into its children so a
        /// shared start index nests outer-before-inner (see <c>BidiResolver.Resolve</c>'s own handling of
        /// multiple overrides sharing an end index).
        /// <paramref name="opacityFactor"/> is the product of the run-chain's <c>opacity</c> below the root
        /// <c>&lt;text&gt;</c> (whose own opacity is already folded into the caller's base opacity).
        /// </summary>
        private static void FlattenRun(SvgTextElement run, double opacityFactor, List<GlyphInfo> glyphs, List<(SvgTextElement, double)> textPaths, List<BidiIsolateOverride> overrides)
        {
            var startIndex = glyphs.Count;

            foreach (var item in run.Content)
            {
                switch (item)
                {
                    case SvgTextFragment fragment when run.Font is { } font:
                        foreach (var rune in fragment.Text.EnumerateRunes())
                            glyphs.Add(new GlyphInfo { Glyph = rune.ToString(), Run = run, Font = font, Opacity = opacityFactor });
                        break;

                    case SvgTextSpan span when span.Run.PathData is not null:
                        textPaths.Add((span.Run, opacityFactor));
                        break;

                    case SvgTextSpan span:
                        FlattenRun(span.Run, opacityFactor * span.Run.Opacity, glyphs, textPaths, overrides);
                        break;
                }
            }

            var contributedLength = glyphs.Count - startIndex;
            if (contributedLength > 0 && run.UnicodeBidi != "normal")
            {
                var unicodeBidi = Map.UnicodeModes.GetValueOrDefault(run.UnicodeBidi, UnicodeMode.Normal);
                var runDirection = Map.DirectionModes.GetValueOrDefault(run.Direction, DirectionMode.Ltr);
                foreach (var push in CssUnicodeBidiMapping.MapToPushes(unicodeBidi, runDirection))
                    overrides.Add(new BidiIsolateOverride(startIndex, contributedLength, push));
            }

            if (contributedLength > 0 && run.TextDecorationLine != "none")
            {
                for (var k = startIndex; k < glyphs.Count; k++)
                    (glyphs[k].Decorators ??= []).Add(run);
            }

            AssignPositionLists(run, glyphs, startIndex);
        }

        /// <summary>Assigns <paramref name="run"/>'s x/y/dx/dy/rotate lists to the characters it contributed (from <paramref name="startIndex"/>) by subtree-relative index; <c>rotate</c>'s last value persists for the remaining characters. Only fills slots a nested run hasn't already claimed (innermost-wins).</summary>
        private static void AssignPositionLists(SvgTextElement run, List<GlyphInfo> glyphs, int startIndex)
        {
            var count = glyphs.Count - startIndex;
            for (var k = 0; k < count; k++)
            {
                var gi = glyphs[startIndex + k];
                if (run.XList is { } xl && k < xl.Length) gi.X ??= xl[k];
                if (run.YList is { } yl && k < yl.Length) gi.Y ??= yl[k];
                if (run.DxList is { } dxl && k < dxl.Length) gi.Dx ??= dxl[k];
                if (run.DyList is { } dyl && k < dyl.Length) gi.Dy ??= dyl[k];
                if (run.RotateList is { Length: > 0 } rl) gi.Rotate ??= k < rl.Length ? rl[k] : rl[^1];
            }
        }

        /// <summary>
        /// Lays out the flattened character stream: advances a pen along the writing mode's own inline
        /// (pen-advance) axis - X for <c>horizontal-tb</c>, Y for <c>vertical-rl</c>/<c>vertical-lr</c> -
        /// applying each character's absolute x/y on that axis (starting a new text chunk) and relative
        /// dx/dy on both axes, then shifts each chunk by its start run's <c>text-anchor</c> over the
        /// chunk's own extent along that same axis. <paramref name="isVertical"/> is resolved once from
        /// the <c>&lt;text&gt;</c> root (see <see cref="RenderText"/>), not per glyph: unlike
        /// <c>text-orientation</c> (see <see cref="IsUprightGlyph"/>), the pen-advance axis itself has no
        /// defined meaning changing mid-text.
        /// </summary>
        private static void LayoutGlyphs(RGraphics g, List<GlyphInfo> glyphs, bool isVertical)
        {
            double penX = 0, penY = 0;
            var chunkStarts = new List<int> { 0 };

            for (var i = 0; i < glyphs.Count; i++)
            {
                var gi = glyphs[i];
                gi.IsUpright = isVertical && IsUprightGlyph(gi);

                if (isVertical)
                {
                    // The inline (chunk-advance) axis is Y; X is the cross axis (glyph position across
                    // the column), the vertical-writing-mode counterpart of horizontal-tb's own roles
                    // below.
                    if (gi.Y is { } gy)
                    {
                        penY = gy;
                        if (i > 0)
                            chunkStarts.Add(i);
                    }
                    if (gi.X is { } gx)
                        penX = gx;

                    penX += gi.Dx ?? 0;
                    penY += gi.Dy ?? 0;

                    gi.Px = penX;
                    gi.Py = penY;

                    // Always measured (not just for the rotated branch below): PaintUprightGlyph's own
                    // cross-axis centering needs this same width too, cached on GlyphInfo.Size so paint
                    // reads it back instead of re-measuring (real glyph shaping) a second time - see
                    // GlyphInfo.Size's own remarks.
                    var measured = g.MeasureString(gi.Glyph, gi.Font, gi.Run.ShapingFeatures);
                    gi.Size = measured;

                    // An upright glyph's down-the-column advance is its real vmtx advance height when
                    // its font carries real OpenType vertical metrics (issue #770), same as
                    // CssLayoutEngine.NaturalWordSize's own upright branch. Otherwise it falls back to
                    // the font's own line height, not its measured width (RGraphics.DrawString always
                    // renders a glyph across the font's full line-height span regardless of that glyph's
                    // own narrower advance width - see NaturalWordSize's remarks for the visual-overlap
                    // failure mode this avoids). A rotated glyph's down-the-column footprint is its own
                    // natural (pre-rotation) width, the same swap FragmentPainter.Text.cs's
                    // SidewaysRotation performs for HTML - untouched by real vertical metrics, since
                    // rotated/sideways orientation is spec-correct using rotated horizontal metrics.
                    if (gi.IsUpright)
                    {
                        // HasVerticalMetrics (vhea/vmtx, advance) and HasVerticalOrigin (a real VORG
                        // table specifically, issue #775) are independent capabilities - a CFF font can
                        // carry a real VORG table without vhea/vmtx (VORG doesn't require them), so the
                        // origin check must not be nested inside the metrics one, matching
                        // FragmentPainter.Text.cs's PaintUprightVerticalRun (HTML) exactly; nesting it
                        // here previously meant such a font's real per-glyph origin was silently dropped
                        // whenever it lacked vhea/vmtx.
                        var rune = System.Text.Rune.GetRuneAt(gi.Glyph, 0);
                        gi.Advance = gi.Font.HasVerticalMetrics ? gi.Font.GetVerticalAdvance(rune) : gi.Font.Height;
                        if (gi.Font.HasVerticalOrigin)
                            gi.OriginYOffset = gi.Font.GetVerticalOriginY(rune) - gi.Font.Ascent;
                    }
                    else
                    {
                        gi.Advance = measured.Width;
                    }

                    gi.Advance += gi.Run.LetterSpacing;
                    if (gi.Run.WordSpacing != 0 && IsWhitespaceGlyph(gi.Glyph))
                        gi.Advance += gi.Run.WordSpacing;

                    penY += gi.Advance;
                }
                else
                {
                    if (gi.X is { } gx)
                    {
                        penX = gx;
                        if (i > 0)
                            chunkStarts.Add(i);
                    }
                    if (gi.Y is { } gy)
                        penY = gy;

                    penX += gi.Dx ?? 0;
                    penY += gi.Dy ?? 0;

                    gi.Px = penX;
                    gi.Py = penY;
                    gi.Size = g.MeasureString(gi.Glyph, gi.Font, gi.Run.ShapingFeatures);
                    gi.Advance = gi.Size.Width + gi.Run.LetterSpacing;
                    if (gi.Run.WordSpacing != 0 && IsWhitespaceGlyph(gi.Glyph))
                        gi.Advance += gi.Run.WordSpacing;

                    penX += gi.Advance;
                }
            }

            for (var c = 0; c < chunkStarts.Count; c++)
            {
                var start = chunkStarts[c];
                var end = c + 1 < chunkStarts.Count ? chunkStarts[c + 1] : glyphs.Count;

                var anchor = glyphs[start].Run.TextAnchor;
                if (anchor == SvgTextAnchor.Start)
                    continue;

                if (isVertical)
                {
                    var extent = glyphs[end - 1].Py + glyphs[end - 1].Advance - glyphs[start].Py;
                    var shift = anchor == SvgTextAnchor.Middle ? -extent / 2 : -extent;

                    for (var i = start; i < end; i++)
                        glyphs[i].Py += shift;
                }
                else
                {
                    var extent = glyphs[end - 1].Px + glyphs[end - 1].Advance - glyphs[start].Px;
                    var shift = anchor == SvgTextAnchor.Middle ? -extent / 2 : -extent;

                    for (var i = start; i < end; i++)
                        glyphs[i].Px += shift;
                }
            }
        }

        /// <summary>Resolves the pen-advance/inline axis for a <c>&lt;text&gt;</c> root's <c>writing-mode</c> -
        /// true for <c>vertical-rl</c>/<c>vertical-lr</c>, false otherwise (including <c>sideways-rl</c>/
        /// <c>sideways-lr</c> and any unrecognized value, matching the HTML pipeline's own scope).</summary>
        private static bool IsVerticalWritingMode(WritingMode writingMode) => writingMode is WritingMode.VerticalRl or WritingMode.VerticalLr;

        /// <summary>
        /// Whether one glyph paints upright (unrotated) under a vertical writing mode: <c>upright</c>/
        /// <c>sideways</c> force one answer for every glyph on <paramref name="gi"/>'s own run (each
        /// nested <c>&lt;tspan&gt;</c> can genuinely override <c>text-orientation</c>, unlike
        /// <c>writing-mode</c> - see <see cref="LayoutGlyphs"/>'s own remarks); <c>mixed</c> (the
        /// default) classifies the glyph's own single codepoint by Unicode's Vertical_Orientation
        /// property, the same <see cref="VerticalOrientationTable"/> the HTML pipeline shares.
        /// </summary>
        private static bool IsUprightGlyph(GlyphInfo gi) => gi.Run.TextOrientation switch
        {
            TextOrientation.Upright => true,
            TextOrientation.Sideways => false,
            _ => System.Text.Rune.DecodeFromUtf16(gi.Glyph, out var rune, out _) == System.Buffers.OperationStatus.Done
                 && VerticalOrientationTable.IsEffectivelyUpright(rune)
        };

        /// <summary>Whether <paramref name="glyph"/> (one <see cref="System.Text.Rune"/>-worth of
        /// text, per <see cref="GlyphInfo"/>'s own per-character granularity) is whitespace - the same
        /// test <c>TextWhitespaceState.Collapse</c> uses, so <c>word-spacing</c> targets exactly the
        /// space characters that survive collapsing.</summary>
        private static bool IsWhitespaceGlyph(string glyph) => glyph.Length > 0 && char.IsWhiteSpace(glyph[0]);

        /// <summary>
        /// Paints the laid-out character stream. Under <c>horizontal-tb</c>: a maximal contiguous group
        /// of same-run, unrotated, in-flow characters is painted as one <see cref="RGraphics.DrawString(string, RFont, RColor, RPoint, RSize, double, RFontPalette?, TextShapingFeatures?)"/>
        /// (kept selectable); an explicitly-rotated character is painted on its own, rotated about its
        /// own position (<see cref="PaintRotatedGlyph"/>). Under a vertical writing mode, every glyph
        /// paints individually - never batched into one string - since consecutive upright glyphs stack
        /// down the column rather than running side by side (<see cref="PaintUprightGlyph"/>), and a
        /// glyph classified rotated (or forced <c>sideways</c>) reuses the identical
        /// <see cref="PaintRotatedGlyph"/> mechanism explicit <c>rotate=""</c> already uses, just at a
        /// default 90° instead of an author-specified angle - an explicit <c>rotate=""</c> still wins
        /// over the orientation-driven default when both apply, matching how <c>rotate=""</c> already
        /// overrides in-flow layout today.
        /// </summary>
        private static void PaintGlyphs(RGraphics g, SvgDocument document, List<GlyphInfo> glyphs, double opacity, bool isVertical)
        {
            var i = 0;
            while (i < glyphs.Count)
            {
                var start = glyphs[i];
                var font = start.Font;

                // Under horizontal-tb, rotate="0" is visually identical to no rotate at all, so it's
                // still left eligible for the batching path below (an unnecessary per-glyph transform
                // push/pop would only cost efficiency/selectability for a no-op angle). Under a vertical
                // writing mode, though, 0 is a meaningful explicit override distinct from "unset" - it's
                // the only way an author can force a rotated-classified glyph to stay unrotated - so any
                // explicit value, including 0, has to take this branch there.
                var explicitRotateOverridesOrientation = isVertical ? start.Rotate.HasValue : (start.Rotate ?? 0) != 0;
                if (explicitRotateOverridesOrientation)
                {
                    PaintRotatedGlyph(g, document, start, font, start.Rotate!.Value, opacity);
                    i++;
                    continue;
                }

                if (isVertical)
                {
                    if (start.IsUpright)
                        PaintUprightGlyph(g, document, start, font, opacity);
                    else
                        PaintRotatedGlyph(g, document, start, font, 90.0, opacity);
                    i++;
                    continue;
                }

                var builder = new StringBuilder(start.Glyph);
                // Built in parallel with builder (same append points, same order) - each position holds
                // this glyph's true logical source (LogicalGlyph when bidi-mirrored it, else Glyph
                // itself/identity), so the result is always positionally aligned with `text` per
                // CMapInfo.AddShapedText's own contract - collapsed to null below when no glyph in this
                // batch was actually mirrored, the overwhelmingly common case.
                var logicalBuilder = new StringBuilder(start.LogicalGlyph ?? start.Glyph);
                i++;

                // word-spacing has no per-character mid-string paint primitive to reuse (unlike
                // letter-spacing, which DrawString/GetTextOutline apply uniformly - see
                // PaintTextGlyphs), so the extra gap after a word-spaced whitespace glyph is made
                // visible by forcing a fresh batch here: the next batch's own Px already reflects
                // the added word-spacing (LayoutGlyphs' pen-advance included it), so starting a new
                // DrawString call exactly there reproduces the gap. A no-word-spacing run (the
                // overwhelmingly common case) never takes this break, so its batching is unchanged.
                // This also has to apply when `start` itself is the word-spaced glyph (e.g. a run
                // boundary lands exactly on a space) - otherwise the gap silently never renders,
                // since nothing downstream re-checks the batch's own first character.
                var startIsWordSpacedWhitespace = start.Run.WordSpacing != 0 && IsWhitespaceGlyph(start.Glyph);
                while (!startIsWordSpacedWhitespace && i < glyphs.Count)
                {
                    var gc = glyphs[i];
                    if (!ReferenceEquals(gc.Run, start.Run) || (gc.Rotate ?? 0) != 0
                        || gc.X is not null || gc.Y is not null || (gc.Dx ?? 0) != 0 || (gc.Dy ?? 0) != 0)
                        break;
                    builder.Append(gc.Glyph);
                    logicalBuilder.Append(gc.LogicalGlyph ?? gc.Glyph);
                    i++;

                    if (start.Run.WordSpacing != 0 && IsWhitespaceGlyph(gc.Glyph))
                        break;
                }

                var text = builder.ToString();
                var logicalText = logicalBuilder.ToString();
                if (logicalText == text) logicalText = null;
                var size = g.MeasureString(text, font, start.Run.ShapingFeatures);
                PaintTextGlyphs(g, document, start.Run, text, font, start.Px, start.Py - font.Ascent, size, opacity * start.Opacity,
                    start.Run.LetterSpacing, start.Run.ShapingFeatures, logicalText);
            }
        }

        /// <summary>
        /// Paints <c>text-decoration-line</c> (underline/overline/line-through) for every run that
        /// requested one, over <paramref name="glyphs"/>' already-laid-out (and, if applicable,
        /// bidi-reordered) horizontal-tb positions. <b>v1 scope</b>: horizontal-tb straight-baseline
        /// text only - a per-glyph-rotated character (an explicit <c>rotate=""</c>, or a vertical
        /// writing mode's own rotated/upright glyphs, which never reach here at all - see
        /// <see cref="RenderText"/>'s own <c>isVertical</c> gate) has no well-defined single decoration
        /// line and is skipped; <c>&lt;textPath&gt;</c> text (laid out and painted entirely by
        /// <see cref="RenderTextPath"/>, a separate method this one is never called from) doesn't get
        /// decorations at all yet. Both are documented, narrower-than-HTML gaps for this first cut, not
        /// silently dropped behavior - see <c>.claude/accepted-gaps</c>.
        ///
        /// For each distinct decorator element (in first-seen order, for stable output) this finds
        /// every maximal run of consecutive, eligible glyphs it decorates that also share one baseline
        /// (<see cref="GlyphInfo.Py"/>) - a decorated span whose baseline shifts (a nested <c>dy</c>)
        /// draws as separate segments rather than one line jumping between baselines - and draws one
        /// line per <c>text-decoration-line</c> keyword the decorator itself requested, using the
        /// decorator's <em>own</em> font metrics (not each individual glyph's), matching how a real
        /// UA keeps decoration thickness/position constant across a span even if a nested element
        /// changes font-size - the same "decorating box" model <c>FragmentPainter.Decorations.cs</c>
        /// documents for HTML, whose exact underline/overline/line-through offset formulas this reuses
        /// verbatim, translated from that file's line-box-top-relative terms into this one's
        /// baseline-relative <c>Py - Ascent</c> convention (see <see cref="PaintTextGlyphs"/>, which
        /// already computes glyph draw origins the same way).
        /// </summary>
        private static void PaintTextDecorations(RGraphics g, List<GlyphInfo> glyphs, double opacity)
        {
            // One forward pass tracking every decorator's currently-open span at once (keyed by
            // decorator, bounded by nesting depth per glyph, not the total distinct-decorator count),
            // instead of rescanning the whole glyph list once per distinct decorator. `order` records
            // first-seen order so multi-decorator output stays stable, matching the original per-
            // decorator draw sequence.
            var order = new List<SvgTextElement>();
            var openStart = new Dictionary<SvgTextElement, GlyphInfo>();
            var openEnd = new Dictionary<SvgTextElement, GlyphInfo>();
            var spans = new Dictionary<SvgTextElement, List<(GlyphInfo Start, GlyphInfo End)>>();

            void Close(SvgTextElement decorator)
            {
                if (!openStart.TryGetValue(decorator, out var start))
                    return;

                if (!spans.TryGetValue(decorator, out var list))
                    spans[decorator] = list = [];
                list.Add((start, openEnd[decorator]));
                openStart.Remove(decorator);
                openEnd.Remove(decorator);
            }

            foreach (var gi in glyphs)
            {
                var active = !gi.IsUpright && (gi.Rotate ?? 0) == 0 ? gi.Decorators : null;

                // Close every currently-open span whose decorator this glyph no longer continues
                // (dropped out of scope, or the baseline moved).
                if (openStart.Count > 0)
                {
                    List<SvgTextElement>? toClose = null;
                    foreach (var decorator in openStart.Keys)
                    {
                        var continues = active is not null && active.Contains(decorator) && gi.Py == openStart[decorator].Py;
                        if (!continues)
                            (toClose ??= []).Add(decorator);
                    }

                    if (toClose is not null)
                        foreach (var decorator in toClose)
                            Close(decorator);
                }

                if (active is null)
                    continue;

                foreach (var decorator in active)
                {
                    if (!openStart.ContainsKey(decorator))
                    {
                        openStart[decorator] = gi;
                        if (!order.Contains(decorator))
                            order.Add(decorator);
                    }

                    openEnd[decorator] = gi;
                }
            }

            foreach (var decorator in openStart.Keys.ToList())
                Close(decorator);

            foreach (var decorator in order)
            {
                if (decorator.Font is not { } font || !spans.TryGetValue(decorator, out var decoratorSpans))
                    continue;

                foreach (var (start, end) in decoratorSpans)
                    DrawDecorationSpan(g, decorator, font, start, end, opacity);
            }
        }

        private static void DrawDecorationSpan(RGraphics g, SvgTextElement decorator, RFont font, GlyphInfo start, GlyphInfo end, double opacity)
        {
            var x1 = start.Px;
            var x2 = end.Px + end.Advance;
            if (x2 <= x1)
                return;

            // currentColor (SvgTreeBuilder resolves an explicit color eagerly, leaving
            // TextDecorationColor null for both "unset" and literal "currentColor") falls back to the
            // decorator's own solid fill - SVG has no separate tracked `color` property the way HTML
            // does, and the text's own fill is the closest available proxy for what a reader perceives
            // as "this text's color".
            var color = decorator.TextDecorationColor
                ?? (decorator.Fill.Kind == SvgPaintKind.Solid ? decorator.Fill.Color : RColor.Black);
            var pen = g.GetPen(ApplyOpacity(color, opacity * decorator.Opacity * decorator.FillOpacity));
            pen.Width = 1;
            pen.DashStyle = TextDecorationStyleMapper.ToDashStyle(decorator.TextDecorationStyle);

            foreach (var line in decorator.TextDecorationLine.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var top = start.Py - font.Ascent;
                double y = line switch
                {
                    "underline" => Math.Round(top + font.UnderlineOffset),
                    "line-through" => top + font.Height / 2,
                    "overline" => top,
                    _ => double.NaN,
                };

                if (double.IsNaN(y))
                    continue;

                g.DrawLine(pen, x1, y, x2, y);
            }
        }

        /// <summary>
        /// Paints one glyph rotated <paramref name="degrees"/> clockwise about its own <c>(Px, Py)</c>
        /// pen position - the mechanism explicit <c>rotate=""</c> has always used (SVG 1.1 §10.7),
        /// reused unchanged for the automatic rotation a <c>text-orientation: mixed</c>-classified
        /// rotated glyph (or a <c>sideways</c>-forced one) gets under a vertical writing mode, at a fixed
        /// 90° instead of an author-specified angle. The positive-degrees-is-clockwise sign convention
        /// matches <c>FragmentPainter.Text.cs</c>'s <c>SidewaysRotation</c> (HTML's equivalent rotation)
        /// so a 90° rotation makes horizontal-reading text run top-to-bottom, the correct sense for
        /// <c>vertical-rl</c>/<c>vertical-lr</c>.
        /// </summary>
        private static void PaintRotatedGlyph(RGraphics g, SvgDocument document, GlyphInfo start, RFont font, double degrees, double opacity)
        {
            var glyphSize = start.Size;
            var radians = degrees * (Math.PI / 180.0);
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            var toOrigin = new RMatrix(1, 0, 0, 1, -start.Px, -start.Py);
            var rotate = new RMatrix(cos, sin, -sin, cos, 0, 0);
            var fromOrigin = new RMatrix(1, 0, 0, 1, start.Px, start.Py);
            g.PushTransform(MultiplyMatrix(MultiplyMatrix(toOrigin, rotate), fromOrigin));
            PaintTextGlyphs(g, document, start.Run, start.Glyph, font, start.Px, start.Py - font.Ascent, glyphSize, opacity * start.Opacity,
                start.Run.LetterSpacing, start.Run.ShapingFeatures, start.LogicalGlyph);
            g.PopTransform();
        }

        /// <summary>
        /// Paints one upright (unrotated) glyph under a vertical writing mode, centered across the
        /// column (<see cref="GlyphInfo.Px"/>'s own cross-axis position) rather than left-aligned to it -
        /// matching CJK vertical typesetting convention, the same choice
        /// <c>FragmentPainter.Text.cs</c>'s <c>PaintUprightVerticalRun</c> makes for HTML.
        /// <see cref="GlyphInfo.Py"/> is this glyph's own down-the-column box top (not a baseline - see
        /// <see cref="LayoutGlyphs"/>'s remarks on how the pen assigns it before advancing), so it needs
        /// no ascent adjustment the way the horizontal/rotated paths' baseline-relative <c>Py</c> does.
        ///
        /// A real <c>vmtx</c> advance is legitimately, routinely *smaller* than the font's line height
        /// (see <c>FragmentPainter.Text.cs</c>'s <c>PaintUprightVerticalRun</c> remarks, the identical
        /// HTML-side situation) - once <see cref="GlyphInfo.Advance"/> reflects real metrics,
        /// <see cref="PaintTextGlyphs"/>'s own "paint a full line-height-tall glyph" behavior would bleed
        /// into whatever paints next down the column unless this glyph's paint is confined to its own
        /// reserved cell, the same clip-per-cell fix that file applies.
        ///
        /// When the font also carries a real <c>VORG</c> table (<see cref="RFont.HasVerticalOrigin"/> -
        /// issue #775), <see cref="GlyphInfo.OriginYOffset"/> (computed in <see cref="LayoutGlyphs"/>,
        /// same derivation as <c>FragmentPainter.Text.cs</c>'s <c>PaintUprightVerticalRun</c> - see its
        /// remarks) nudges the anchor away from the plain top-of-cell position. Added, not subtracted -
        /// matching that file's corrected sign, even though the pre-existing convention here for
        /// baseline-relative paths (<see cref="PaintRotatedGlyph"/>) subtracts <c>font.Ascent</c>; this
        /// path's anchor is a top-of-cell position, not a baseline, so the two aren't the same kind of
        /// offset and shouldn't share a sign by default.
        ///
        /// The clip window is deliberately **not** shifted along with the anchor - see
        /// <c>PaintUprightVerticalRun</c>'s own remarks on why (the unshifted per-cell reservation, not
        /// the origin-adjusted anchor, is what actually prevents bleed into neighboring cells; a
        /// self-consistent <c>VORG</c> table keeps ink inside it by construction). The clip is pushed
        /// whenever either <see cref="RFont.HasVerticalMetrics"/> or <see cref="RFont.HasVerticalOrigin"/>
        /// is true, not just the former: a VORG-shifted anchor can push the painted span past the
        /// reserved cell even when <see cref="GlyphInfo.Advance"/> is the line-height fallback (its "the
        /// advance already equals the full painted span" guarantee assumes an unshifted anchor).
        /// </summary>
        private static void PaintUprightGlyph(RGraphics g, SvgDocument document, GlyphInfo start, RFont font, double opacity)
        {
            var glyphSize = start.Size;
            var drawX = start.Px - glyphSize.Width / 2;
            var y = start.Py + start.OriginYOffset;

            if (font.HasVerticalMetrics || font.HasVerticalOrigin)
            {
                // Only the block (Y) axis needs bounding - the cross axis has no overlap risk to guard
                // against, so this just needs to be generous enough to never itself clip real glyph ink.
                // An actually-unbounded RRect (double.MinValue/MaxValue) breaks under the viewBox-to-
                // viewport transform PushTransform/RenderInto already has active here (the extreme
                // coordinates overflow through that matrix multiply), which silently produced an empty
                // effective clip and made every upright glyph invisible - a finite, merely-generous margin
                // avoids that without reintroducing any real cross-axis clipping risk.
                var crossAxisMargin = Math.Max(glyphSize.Width, font.Size) * 8;
                g.PushClip(new RRect(start.Px - crossAxisMargin, start.Py, crossAxisMargin * 2, start.Advance));
                PaintTextGlyphs(g, document, start.Run, start.Glyph, font, drawX, y, glyphSize, opacity * start.Opacity,
                    start.Run.LetterSpacing, start.Run.ShapingFeatures, start.LogicalGlyph);
                g.PopClip();
            }
            else
            {
                PaintTextGlyphs(g, document, start.Run, start.Glyph, font, drawX, y, glyphSize, opacity * start.Opacity,
                    start.Run.LetterSpacing, start.Run.ShapingFeatures, start.LogicalGlyph);
            }
        }

        /// <summary>
        /// Paints one straight-baseline group of characters (<paramref name="text"/>, all sharing one run's
        /// font/fill/stroke) at a given top-left origin. Plain solid, non-stroked text keeps the fast
        /// <see cref="RGraphics.DrawString(string, RFont, RColor, RPoint, RSize, double, RFontPalette?, TextShapingFeatures?)"/> path (a single-color PDF text show, so it stays
        /// selectable and tagged-PDF-friendly). A gradient/pattern <c>fill</c> or any <c>stroke</c>
        /// needs the glyphs as an addressable vector path (<see cref="RGraphics.GetTextOutline"/>),
        /// filled/stroked through the same brush/pen machinery shapes use - outlined text is vector art
        /// (not selectable). A CFF/bitmap font yields no outline, so it falls back to a solid fill.
        /// <paramref name="logicalText"/> is <paramref name="text"/>'s true logical-order source,
        /// positionally aligned with it (see <c>PeachPDF.Fonts.CMapInfo.AddShapedText</c>'s own remarks) -
        /// null (the common case) when this run of characters was never bidi-mirrored.
        /// </summary>
        private static void PaintTextGlyphs(RGraphics g, SvgDocument document, SvgTextElement run, string text, RFont font, double drawX, double drawY, RSize size, double opacity,
            double letterSpacing = 0, TextShapingFeatures? features = null, string? logicalText = null)
        {
            var hasStroke = run.Stroke.Kind != SvgPaintKind.None && run.StrokeWidth > 0;
            var needsOutline = run.Fill.Kind is SvgPaintKind.GradientRef or SvgPaintKind.PatternRef || hasStroke;

            if (!needsOutline)
            {
                // Fast path: solid fill (or no fill at all) with no stroke.
                if (run.Fill.Kind != SvgPaintKind.Solid)
                    return;

                var solid = ApplyOpacity(run.Fill.Color, opacity * run.FillOpacity);
                g.DrawString(text, font, solid, new RPoint(drawX, drawY), size, letterSpacing, fontPalette: null, features: features, logicalText: logicalText);
                return;
            }

            // DrawString positions from the top-left of the line box; the outline places the baseline
            // directly, so shift down by the ascent. The measured box (top-left drawX/drawY, size) is
            // the objectBoundingBox reference for gradient/pattern paint - SvgGeometryBounds can't
            // measure text statically.
            var baseline = new RPoint(drawX, drawY + font.Ascent);
            var outline = g.GetTextOutline(text, font, baseline, letterSpacing, features);

            if (outline is null)
            {
                // CFF/bitmap font: no glyf outlines. Best-effort solid fill; a gradient/pattern/stroke
                // simply can't be honored here (documented gap).
                if (run.Fill.Kind == SvgPaintKind.Solid)
                    g.DrawString(text, font, ApplyOpacity(run.Fill.Color, opacity * run.FillOpacity), new RPoint(drawX, drawY), size, letterSpacing, fontPalette: null, features: features, logicalText: logicalText);
                return;
            }

            // `size` comes from RGraphics.MeasureString, which (like HTML's own CssBox/CssLayoutEngine
            // word measurement) has no letterSpacing parameter of its own - widen it the same
            // established way those callers do, via CountShapedGlyphs, so the objectBoundingBox
            // reference actually bounds the letter-spaced outline painted below rather than the
            // narrower unspaced advance.
            var spacedWidth = size.Width + (letterSpacing != 0 ? g.CountShapedGlyphs(text, font, features) * letterSpacing : 0);
            var textBounds = new RRect(drawX, drawY, spacedWidth, size.Height);

            // Fill then stroke, matching SVG paint order.
            if (run.Fill.Kind != SvgPaintKind.None)
            {
                if (run.Fill.Kind == SvgPaintKind.PatternRef)
                {
                    PaintPatternFill(g, document, run, outline, opacity * run.FillOpacity, textBounds);
                }
                else
                {
                    var brush = ResolvePaintBrush(g, document, run, run.Fill, opacity * run.FillOpacity, textBounds);
                    if (brush is not null)
                        g.DrawPath(brush, outline);
                }
            }

            if (hasStroke)
            {
                var pen = ResolveStrokePen(g, document, run, opacity * run.StrokeOpacity, textBounds);
                if (pen is not null)
                    g.DrawPath(pen, outline);
            }

            outline.Dispose();
        }

        /// <summary>
        /// Lays a <c>&lt;textPath&gt;</c>'s glyphs along its referenced path (a <c>&lt;path&gt;</c> or a basic
        /// shape): the run's own text and any nested <c>&lt;tspan&gt;</c>s are flattened in document order,
        /// and each glyph is placed at its own midpoint distance along the path (honoring <c>startOffset</c>,
        /// <c>text-anchor</c>, per-character <c>dx</c>/<c>dy</c>/<c>rotate</c>, and <c>side</c>) and rotated
        /// to the path tangent there. A glyph whose midpoint falls off the path is dropped. Each glyph paints
        /// in its own run's font/fill/stroke via <see cref="PaintGlyphAlongPath"/>.
        /// </summary>
        private static void RenderTextPath(RGraphics g, SvgDocument document, SvgTextElement run, double inheritedOpacity)
        {
            if (run.PathData is not { } segments)
                return;

            var geometry = new SvgTextPathGeometry(segments);
            var totalLength = geometry.TotalLength;
            if (totalLength <= 0)
                return;

            var opacity = inheritedOpacity * run.Opacity;

            // Flatten the textPath's own text plus nested <tspan>s (a nested <textPath> is out of scope and
            // dropped). Each glyph carries its owning run (font/paint) and its assigned dx/dy/rotate.
            var glyphs = new List<GlyphInfo>();
            var ignoredTextPaths = new List<(SvgTextElement, double)>();
            var overrides = new List<BidiIsolateOverride>();
            FlattenRun(run, 1.0, glyphs, ignoredTextPaths, overrides);
            if (glyphs.Count == 0)
                return;

            // A <textPath> always flows its glyphs along the path's own tangent, regardless of
            // writing-mode - there is no vertical variant of this layout (out of scope, matching real
            // UA behavior: a vertical <text> containing a <textPath> still lays that descendant out
            // horizontally along the path).
            ApplyBidiReordering(run, glyphs, overrides, isVertical: false);

            double runWidth = 0;
            foreach (var gi in glyphs)
            {
                gi.Advance = g.MeasureString(gi.Glyph, gi.Font, gi.Run.ShapingFeatures).Width + gi.Run.LetterSpacing;
                if (gi.Run.WordSpacing != 0 && IsWhitespaceGlyph(gi.Glyph))
                    gi.Advance += gi.Run.WordSpacing;
                runWidth += gi.Advance;
            }

            var startOffset = (run.StartOffsetIsPercent ? run.StartOffset * totalLength : run.StartOffset)
                + run.TextAnchor switch
                {
                    SvgTextAnchor.Middle => -runWidth / 2,
                    SvgTextAnchor.End => -runWidth,
                    _ => 0,
                };

            var pen = 0.0;
            foreach (var gi in glyphs)
            {
                var extraDx = gi.Dx ?? 0;
                var extraDy = gi.Dy ?? 0;
                var advance = gi.Advance;

                var mid = startOffset + pen + extraDx + advance / 2;
                pen += advance + extraDx;   // dx shifts the current position along the path

                // side="right" reads the path in reverse (measured from the far end, glyphs flipped 180°).
                var distance = run.Side == SvgTextPathSide.Right ? totalLength - mid : mid;

                // A glyph centered off the ends of the path is not rendered.
                if (distance < 0 || distance > totalLength)
                    continue;

                var (px, py, tangentDegrees) = geometry.PointAtLength(distance);
                if (run.Side == SvgTextPathSide.Right)
                    tangentDegrees += 180;

                var tangentRad = tangentDegrees * (Math.PI / 180.0);
                var tangentCos = Math.Cos(tangentRad);
                var tangentSin = Math.Sin(tangentRad);

                // dy offsets the glyph perpendicular to the path (along the normal).
                var offsetX = px - tangentSin * extraDy;
                var offsetY = py + tangentCos * extraDy;

                // The glyph frame rotates to the tangent plus any per-character rotate, then translates.
                var glyphRad = (tangentDegrees + (gi.Rotate ?? 0)) * (Math.PI / 180.0);
                var frame = MultiplyMatrix(
                    new RMatrix(Math.Cos(glyphRad), Math.Sin(glyphRad), -Math.Sin(glyphRad), Math.Cos(glyphRad), 0, 0),
                    new RMatrix(1, 0, 0, 1, offsetX, offsetY));

                var hasStroke = gi.Run.Stroke.Kind != SvgPaintKind.None && gi.Run.StrokeWidth > 0;
                var needsOutline = gi.Run.Fill.Kind is SvgPaintKind.GradientRef or SvgPaintKind.PatternRef || hasStroke;

                g.PushTransform(frame);
                PaintGlyphAlongPath(g, document, gi.Run, gi.Font, gi.Glyph, advance, opacity * gi.Opacity, needsOutline, hasStroke, gi.LogicalGlyph);
                g.PopTransform();
            }
        }

        /// <summary>Paints one glyph of a <c>&lt;textPath&gt;</c> at the current (already rotated/translated) frame, centered on the local origin. <paramref name="logicalGlyph"/> is <paramref name="glyph"/>'s true logical-order source when bidi-mirrored it (see <c>PeachPDF.Fonts.CMapInfo.AddShapedText</c>'s own remarks) - null (the common case) otherwise.</summary>
        private static void PaintGlyphAlongPath(RGraphics g, SvgDocument document, SvgTextElement run, RFont font, string glyph, double advance, double opacity, bool needsOutline, bool hasStroke, string? logicalGlyph = null)
        {
            var leftX = -advance / 2;
            var glyphSize = g.MeasureString(glyph, font, run.ShapingFeatures);

            if (!needsOutline)
            {
                if (run.Fill.Kind != SvgPaintKind.Solid)
                    return;

                g.DrawString(glyph, font, ApplyOpacity(run.Fill.Color, opacity * run.FillOpacity), new RPoint(leftX, -font.Ascent), glyphSize, letterSpacing: 0, fontPalette: null, features: run.ShapingFeatures, logicalText: logicalGlyph);
                return;
            }

            var outline = g.GetTextOutline(glyph, font, new RPoint(leftX, 0), features: run.ShapingFeatures);
            if (outline is null)
            {
                if (run.Fill.Kind == SvgPaintKind.Solid)
                    g.DrawString(glyph, font, ApplyOpacity(run.Fill.Color, opacity * run.FillOpacity), new RPoint(leftX, -font.Ascent), glyphSize, letterSpacing: 0, fontPalette: null, features: run.ShapingFeatures, logicalText: logicalGlyph);
                return;
            }

            // objectBoundingBox gradient/pattern on a textPath glyph uses the glyph's own local box (an
            // envelope approximation, since the run's straight bbox is meaningless in the rotated frame).
            var bounds = new RRect(leftX, -font.Ascent, glyphSize.Width, glyphSize.Height);

            if (run.Fill.Kind != SvgPaintKind.None)
            {
                if (run.Fill.Kind == SvgPaintKind.PatternRef)
                {
                    PaintPatternFill(g, document, run, outline, opacity * run.FillOpacity, bounds);
                }
                else
                {
                    var brush = ResolvePaintBrush(g, document, run, run.Fill, opacity * run.FillOpacity, bounds);
                    if (brush is not null)
                        g.DrawPath(brush, outline);
                }
            }

            if (hasStroke)
            {
                var strokePen = ResolveStrokePen(g, document, run, opacity * run.StrokeOpacity, bounds);
                if (strokePen is not null)
                    g.DrawPath(strokePen, outline);
            }

            outline.Dispose();
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
                // objectBoundingBox: map the clipPath's 0..1 child geometry onto the referencing
                // element's bounding box (SVG 1.1 §14.3.5). The clip is built in the element's local space
                // (element.Transform is already pushed above), the same space GetBoundingBox reports, so
                // the mapping is RMatrix(w, 0, 0, h, x, y). A missing/zero bbox falls back to no mapping.
                RMatrix? unitsMatrix = null;
                if (!clipDefinition.ClipPathUnitsUserSpaceOnUse &&
                    SvgGeometryBounds.GetBoundingBox(element) is { Width: > 0, Height: > 0 } bbox)
                {
                    unitsMatrix = new RMatrix(bbox.Width, 0, 0, bbox.Height, bbox.X, bbox.Y);
                }

                clipPath = BuildClipPath(g, clipDefinition, unitsMatrix);

                if (clipPath is not null)
                {
                    g.PushClip(clipPath);
                    pushedClip = true;
                }
            }

            if (element.MaskRef is { } maskRef && document.Masks.TryGetValue(maskRef, out var mask))
                RenderMaskedElementContent(g, document, element, mask, opacity, viewport);
            else if (element.Opacity < 1.0 && NeedsContainerOpacityGroup(element))
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
        /// Which elements need the isolated-transparency-group composite for their own <c>opacity</c>:
        /// containers whose children can overlap. A <c>&lt;g&gt;</c>/<c>&lt;a&gt;</c>
        /// (<see cref="SvgAnchorElement"/> derives from <see cref="SvgGroupElement"/>) or nested
        /// <c>&lt;svg&gt;</c> always qualifies; a <c>&lt;use&gt;</c> qualifies only when it references a
        /// container (<c>&lt;g&gt;</c>/<c>&lt;symbol&gt;</c>/nested <c>&lt;svg&gt;</c>), since a
        /// <c>&lt;use&gt;</c> of a single shape/text/image has no overlapping sub-content and the plain
        /// per-shape alpha multiply is already correct (and cheaper - no tile) for it.
        /// </summary>
        private static bool NeedsContainerOpacityGroup(SvgElement element) => element switch
        {
            SvgGroupElement or SvgNestedSvgElement => true,
            SvgUseElement { Target: SvgGroupElement or SvgSymbolElement or SvgNestedSvgElement } => true,
            _ => false,
        };

        /// <summary>
        /// Renders a container element's (<c>&lt;g&gt;</c>/<c>&lt;a&gt;</c>/nested <c>&lt;svg&gt;</c>, or a
        /// <c>&lt;use&gt;</c> of one - see <see cref="NeedsContainerOpacityGroup"/>) children into an
        /// offscreen tile at full local alpha, then composites that tile onto <paramref name="g"/> as a
        /// single flattened result at <paramref name="element"/>'s own <see cref="SvgElement.Opacity"/> -
        /// the same isolated-transparency-group technique <c>CssBox</c> uses for CSS <c>opacity</c> (see
        /// <c>CssBox.PaintWithOpacity</c>), applied here to fix the double-blend limitation this renderer
        /// previously had for SVG group opacity.
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
            // The bounding box is the element's own local-space extent (the same space the placement rect
            // is interpreted in), with the same -10%/+20% margin SvgMask's own default region uses as a
            // stroke-width/curve-control-point safety margin. GetOpacityGroupBounds extends the geometry-
            // only SvgGeometryBounds to also bound <text>/<image>/nested-<svg>/<use> content, so a group
            // whose only content is those types (previously unboundable) still gets an isolated composite
            // instead of falling back to a double-blend-prone per-shape alpha multiply.
            //
            // Approximation (same as SvgGeometryBounds, which this reuses for objectBoundingBox
            // gradients/masks): a descendant's own `transform` is NOT folded into the bounds, so a child
            // carrying a large translate/scale that pushes its painted geometry outside the untransformed
            // union can be clipped by the raster tile - a pre-existing renderer limitation that applies
            // equally to the boundable-geometry path, mitigated (not eliminated) by the margin. Likewise a
            // <use>-of-a-<use>-of-a-container isn't routed here (NeedsContainerOpacityGroup only unwraps one
            // <use> level), so its target's children fall back to the per-shape multiply.
            if (GetOpacityGroupBounds(g, element, viewport) is not { } bbox || bbox.Width <= 0 || bbox.Height <= 0)
            {
                // Truly empty / zero-area content: nothing paints, so there is nothing to double-blend -
                // a direct render is a harmless no-op.
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
                // No page/document context (a measure-only pass - CreateTile returns null there) - keep
                // the graceful direct fallback rather than throwing. Tested by
                // Opacity_SvgGroupOpacity_NoPageContext_FallsBackToDirectRender.
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
        /// Local-space bounds for sizing an opacity-group tile. Extends the geometry-only
        /// <see cref="SvgGeometryBounds.GetBoundingBox"/> to cover the element types it intentionally
        /// leaves unbounded - <c>&lt;text&gt;</c> (needs font measurement), <c>&lt;image&gt;</c> and
        /// nested <c>&lt;svg&gt;</c> (exact from their own <c>x</c>/<c>y</c>/<c>width</c>/<c>height</c>),
        /// and <c>&lt;use&gt;</c> (mirroring <see cref="RenderElementSwitch"/>'s use handling) - so a
        /// container whose only content is those types is still boundable and gets a proper isolated
        /// composite. Kept renderer-local (not folded into <see cref="SvgGeometryBounds"/>) so
        /// <c>objectBoundingBox</c> gradient/mask/clip resolution, which relies on those types reporting
        /// <c>null</c> there, is unaffected.
        /// </summary>
        private static RRect? GetOpacityGroupBounds(RGraphics g, SvgElement element, (double Width, double Height) viewport) => element switch
        {
            SvgTextElement text => MeasureTextBounds(g, text),
            SvgImageElement { Width: > 0, Height: > 0 } image => new RRect(image.X, image.Y, image.Width, image.Height),
            SvgNestedSvgElement { Width: > 0, Height: > 0 } nestedSvg => new RRect(nestedSvg.X, nestedSvg.Y, nestedSvg.Width, nestedSvg.Height),
            SvgUseElement { Target: SvgSymbolElement } use => new RRect(use.X, use.Y, use.Width ?? viewport.Width, use.Height ?? viewport.Height),
            SvgUseElement { Target: SvgNestedSvgElement nestedTarget } use => new RRect(use.X, use.Y, use.Width ?? nestedTarget.Width, use.Height ?? nestedTarget.Height),
            SvgUseElement { Target: { } target } use => OffsetBounds(GetOpacityGroupBounds(g, target, viewport), use.X, use.Y),
            SvgGroupElement group => UnionOpacityGroupBounds(g, group.Children, viewport),
            _ => SvgGeometryBounds.GetBoundingBox(element),
        };

        private static RRect? OffsetBounds(RRect? rect, double dx, double dy) =>
            rect is { } r ? new RRect(r.X + dx, r.Y + dy, r.Width, r.Height) : null;

        private static RRect? UnionOpacityGroupBounds(RGraphics g, IEnumerable<SvgElement> elements, (double Width, double Height) viewport)
        {
            RRect? result = null;

            foreach (var element in elements)
            {
                if (GetOpacityGroupBounds(g, element, viewport) is not { } b)
                    continue;

                result = result is { } r ? UnionRects(r, b) : b;
            }

            return result;
        }

        private static RRect UnionRects(RRect a, RRect b)
        {
            var minX = Math.Min(a.X, b.X);
            var minY = Math.Min(a.Y, b.Y);
            var maxX = Math.Max(a.X + a.Width, b.X + b.Width);
            var maxY = Math.Max(a.Y + a.Height, b.Y + b.Height);
            return new RRect(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// Local-space bounds of a <c>&lt;text&gt;</c> and its descendants, computed from the exact same
        /// flatten + layout <see cref="RenderText"/> paints with (so the opacity-group tile region and the
        /// painted glyphs can't drift). Each straight glyph contributes its measured box (rotated by its own
        /// per-character <c>rotate</c> about its position); a <c>&lt;textPath&gt;</c> descendant contributes
        /// its flattened path's bbox inflated by the font ascent. The tile's own -10%/+20% margin absorbs slack.
        /// </summary>
        private static RRect? MeasureTextBounds(RGraphics g, SvgTextElement text)
        {
            RRect? result = null;

            var glyphs = new List<GlyphInfo>();
            var textPaths = new List<(SvgTextElement Run, double ParentOpacity)>();
            var overrides = new List<BidiIsolateOverride>();
            FlattenRun(text, 1.0, glyphs, textPaths, overrides);

            if (glyphs.Count > 0)
            {
                var isVertical = IsVerticalWritingMode(text.WritingMode);
                LayoutGlyphs(g, glyphs, isVertical);
                ApplyBidiReordering(text, glyphs, overrides, isVertical);
                foreach (var gi in glyphs)
                {
                    var size = gi.Size;

                    // Mirrors PaintGlyphs's own axis-aware explicit-rotate check exactly - see its
                    // remarks for why rotate="0" only counts as an override under a vertical writing
                    // mode.
                    var explicitRotateOverridesOrientation = isVertical ? gi.Rotate.HasValue : (gi.Rotate ?? 0) != 0;
                    if (explicitRotateOverridesOrientation)
                    {
                        var explicitDegrees = gi.Rotate!.Value;
                        var rotated = new RRect(gi.Px, gi.Py - gi.Font.Ascent, size.Width, size.Height);
                        result = result is { } r1 ? UnionRects(r1, RotateRectBounds(rotated, explicitDegrees, gi.Px, gi.Py)) : RotateRectBounds(rotated, explicitDegrees, gi.Px, gi.Py);
                        continue;
                    }

                    // Matches PaintUprightGlyph/PaintRotatedGlyph's own box shapes exactly - see their
                    // remarks for why Py needs no ascent adjustment in the upright case.
                    var box = isVertical && gi.IsUpright
                        ? new RRect(gi.Px - size.Width / 2, gi.Py, size.Width, gi.Font.Height)
                        : isVertical
                            ? RotateRectBounds(new RRect(gi.Px, gi.Py - gi.Font.Ascent, size.Width, size.Height), 90.0, gi.Px, gi.Py)
                            : new RRect(gi.Px, gi.Py - gi.Font.Ascent, size.Width, size.Height);

                    result = result is { } r ? UnionRects(r, box) : box;
                }
            }

            foreach (var (run, _) in textPaths)
            {
                if (run.PathData is not { } pathSegments || run.Font is not { } pathFont)
                    continue;

                var geometry = new SvgTextPathGeometry(pathSegments);
                if (geometry.IsEmpty)
                    continue;

                var inflate = pathFont.Ascent;
                var pathBox = geometry.Bounds;
                var runBox = new RRect(pathBox.X - inflate, pathBox.Y - inflate, pathBox.Width + 2 * inflate, pathBox.Height + 2 * inflate);
                result = result is { } existing ? UnionRects(existing, runBox) : runBox;
            }

            return result;
        }

        /// <summary>Axis-aligned envelope of <paramref name="rect"/> rotated <paramref name="degrees"/> about (<paramref name="pivotX"/>, <paramref name="pivotY"/>) - matches the pivot <see cref="PaintGlyphs"/> rotates its glyphs around.</summary>
        private static RRect RotateRectBounds(RRect rect, double degrees, double pivotX, double pivotY)
        {
            var radians = degrees * (Math.PI / 180.0);
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

            ReadOnlySpan<(double X, double Y)> corners =
                [(rect.X, rect.Y), (rect.Right, rect.Y), (rect.X, rect.Bottom), (rect.Right, rect.Bottom)];
            foreach (var (cx, cy) in corners)
            {
                var dx = cx - pivotX;
                var dy = cy - pivotY;
                var rx = pivotX + dx * cos - dy * sin;
                var ry = pivotY + dx * sin + dy * cos;
                minX = Math.Min(minX, rx);
                minY = Math.Min(minY, ry);
                maxX = Math.Max(maxX, rx);
                maxY = Math.Max(maxY, ry);
            }

            return new RRect(minX, minY, maxX - minX, maxY - minY);
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
                    RenderText(g, document, text, opacity);
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

        /// <summary>
        /// The bounding box <c>objectBoundingBox</c> paint (gradient/pattern) resolves against. Text
        /// runs pass their measured box as <paramref name="boundsOverride"/> because
        /// <see cref="SvgGeometryBounds.GetBoundingBox"/> can't measure a <c>&lt;text&gt;</c> statically;
        /// every non-text caller passes null and keeps the geometric bounds.
        /// </summary>
        private static RRect? OwnerBounds(SvgElement owner, RRect? boundsOverride)
            => boundsOverride ?? SvgGeometryBounds.GetBoundingBox(owner);

        private static RBrush? ResolvePaintBrush(RGraphics g, SvgDocument document, SvgElement owner, SvgPaint paint, double opacity, RRect? boundsOverride = null)
        {
            return paint.Kind switch
            {
                SvgPaintKind.Solid => g.GetSolidBrush(ApplyOpacity(paint.Color, opacity)),
                SvgPaintKind.GradientRef when paint.ReferenceId is { } id && document.Gradients.TryGetValue(id, out var gradient)
                    => ResolveGradientBrush(g, owner, gradient, opacity, boundsOverride),
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
        private static void PaintPatternFill(RGraphics g, SvgDocument document, SvgElement element, RGraphicsPath path, double opacity, RRect? boundsOverride = null)
        {
            if (element.Fill.ReferenceId is not { } id || !document.Patterns.TryGetValue(id, out var pattern))
                return;

            var (x, y, width, height) = ResolvePatternRect(element, pattern, boundsOverride);
            if (width <= 0 || height <= 0)
                return;

            var tile = g.CreateTile(width, height);
            if (tile is not { } t)
                return;

            RenderViewport(t.Graphics, document, 0, 0, width, height, pattern.ViewBox, pattern.PreserveAspectRatio, pattern.Children, opacity);
            t.Graphics.Dispose();

            var bounds = OwnerBounds(element, boundsOverride) ?? new RRect(x, y, width, height);

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
        private static (double X, double Y, double Width, double Height) ResolvePatternRect(SvgElement owner, SvgPattern pattern, RRect? boundsOverride = null)
        {
            if (pattern.PatternUnitsUserSpaceOnUse)
                return (pattern.X, pattern.Y, pattern.Width, pattern.Height);

            if (OwnerBounds(owner, boundsOverride) is not { } bbox)
                return (pattern.X, pattern.Y, pattern.Width, pattern.Height);

            return (bbox.X + pattern.X * bbox.Width, bbox.Y + pattern.Y * bbox.Height, pattern.Width * bbox.Width, pattern.Height * bbox.Height);
        }

        private static RBrush? ResolveGradientBrush(RGraphics g, SvgElement owner, SvgGradient gradient, double opacity, RRect? boundsOverride = null)
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
                    var (x1, y1) = ResolveGradientPoint(owner, gradient, linear.X1, linear.Y1, boundsOverride);
                    var (x2, y2) = ResolveGradientPoint(owner, gradient, linear.X2, linear.Y2, boundsOverride);
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
                        (p1, p2, stops) = ExpandLinearSpread(owner, p1, p2, stops, reflect, boundsOverride);

                    p1 = ApplyMatrix(p1, gradient.GradientTransform);
                    p2 = ApplyMatrix(p2, gradient.GradientTransform);
                    return g.GetLinearGradientBrush(p1, p2, stops, isRepeating);
                }

                case SvgRadialGradient radial:
                {
                    var (cx, cy) = ResolveGradientPoint(owner, gradient, radial.Cx, radial.Cy, boundsOverride);
                    var (fx, fy) = ResolveGradientPoint(owner, gradient, radial.Fx ?? radial.Cx, radial.Fy ?? radial.Cy, boundsOverride);
                    var r = ResolveGradientRadius(owner, gradient, radial.R, boundsOverride);

                    // Radial counterpart: tiles concentric rings outward from the center to cover the
                    // shape's bounding box, rather than extending along a linear axis.
                    if (isRepeating)
                        (r, stops) = ExpandRadialSpread(owner, new RPoint(cx, cy), r, stops, reflect, boundsOverride);

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
            SvgElement owner, RPoint p1, RPoint p2, (RColor Color, double Position)[] stops, bool reflect, RRect? boundsOverride = null)
        {
            if (stops.Length < 2 || OwnerBounds(owner, boundsOverride) is not { } bbox)
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
            SvgElement owner, RPoint center, double r, (RColor Color, double Position)[] stops, bool reflect, RRect? boundsOverride = null)
        {
            if (stops.Length < 2 || r < 1e-9 || OwnerBounds(owner, boundsOverride) is not { } bbox)
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
        private static (double X, double Y) ResolveGradientPoint(SvgElement owner, SvgGradient gradient, double rawX, double rawY, RRect? boundsOverride = null)
        {
            if (gradient.GradientUnitsUserSpaceOnUse)
                return (rawX, rawY);

            if (OwnerBounds(owner, boundsOverride) is not { } bbox)
                return (rawX, rawY);

            return (bbox.X + rawX * bbox.Width, bbox.Y + rawY * bbox.Height);
        }

        /// <summary>Same as <see cref="ResolveGradientPoint"/> but for a single scalar radius, scaled by the bounding box's spec-defined diagonal formula.</summary>
        private static double ResolveGradientRadius(SvgElement owner, SvgGradient gradient, double rawR, RRect? boundsOverride = null)
        {
            if (gradient.GradientUnitsUserSpaceOnUse)
                return rawR;

            if (OwnerBounds(owner, boundsOverride) is not { } bbox)
                return rawR;

            return rawR * Math.Sqrt((bbox.Width * bbox.Width + bbox.Height * bbox.Height) / 2.0);
        }

        private static RPen? ResolveStrokePen(RGraphics g, SvgDocument document, SvgElement element, double opacity, RRect? boundsOverride = null)
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
                var brush = ResolveGradientBrush(g, element, gradient, opacity, boundsOverride);
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

        private static RGraphicsPath? BuildClipPath(RGraphics g, SvgClipPath clipPath, RMatrix? unitsMatrix)
        {
            var path = g.GetGraphicsPath();
            path.FillMode = clipPath.ClipRule;
            var any = false;

            // unitsMatrix is the objectBoundingBox mapping (0..1 -> referencing element's bbox), or null
            // for userSpaceOnUse. It enters as the ambient matrix so it composes as the OUTER transform of
            // every clip shape (after each shape's own transform), reusing the existing transform baking.
            foreach (var shape in clipPath.Shapes)
                any |= AppendClipShapeGeometry(g, path, shape, unitsMatrix);

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

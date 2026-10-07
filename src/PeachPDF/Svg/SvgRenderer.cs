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

using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Unicode;
using PeachPDF.CSS;
using PeachDrawing.Core;
using PeachDrawing.Core.Geometry;
using PeachPDF.Html.Core.Paint;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Paints a parsed <see cref="SvgDocument"/> into an <see cref="Canvas"/>, mapping its
    /// viewBox onto a target viewport rectangle and walking the scene graph. Whole documents can
    /// be stored as reusable forms by <see cref="RenderCachedInto"/>.
    /// </summary>
    internal static partial class SvgRenderer
    {
        // A form belongs to one PDF document. ConditionalWeakTable also lets a finished PDF and all
        // its cached forms be collected, even though each form refers back to its owning document.
        private static readonly ConditionalWeakTable<object, Dictionary<(SvgDocument Document, double Width,
            double Height, double PixelsPerPoint), Image>> FormCaches = new();

        /// <summary>
        /// Paints a whole SVG from a document-local Form XObject. Position is only a placement concern;
        /// size and PixelsPerPoint affect the artwork rendered into the form. A scene graph is shared
        /// only by identity, since separately built inline SVGs may have different resolved currentColor.
        /// </summary>
        public static void RenderCachedInto(Canvas g, SvgDocument document, Rect viewportRect)
        {
            if (viewportRect.Width <= 0 || viewportRect.Height <= 0)
                return;

            // What a filter reading BackgroundImage shows depends on what was painted behind it, which differs at every placement.
            if (document.ReadsBackdrop)
            {
                RenderInto(g, document, viewportRect);
                return;
            }

            // Recording/measurement graphics have no PDF document to own a form and should still
            // receive the individual drawing calls directly.
            if (g.TileCacheOwner is null)
            {
                RenderInto(g, document, viewportRect);
                return;
            }

            var form = GetOrCreateForm(g, document, viewportRect.Width, viewportRect.Height);
            if (form is not null)
                g.DrawImage(form, viewportRect);
            else
                RenderInto(g, document, viewportRect);
        }

        /// <summary>Returns the reusable SVG artwork tile, or null if this graphics cannot create one.</summary>
        public static Image? GetOrCreateForm(Canvas g, SvgDocument document, double width, double height)
        {
            if (width <= 0 || height <= 0 ||
                (g.TileCacheOwner is not null && (width / g.PixelsPerPoint < 1 || height / g.PixelsPerPoint < 1)))
                return null;

            var owner = g.TileCacheOwner;
            var key = (document, width, height, g.PixelsPerPoint);
            Dictionary<(SvgDocument Document, double Width, double Height, double PixelsPerPoint), Image>? cache =
                owner is null ? null : FormCaches.GetValue(owner, _ => new());
            if (cache is not null && cache.TryGetValue(key, out var existing))
                return existing;

            var tile = g.CreateTile(width, height);
            if (tile is not { } t)
                return null;

            using (t.Graphics)
                RenderInto(t.Graphics, document, new Rect(0, 0, width, height));

            cache?.Add(key, t.Image);
            return t.Image;
        }

        /// <summary>
        /// Clips to <paramref name="viewportRect"/>, pushes the viewBox-to-viewport transform, renders
        /// every root element of <paramref name="document"/>, then pops both. This is the single entry
        /// point used to paint the cached form's content, or to paint directly when no form can be
        /// created. Replaced elements call <see cref="RenderCachedInto"/> instead.
        /// </summary>
        public static void RenderInto(Canvas g, SvgDocument document, Rect viewportRect)
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

            var frame = g.CurrentTransform;
            g.PushClip(viewportRect);
            g.PushTransform(matrix);

            var viewport = (viewBoxWidth, viewBoxHeight);

            var previousBackdrop = SvgBackdropSlot.Get(g);
            if (document.ReadsBackdrop)
            {
                SvgBackdropSlot.Set(g, new SvgBackdropContext(document, PageBackdropFor(document))
                {
                    Frame = frame,
                    ViewportRect = viewportRect,
                    ViewBoxMatrix = matrix,
                    Viewport = viewport,
                });
            }

            try
            {
                foreach (var element in document.Children)
                    RenderElement(g, document, element, 1.0, viewport);
            }
            finally
            {
                SvgBackdropSlot.Set(g, previousBackdrop);
            }

            g.PopTransform();
            g.PopClip();
        }

        /// <summary>
        /// Walks the scene graph purely to compute the final page-space bounding rectangle of every
        /// <c>&lt;a&gt;</c> element's content, for PDF link-annotation registration. Deliberately
        /// separate from <see cref="RenderInto"/>/<see cref="RenderElement"/> - it never touches
        /// <see cref="Canvas"/> (no painting, just matrix composition + bounding-box math), so it's
        /// safe to call exactly once regardless of how many times the document is actually painted
        /// (e.g. once per output page during pagination - painting is a repeated "scroll and repaint"
        /// pass in this renderer, which would make link rectangles collected *during* paint duplicate
        /// once per page). Callers should gather link rectangles from this method's output instead of
        /// hooking into paint at all.
        /// </summary>
        public static void CollectLinks(SvgDocument document, Rect viewportRect, List<(Rect Rect, string Href)> sink)
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

        private static void CollectLinksFromElement(SvgElement element, Matrix3x2 ambientMatrix, List<(Rect Rect, string Href)> sink)
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
                        ? MultiplyMatrix(new Matrix3x2(1, 0, 0, 1, (float)use.X, (float)use.Y), matrix)
                        : matrix;
                    CollectLinksFromElement(target, useMatrix, sink);
                    break;
            }
        }

        /// <summary>Composes two matrices for row-vector point transformation: applies <paramref name="first"/>, then <paramref name="second"/> (i.e. <c>p' = p * first * second</c>).</summary>
        private static Matrix3x2 MultiplyMatrix(Matrix3x2 first, Matrix3x2 second) => first.Then(second);

        /// <summary>
        /// Transforms an axis-aligned local-space rect by <paramref name="matrix"/> and returns the
        /// axis-aligned bounding box of the four transformed corners - needed since an arbitrary
        /// (possibly rotated/skewed) transform doesn't generally preserve axis-alignment. A documented
        /// approximation for a rotated/skewed <c>&lt;a&gt;</c>: PDF link annotations are themselves
        /// always axis-aligned rectangles, so this is the closest any implementation could get anyway.
        /// </summary>
        private static Rect TransformBoundingBox(Rect localBounds, Matrix3x2 matrix)
        {
            var corners = new[]
            {
                ApplyMatrix(new PaintPoint(localBounds.X, localBounds.Y), matrix),
                ApplyMatrix(new PaintPoint(localBounds.X + localBounds.Width, localBounds.Y), matrix),
                ApplyMatrix(new PaintPoint(localBounds.X, localBounds.Y + localBounds.Height), matrix),
                ApplyMatrix(new PaintPoint(localBounds.X + localBounds.Width, localBounds.Y + localBounds.Height), matrix),
            };

            var minX = corners.Min(c => c.X);
            var maxX = corners.Max(c => c.X);
            var minY = corners.Min(c => c.Y);
            var maxY = corners.Max(c => c.Y);

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// <see cref="ComputeViewportTransform"/>, adjusted for painting through <paramref name="g"/>:
        /// that method's own math (shared with <see cref="CollectLinks"/>, which needs it unmodified)
        /// resolves entirely in <paramref name="g"/>'s own coordinate space, where <paramref name="viewportRect"/>
        /// lives - but <c>viewBoxWidth</c>/<c>viewBoxHeight</c> (and the resulting linear "scale") are
        /// plain SVG user-unit numbers, never scaled by <see cref="Canvas.PixelsPerPoint"/> the way
        /// <paramref name="viewportRect"/> itself already is. <see cref="Canvas.PushTransform"/> only
        /// divides a matrix's translation by <c>PixelsPerPoint</c> before handing it to the backend, not
        /// its linear part - correct for an ordinary CSS <c>transform: scale()</c> (already scale-neutral),
        /// wrong for this transform's scale (a ratio of a <c>PixelsPerPoint</c>-scaled length over a
        /// never-scaled one), which would otherwise land <c>PixelsPerPoint</c> times too large (issue
        /// #814: an inline SVG icon's content overflowing its own, correctly-sized clip). Pre-dividing
        /// the linear part here - leaving the translation untouched, since that still wants
        /// <see cref="Canvas.PushTransform"/>'s own single division - is why this exists as a distinct
        /// helper from <see cref="ComputeViewportTransform"/> rather than a change to it directly.
        /// </summary>
        private static Matrix3x2 ComputePaintViewportTransform(Canvas g, Rect viewportRect, double viewBoxX, double viewBoxY, double viewBoxWidth, double viewBoxHeight, SvgPreserveAspectRatio par)
        {
            var matrix = ComputeViewportTransform(viewportRect, viewBoxX, viewBoxY, viewBoxWidth, viewBoxHeight, par);
            var pixelsPerPoint = g.PixelsPerPoint;
            return pixelsPerPoint == 1.0
                ? matrix
                : new Matrix3x2((float)(matrix.M11 / pixelsPerPoint), (float)(matrix.M12 / pixelsPerPoint),
                    (float)(matrix.M21 / pixelsPerPoint), (float)(matrix.M22 / pixelsPerPoint), matrix.M31, matrix.M32);
        }

        /// <summary>
        /// Computes the viewBox-to-viewport transform per <paramref name="par"/>'s alignment and
        /// meet/slice mode. <c>xMidYMid meet</c> (the SVG/CSS default) is a uniform scale, centered,
        /// letterboxed; other alignments shift which edge/corner touches the viewport instead of
        /// centering; <c>slice</c> uses the larger of the two axis scales (overflowing and relying on
        /// the caller's viewport clip) instead of the smaller; <c>none</c> stretches each axis
        /// independently, ignoring aspect ratio.
        /// </summary>
        private static Matrix3x2 ComputeViewportTransform(Rect viewportRect, double viewBoxX, double viewBoxY, double viewBoxWidth, double viewBoxHeight, SvgPreserveAspectRatio par)
        {
            if (par.Align == SvgAlign.None)
            {
                var sx = viewportRect.Width / viewBoxWidth;
                var sy = viewportRect.Height / viewBoxHeight;
                return new Matrix3x2((float)sx, 0, 0, (float)sy, (float)(viewportRect.X - viewBoxX * sx), (float)(viewportRect.Y - viewBoxY * sy));
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

            return new Matrix3x2((float)scale, 0, 0, (float)scale, (float)offsetX, (float)offsetY);
        }

        /// <summary>
        /// Establishes a new nested viewport (for a nested <c>&lt;svg&gt;</c>, or a <c>&lt;symbol&gt;</c>/
        /// nested-<c>&lt;svg&gt;</c> reached through <c>&lt;use&gt;</c>) at local coordinates
        /// (<paramref name="x"/>, <paramref name="y"/>) sized <paramref name="width"/>x<paramref name="height"/>,
        /// then renders <paramref name="children"/> into it - the same viewBox-transform-then-recurse
        /// shape as <see cref="RenderInto"/>, just relative to whatever transform is already active
        /// rather than the page's own initial (identity) transform.
        /// </summary>
        /// <param name="g">The graphics to paint through.</param>
        /// <param name="document">The owning document (gradient/clip/mask/pattern/filter registries for <paramref name="children"/> to resolve against).</param>
        /// <param name="x">Local X of the viewport rectangle.</param>
        /// <param name="y">Local Y of the viewport rectangle.</param>
        /// <param name="width">Width of the viewport rectangle.</param>
        /// <param name="height">Height of the viewport rectangle.</param>
        /// <param name="viewBox">The viewBox mapped onto the viewport rectangle, or null for an identity (viewBox-less) mapping.</param>
        /// <param name="par">Alignment/meet-slice mode for the viewBox-to-viewport mapping.</param>
        /// <param name="children">The content to render into the new viewport.</param>
        /// <param name="opacity">Accumulated ancestor opacity to multiply into <paramref name="children"/>'s own.</param>
        /// <param name="contextElement">
        /// The <c>&lt;use&gt;</c> reaching this viewport as a <c>&lt;symbol&gt;</c>/nested-<c>&lt;svg&gt;</c> target, when
        /// that's how it's being rendered - null for every other caller (a directly-authored nested <c>&lt;svg&gt;</c>, a
        /// <c>&lt;marker&gt;</c>, a <c>&lt;pattern&gt;</c>). When given, this viewport's own children's frame (the viewBox-
        /// to-viewport <c>matrix</c> composed with the ambient transform active here) is recorded under it in
        /// <see cref="s_paintContextFrames"/> for the duration of <paramref name="children"/>'s paint - the same mechanism
        /// the plain-element <c>RenderElementSwitch</c> arm already uses for its own target, letting <see cref="ContextBounds"/>
        /// map a gradient/pattern context paint that reaches into <paramref name="children"/> out of the (pre-mapping)
        /// frame <see cref="SvgGeometryBounds.GetUseTargetBoundingBox"/> reports its box in.
        /// </param>
        private static void RenderViewport(Canvas g, SvgDocument document, double x, double y, double width, double height, Rect? viewBox, SvgPreserveAspectRatio par, IReadOnlyList<SvgElement> children, double opacity, SvgElement? contextElement = null)
        {
            if (width <= 0 || height <= 0)
                return;

            var viewBoxWidth = viewBox?.Width ?? width;
            var viewBoxHeight = viewBox?.Height ?? height;

            if (viewBoxWidth <= 0 || viewBoxHeight <= 0)
                return;

            var viewBoxX = viewBox?.X ?? 0;
            var viewBoxY = viewBox?.Y ?? 0;
            var viewportRect = new Rect(x, y, width, height);
            var matrix = ComputePaintViewportTransform(g, viewportRect, viewBoxX, viewBoxY, viewBoxWidth, viewBoxHeight, par);

            var hadOuterFrame = false;
            var outerFrame = default(Matrix3x2);
            if (contextElement is not null)
            {
                hadOuterFrame = s_paintContextFrames.TryGetValue(contextElement, out outerFrame);
                s_paintContextFrames[contextElement] = MultiplyMatrix(matrix, g.CurrentTransform);
            }

            var pushedClip = false;
            var pushedTransform = false;

            try
            {
                g.PushClip(viewportRect);
                pushedClip = true;
                g.PushTransform(matrix);
                pushedTransform = true;

                var nestedViewport = (viewBoxWidth, viewBoxHeight);
                foreach (var child in children)
                    RenderElement(g, document, child, opacity, nestedViewport);
            }
            finally
            {
                if (pushedTransform) g.PopTransform();
                if (pushedClip) g.PopClip();

                if (contextElement is not null)
                {
                    if (hadOuterFrame) s_paintContextFrames[contextElement] = outerFrame;
                    else s_paintContextFrames.Remove(contextElement);
                }
            }
        }

        /// <summary>
        /// Renders an <c>&lt;image&gt;</c> element - either an embedded raster payload (fit into its
        /// own (x, y, width, height) box per its own <c>preserveAspectRatio</c>, same alignment/meet/
        /// slice math as any other viewport) or an embedded <c>image/svg+xml</c> payload (rendered as
        /// its own self-contained <see cref="SvgDocument"/>, so its <c>url(#id)</c> references resolve
        /// against its own gradient/clip/mask/pattern registries, not the host document's). Does
        /// nothing for an unresolved <c>href</c> (see <see cref="SvgImageElement"/>).
        /// </summary>
        private static void RenderImage(Canvas g, SvgImageElement image, double opacity)
        {
            if (image.Width <= 0 || image.Height <= 0)
                return;

            var viewportRect = new Rect(image.X, image.Y, image.Width, image.Height);

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
                g.DrawImage(raster, new Rect(0, 0, raster.Width, raster.Height));
                g.PopTransform();
                g.PopClip();
            }
        }

        /// <summary>
        /// Renders a <c>&lt;foreignObject&gt;</c>: paints (clipped to its viewport by the content) its already laid-out HTML
        /// with the box's top-left at (x, y). Does nothing without content (a standalone SVG).
        /// </summary>
        private static void RenderForeignObject(Canvas g, SvgForeignObjectElement foreign)
        {
            if (foreign.Width <= 0 || foreign.Height <= 0)
                return;

            // Canvas.PushTransform takes layout units and divides by PixelsPerPoint, while everything drawn inside the
            // SVG's own transform is in user units - hence the factor, so the box lands at (x, y) user units.
            var ppp = g.PixelsPerPoint;
            g.PushTransform(Matrix3x2.CreateTranslation((float)(foreign.X * ppp), (float)(foreign.Y * ppp)));
            foreign.Content.Paint(g);
            g.PopTransform();
        }

        /// <summary>
        /// One addressable character of a <c>&lt;text&gt;</c> subtree during flatten/layout: the glyph, its
        /// owning run (font/paint/anchor) and accumulated opacity, its assigned per-character position/
        /// rotation (null = unset ⇒ flow/inherit), and the layout results (<see cref="Px"/>/<see cref="Py"/>/
        /// <see cref="Advance"/>).
        /// </summary>
        private sealed class GlyphInfo
        {
            /// <summary>Set when <c>textLength</c> moved this glyph away from its natural pen position, so it must be painted on its own rather than batched with its neighbours.</summary>
            public bool SpacingAdjusted;

            /// <summary>The factor <c>textLength</c> with <c>lengthAdjust="spacingAndGlyphs"</c> stretched this glyph by along the inline axis (1 = unscaled). The glyph paints scaled about its own pen position, and <see cref="Advance"/> is already the scaled advance.</summary>
            public double GlyphScale = 1;

            /// <summary>The extra space <c>textLength</c> with <c>lengthAdjust="spacing"</c> added to <see cref="Advance"/> after this glyph (scaled along with it by any later <c>spacingAndGlyphs</c>), so a <c>&lt;textPath&gt;</c> can centre the glyph in the part of the advance that is its own.</summary>
            public double GapAdded;

            /// <summary>Whether <c>textLength</c> stretched this glyph.</summary>
            public bool IsScaled => GlyphScale != 1;

            /// <summary>The auto-wrapped line box this glyph landed on (SVG 2 §11.7); always 0 for text that is not wrapped. A paint batch never spans two lines.</summary>
            public int LineIndex;

            /// <summary>Set on the last glyph of a wrapped line that was broken at a hyphenation point: a hyphen is painted after it, and <see cref="Advance"/> includes it.</summary>
            public bool TrailingHyphen;

            /// <summary>Set on a glyph of wrapped text that did not fit in the shape (or box) the text is laid out in; it is not painted.</summary>
            public bool Omitted;

            /// <summary>Settable (not <c>init</c>) so bidi L4 mirroring can rewrite an RTL glyph's
            /// character to its mirror-image codepoint in place (see <c>ApplyBidiReordering</c>).</summary>
            public required string Glyph { get; set; }

            /// <summary>
            /// This glyph's true logical-order source character, when <see cref="Glyph"/> was rewritten
            /// to its mirror image by <see cref="ApplyBidiReordering"/> (L4) - null (the overwhelming
            /// common case: never mirrored) means <see cref="Glyph"/> itself is already the logical
            /// source. Read by <see cref="PaintGlyphs"/>/<see cref="PaintUprightGlyph"/>/
            /// <see cref="PaintRotatedGlyph"/> to build each painted string's positionally-aligned
            /// ToUnicode logical source (see <c>PeachDrawing.Text.Internal.Fonts.CMapInfo.AddShapedText</c>'s own remarks
            /// on that contract) - unlike HTML's whole-word reversal, SVG's bidi pass physically reorders
            /// individual <see cref="GlyphInfo"/> instances, so each glyph already carries its own
            /// correct logical value directly; nothing needs recomputing from a run-wide position formula.
            /// </summary>
            public string? LogicalGlyph { get; set; }
            public required SvgTextElement Run { get; init; }
            public required Font Font { get; init; }
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
            public Size Size { get; set; }

            /// <summary>Set by <see cref="LayoutGlyphs"/> - whether this glyph paints upright (unrotated)
            /// rather than rotated, under a vertical writing mode. Always false when the text root's
            /// <c>writing-mode</c> is <c>horizontal-tb</c>.</summary>
            public bool IsUpright { get; set; }

            /// <summary>Set by <see cref="LayoutGlyphs"/> for an upright glyph whose font carries a real
            /// <c>VORG</c> table (<see cref="Font.HasVerticalOrigin"/>, issue #775) - the anchor
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

            /// <summary>
            /// Non-null when this glyph participates in a multi-character Arabic-family joining or
            /// Devanagari USE shaping run - a reference to the run's own first <see cref="GlyphInfo"/>
            /// (which may be this instance itself, for a lone participating character), carrying the
            /// run's shared state (<see cref="RunText"/>/<see cref="RunJoiningForms"/>/
            /// <see cref="RunUseCategories"/>/<see cref="RunScriptTag"/>/<see cref="RunMeasuredWidth"/>/
            /// <see cref="RunReverseForDisplay"/>). Null - the overwhelming common case - means this
            /// glyph shapes/measures/reorders/paints exactly as it did before this feature existed,
            /// entirely on its own. Assigned once by <see cref="ResolveComplexScriptRuns"/>, which runs
            /// right after <see cref="FlattenRun"/> and before <see cref="LayoutGlyphs"/>/
            /// <see cref="ApplyBidiReordering"/> (both of which read it).
            /// </summary>
            public GlyphInfo? ShapingRunFirst { get; set; }

            /// <summary>This run's full logical-order (never reordered/mirrored) text - only meaningful
            /// on the run's own first glyph (<see cref="ShapingRunFirst"/> referencing itself).</summary>
            public string? RunText { get; set; }

            /// <summary>This run's per-character Arabic-family joining forms (parallel to
            /// <see cref="RunText"/>'s Runes), or null for a USE run - only meaningful on the run's own
            /// first glyph.</summary>
            public ArabicJoiningForm[]? RunJoiningForms { get; set; }

            /// <summary>This run's per-character Devanagari USE categories (parallel to
            /// <see cref="RunText"/>'s Runes), or null for an Arabic-family joining run - only
            /// meaningful on the run's own first glyph.</summary>
            public UseCategory[]? RunUseCategories { get; set; }

            /// <summary>This run's requested OpenType script tag - only meaningful on the run's own
            /// first glyph.</summary>
            public string? RunScriptTag { get; set; }

            /// <summary>
            /// This run's total shaped advance width, measured once as a whole string (only meaningful
            /// on the run's own first glyph, set by <see cref="LayoutGlyphs"/>) - every other member of
            /// the run gets <see cref="Advance"/> 0, since its position/width is subsumed into the run's
            /// own single shaped span (a GSUB ligature's component characters have no independently
            /// addressable position either - SVG 2 §11.5). Vertical writing modes never form a run at
            /// all (see <see cref="ResolveComplexScriptRuns"/>'s own remarks), so this is always 0 there.
            /// </summary>
            public double RunMeasuredWidth { get; set; }

            /// <summary>
            /// Whether this run's shaped glyph list should reverse for display - only meaningful on the
            /// run's own first glyph. Set true by <see cref="ApplyBidiReordering"/> for an Arabic-family
            /// joining run (<see cref="RunJoiningForms"/> non-null) that falls inside a right-to-left
            /// visual run; a USE (Devanagari) run is never display-reversed, mirroring
            /// <c>CssRectWord.DisplayOrderReversed</c>'s own <c>EffectiveJoiningForms</c>-only gating on
            /// the HTML side.
            /// </summary>
            public bool RunReverseForDisplay { get; set; }
        }

        /// <summary>
        /// Renders a whole <c>&lt;text&gt;</c> element: its subtree is flattened to an addressable-character
        /// stream (SVG 1.1 §10.4), laid out (per-character x/y/dx/dy/rotate lists, text chunks, per-chunk
        /// <c>text-anchor</c>), and painted - consecutive same-run, unrotated, in-flow characters as one
        /// selectable <see cref="Canvas.DrawString(string, Font, PaintColor, PaintPoint, Size, double, FontPalette?, ShapeSettings?)"/>, anything positioned/rotated/gradient/stroked per
        /// glyph. A <c>&lt;textPath&gt;</c> descendant lays out independently along its path.
        /// </summary>
        private static void RenderText(Canvas g, SvgDocument document, SvgTextElement text, double opacity)
        {
            var glyphs = new List<GlyphInfo>();
            var textPaths = new List<(SvgTextElement Run, double ParentOpacity)>();
            var overrides = new List<EmbeddingSpan>();
            FlattenRun(text, 1.0, glyphs, textPaths, overrides);

            if (glyphs.Count > 0)
            {
                // writing-mode is resolved once from the <text> root, not per descendant run: unlike
                // text-orientation (genuinely meaningful per nested <tspan>, see IsUprightGlyph), a
                // change of pen-advance axis mid-text has no defined real-world meaning to preserve.
                var isVertical = IsVerticalWritingMode(text.WritingMode);

                // Arabic-family joining/Devanagari USE shaping runs are horizontal-tb-only (see
                // ResolveComplexScriptRuns's own remarks) - must run before LayoutGlyphs, which reads
                // GlyphInfo.ShapingRunFirst to decide per-run vs per-glyph measurement.
                if (!isVertical)
                    ResolveComplexScriptRuns(glyphs);

                LayoutTextBlock(g, text, glyphs, overrides, isVertical);
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
        /// Real UAX#9 resolution (<see cref="Bidi"/>) for one <c>&lt;text&gt;</c> element's
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
        private static void ApplyBidiReordering(SvgTextElement text, List<GlyphInfo> glyphs, List<EmbeddingSpan> overrides, bool isVertical, IReadOnlyList<(int Start, int Length)>? lines = null)
        {
            var paragraphText = string.Concat(glyphs.Select(gi => gi.Glyph));
            if (paragraphText.Length == 0)
                return;

            var direction = Map.DirectionModes.GetValueOrDefault(text.Direction, DirectionMode.Ltr) == DirectionMode.Rtl
                ? BaseDirection.Rtl
                : BaseDirection.Ltr;

            // paragraphText is a UTF-16 string (a surrogate pair - any astral character, e.g. U+10800
            // and above - is two code units), but glyphs is exactly one GlyphInfo per Rune (FlattenRun),
            // so paragraphText.Length can exceed glyphs.Count. Bidi.Analyze returns one level
            // per UTF-16 code unit of its input, and overrides (built by FlattenRun) are expressed in
            // glyph ordinals - both need translating against a per-glyph UTF-16 start-offset map before/
            // after crossing into Bidi's own code-unit-indexed world, or everything from the
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

            var result = Bidi.Analyze(paragraphText, direction, codeUnitOverrides);

            var glyphLevels = new byte[glyphs.Count];
            for (var i = 0; i < glyphs.Count; i++)
                // A glyph with no text of its own (a soft hyphen or line feed in wrapped text) takes the level of the character at its position.
                glyphLevels[i] = result.Levels[Math.Min(utf16Starts[i], result.Levels.Length - 1)];

            // The paragraph's levels are resolved once above; each line of auto-wrapped text (lines) is then reordered on its own, in place within its own
            // slice of the list. Text that is not wrapped is one line.
            var lineRanges = lines ?? [(0, glyphs.Count)];

            // Reordering operates along whichever axis LayoutGlyphs actually advanced the pen on - Px
            // for horizontal-tb, Py for vertical-rl/vertical-lr (see LayoutGlyphs's own remarks); the
            // cross axis is untouched by reordering either way, same as GlyphInfo.Py is never reassigned
            // in the horizontal case below.
            Func<GlyphInfo, double> getPos = isVertical ? gi => gi.Py : gi => gi.Px;
            Action<GlyphInfo, double> setPos = isVertical ? (gi, v) => gi.Py = v : (gi, v) => gi.Px = v;

            var originalPos = glyphs.Select(getPos).ToList();

            var reordered = new List<GlyphInfo>(glyphs.Count);
            var anyReordered = false;
            foreach (var (lineStart, lineLength) in lineRanges)
            {
                var runs = Bidi.ReorderLine(glyphLevels, lineStart, lineLength);
                if (runs.Count == 0 || (runs.Count == 1 && !runs[0].IsRtl))
                {
                    for (var idx = lineStart; idx < lineStart + lineLength; idx++)
                        reordered.Add(glyphs[idx]);
                    continue;
                }

                anyReordered = true;
                var runNewStart = originalPos[lineStart];
                foreach (var run in runs)
                {
                    var runOldStart = originalPos[run.Start];
                    var lastIndexInRun = run.Start + run.Length - 1;
                    var runContentWidth = originalPos[lastIndexInRun] + glyphs[lastIndexInRun].Advance - runOldStart;

                    if (run.IsRtl)
                    {
                        var k = run.Length - 1;
                        while (k >= 0)
                        {
                            var idx = run.Start + k;
                            var gi = glyphs[idx];

                            if (gi.ShapingRunFirst is { } shapingFirst)
                            {
                                // A complex-shaping run (Arabic-family joining or Devanagari USE) reorders as
                                // one atomic block, preserving its own internal logical-order adjacency -
                                // mirroring CssLayoutEngine.MirrorWordTextIfNeeded's HTML precedent (a
                                // joining word's text is never itself reversed/mirrored; only the resulting
                                // shaped glyph list is, via ShapeSettings.ReverseForDisplay - see
                                // ResolveShapingFeatures). It can never straddle this bidi run's own
                                // boundary: ResolveComplexScriptRuns never lets a run cross an
                                // SvgTextElement (tspan) boundary, and every bidi-level change from an
                                // explicit unicode-bidi push occurs at exactly such a boundary (FlattenRun
                                // only ever emits one there) - so scanning backward from any of a run's
                                // member glyphs always finds the whole run still inside this bidi run.
                                var blockEnd = idx;
                                var blockStart = idx;
                                while (blockStart > run.Start && ReferenceEquals(glyphs[blockStart - 1].ShapingRunFirst, shapingFirst))
                                    blockStart--;

                                var blockOffsetFromRunStart = originalPos[blockStart] - runOldStart;
                                var blockWidth = shapingFirst.RunMeasuredWidth;

                                // USE (Devanagari) never display-reverses - only Arabic-family joining does,
                                // matching CssRectWord.DisplayOrderReversed's own EffectiveJoiningForms-only
                                // gating on the HTML side.
                                if (shapingFirst.RunJoiningForms is not null)
                                    shapingFirst.RunReverseForDisplay = true;

                                var newLeftEdge = runNewStart + runContentWidth - (blockOffsetFromRunStart + blockWidth);
                                for (var m = blockStart; m <= blockEnd; m++)
                                {
                                    var mgi = glyphs[m];
                                    setPos(mgi, ReferenceEquals(mgi, shapingFirst) ? newLeftEdge : newLeftEdge + blockWidth);
                                    reordered.Add(mgi);
                                }

                                k = blockStart - run.Start - 1;
                                continue;
                            }

                            if (System.Text.Rune.DecodeFromUtf16(gi.Glyph, out var rune, out _) == System.Buffers.OperationStatus.Done
                                && Bidi.TryGetMirror(rune, out var mirrored))
                            {
                                // The pre-mirror value is this glyph's true logical-order source - captured
                                // before Glyph itself is overwritten below.
                                gi.LogicalGlyph = gi.Glyph;
                                gi.Glyph = mirrored.ToString();
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
                                    ? gi.Font.GetVerticalOriginY(mirrored) - gi.Font.Ascent
                                    : 0;
                            }

                            var offsetFromRunStart = originalPos[idx] - runOldStart;
                            setPos(gi, runNewStart + runContentWidth - (offsetFromRunStart + gi.Advance));
                            reordered.Add(gi);
                            k--;
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
            }

            if (!anyReordered)
                return;

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
        /// shared start index nests outer-before-inner (see <c>Bidi.Analyze</c>'s own handling of
        /// multiple overrides sharing an end index).
        /// <paramref name="opacityFactor"/> is the product of the run-chain's <c>opacity</c> below the root
        /// <c>&lt;text&gt;</c> (whose own opacity is already folded into the caller's base opacity).
        /// </summary>
        private static void FlattenRun(SvgTextElement run, double opacityFactor, List<GlyphInfo> glyphs, List<(SvgTextElement, double)> textPaths, List<EmbeddingSpan> overrides)
        {
            var startIndex = glyphs.Count;

            foreach (var item in run.Content)
            {
                switch (item)
                {
                    case SvgTextFragment fragment when run.Font is { } font:
                        foreach (var rune in fragment.Text.EnumerateRunes())
                        {
                            // A combining mark, joiner or variation selector stays in its base character's font so the cluster still shapes together.
                            var continuesCluster = glyphs.Count > 0 && ReferenceEquals(glyphs[^1].Run, run) && IsClusterContinuation(rune);
                            var glyphFont = continuesCluster ? glyphs[^1].Font : run.FontFor?.Invoke(rune) ?? font;
                            glyphs.Add(new GlyphInfo { Glyph = rune.ToString(), Run = run, Font = glyphFont, Opacity = opacityFactor });
                        }
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
                    overrides.Add(new EmbeddingSpan(startIndex, contributedLength, push));
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
        /// Resolves per-character Arabic-family joining forms and Devanagari USE categories over
        /// <paramref name="glyphs"/>' full logical (document) order - the SVG-side equivalent of
        /// <c>CssBidiParagraphResolver.ResolveScriptsAndJoining</c>, simpler here since
        /// <see cref="FlattenRun"/> already produces exactly one <see cref="GlyphInfo"/> per Rune (no
        /// UTF-16 surrogate-pair re-indexing needed the way <c>CssBox</c>'s char-indexed arrays require -
        /// see <see cref="ApplyBidiReordering"/>'s own remarks on that distinction). Then groups maximal
        /// contiguous runs of participating characters - sharing one <see cref="SvgTextElement"/> run,
        /// one participating script (Arabic-family or Devanagari), zero <c>letter-spacing</c> (non-zero
        /// letter-spacing inserts space between glyphs, which real shapers - and real browsers - already
        /// treat as disabling optional ligature/cursive joining, so this simply never forms a run rather
        /// than forming an incorrect one), and no explicit per-character <c>x</c>/<c>y</c>/<c>dx</c>/<c>dy</c>/
        /// <c>rotate</c> on any but the run's own first character (SVG 2 §11.5: a ligature/joined glyph's
        /// component characters have no independently addressable position) - into a shared
        /// <see cref="GlyphInfo.ShapingRunFirst"/> run, so <see cref="LayoutGlyphs"/>/
        /// <see cref="ApplyBidiReordering"/>/<see cref="PaintGlyphs"/> can shape, measure, reorder and
        /// paint the whole run as one atomic unit - real joined/reordered glyphs - instead of nominal,
        /// unjoined, unreordered isolated-form characters (the prior, and still SVG <c>&lt;textPath&gt;</c>,
        /// behavior - see <see cref="RenderTextPath"/>'s own remarks on why that path is out of scope).
        /// A run's own first character is also required to carry no explicit <c>rotate</c> - unlike
        /// <see cref="PaintGlyphs"/>'s own batching, which lets a batch's start glyph rotate freely,
        /// <see cref="PaintRotatedGlyph"/> paints only that one <see cref="GlyphInfo"/>'s own
        /// <see cref="GlyphInfo.Glyph"/> string, which would silently drop the rest of a multi-character
        /// run's text.
        /// </summary>
        private static void ResolveComplexScriptRuns(List<GlyphInfo> glyphs)
        {
            var count = glyphs.Count;
            if (count == 0)
                return;

            var codepoints = new int[count];
            for (var i = 0; i < count; i++)
                codepoints[i] = Rune.GetRuneAt(glyphs[i].Glyph, 0).Value;

            var rawScripts = new string[count];
            for (var i = 0; i < count; i++)
                rawScripts[i] = Scripts.Of(codepoints[i]);
            var resolvedScripts = Scripts.ResolveLooked(rawScripts);

            // Run unconditionally over the whole stream, like CssBidiParagraphResolver does - a
            // non-joining codepoint's ArabicJoiningType is already Non_Joining (U), which
            // ArabicJoiningShaper resolves to ArabicJoiningForm.None for free.
            var joiningForms = ArabicJoining.Resolve(codepoints);

            // Only classified (and only allocated at all) when the stream actually contains Devanagari
            // text - same "don't activate syllable scanning for every run" reasoning as the HTML side.
            UseCategory[]? useCategories = null;
            for (var i = 0; i < count; i++)
            {
                if (resolvedScripts[i] != "Devanagari")
                    continue;
                useCategories ??= new UseCategory[count];
                useCategories[i] = UniversalShaping.Classify(codepoints[i]);
            }

            var pos = 0;
            while (pos < count)
            {
                var isArabicParticipant = ArabicJoining.TypeOf(codepoints[pos]) != ArabicJoiningType.U;
                var isUseParticipant = !isArabicParticipant && resolvedScripts[pos] == "Devanagari";

                if (!isArabicParticipant && !isUseParticipant)
                {
                    pos++;
                    continue;
                }

                var first = glyphs[pos];

                // A character whose own explicit rotate/letter-spacing disqualifies it from anchoring a
                // run can't retroactively be un-scanned by the extend-loop below - checking eligibility
                // up front and only advancing by one position (not skipping the whole contiguous
                // participant span) lets a LATER character in that same span still validly start its
                // own run (e.g. "بيت" with rotate="15 0 0": Beh can't anchor a run, but Yeh+Teh still can).
                if ((first.Rotate ?? 0) != 0 || first.Run.LetterSpacing != 0)
                {
                    pos++;
                    continue;
                }

                var end = pos + 1;
                while (end < count
                       && ReferenceEquals(glyphs[end].Run, first.Run)
                       && ReferenceEquals(glyphs[end].Font, first.Font)
                       && first.Run.LetterSpacing == 0
                       && (glyphs[end].Rotate ?? 0) == 0
                       && glyphs[end].X is null && glyphs[end].Y is null
                       && glyphs[end].Dx is null && glyphs[end].Dy is null
                       && (isArabicParticipant
                           ? ArabicJoining.TypeOf(codepoints[end]) != ArabicJoiningType.U
                           : resolvedScripts[end] == "Devanagari"))
                {
                    end++;
                }

                var length = end - pos;
                var text = new StringBuilder(length);
                for (var m = pos; m < end; m++)
                    text.Append(glyphs[m].Glyph);

                first.ShapingRunFirst = first;
                first.RunText = text.ToString();
                first.RunScriptTag = OpenTypeTags.ForScript(resolvedScripts[pos]);

                if (isArabicParticipant)
                {
                    var forms = new ArabicJoiningForm[length];
                    Array.Copy(joiningForms, pos, forms, 0, length);
                    first.RunJoiningForms = forms;
                }
                else
                {
                    var categories = new UseCategory[length];
                    Array.Copy(useCategories!, pos, categories, 0, length);
                    first.RunUseCategories = categories;
                }

                for (var m = pos + 1; m < end; m++)
                    glyphs[m].ShapingRunFirst = first;

                pos = end;
            }
        }

        /// <summary>
        /// This glyph's effective <see cref="ShapeSettings"/> for measurement/painting: its own
        /// run's <see cref="SvgTextElement.ShapingFeatures"/>, layered with the run-wide script tag/
        /// joining forms/USE categories/reverse-for-display request when <paramref name="gi"/> is part
        /// of a multi-character complex-script shaping run (see <see cref="GlyphInfo.ShapingRunFirst"/>/
        /// <see cref="ResolveComplexScriptRuns"/>) - the SVG-side equivalent of HTML's
        /// <c>CssBox.ResolveWordShapingFeatures</c>. Returns <paramref name="gi"/>'s own run features
        /// unchanged (today's exact behavior) for every glyph outside such a run - the overwhelming
        /// common case.
        /// </summary>
        private static ShapeSettings ResolveShapingFeatures(GlyphInfo gi) =>
            gi.ShapingRunFirst is not { } first
                ? gi.Run.ShapingFeatures
                : gi.Run.ShapingFeatures with
                {
                    ScriptTag = first.RunScriptTag,
                    JoiningForms = first.RunJoiningForms,
                    UseCategories = first.RunUseCategories,
                    ReverseForDisplay = first.RunReverseForDisplay,
                };

        /// <summary>
        /// How far below the text position a run's alphabetic baseline sits along the block axis. The run's dominant baseline - or, for a nested element with an
        /// <c>alignment-baseline</c> of its own, that baseline - is put on the position; otherwise the run's alphabetic baseline aligns with its parent's. A
        /// <c>baseline-shift</c> then raises the run. The pen does not move: only where the glyphs are drawn does. Under a vertical writing mode the block axis is
        /// horizontal and the dominant baseline defaults to <c>central</c>; <paramref name="vertical"/> selects it (and the font's vertical <c>BASE</c> axis).
        /// </summary>
        private static double BaselineOffset(SvgTextElement run, bool vertical = false)
        {
            var offset = AlphabeticBaselineBelowPosition(run);
            return offset - run.BaselineShift;

            double AlphabeticBaselineBelowPosition(SvgTextElement r)
            {
                if (r.Font is not { } font)
                    return 0;

                if (r.AlignmentBaseline is not ("auto" or "baseline" or "use-script" or "no-change" or "reset-size"))
                    return HeightAboveAlphabetic(r.AlignmentBaseline, font, vertical);

                return r.ParentRun is { } parent
                    ? AlphabeticBaselineBelowPosition(parent)
                    : HeightAboveAlphabetic(vertical && r.DominantBaseline is "auto" ? "central" : r.DominantBaseline, font, vertical);
            }
        }

        /// <summary>
        /// How far a vertical run is moved across the column (towards +x) from where it is centred on the text position. Runs that state no baseline of their own
        /// stay centred, as they always have; once a dominant baseline, an <c>alignment-baseline</c> or a <c>baseline-shift</c> is in play the run's baseline
        /// is aligned the way <see cref="BaselineOffset"/> does for horizontal text, relative to the centred default.
        /// </summary>
        private static double VerticalCrossShift(SvgTextElement run)
        {
            var explicitBaseline = false;
            for (var r = run; r is not null && !explicitBaseline; r = r.ParentRun)
                explicitBaseline = r.DominantBaseline != "auto" || r.AlignmentBaseline != "auto" || r.BaselineShift != 0;

            if (!explicitBaseline || run.Font is not { } font)
                return 0;

            // A glyph drawn centred has its central baseline on the position; the alphabetic baseline must instead sit BaselineOffset away from it.
            return HeightAboveAlphabetic("central", font, vertical: true) - BaselineOffset(run, vertical: true);
        }

        /// <summary>
        /// The height of a named baseline above the alphabetic baseline of <paramref name="font"/>: read from the font's <c>BASE</c> table when it states one
        /// (<c>ideographic</c>, <c>hanging</c>, <c>mathematical</c>, and <c>central</c> as the middle of the ideographic character face), otherwise
        /// approximated from its metrics.
        /// </summary>
        private static double HeightAboveAlphabetic(string baseline, Font font, bool vertical = false)
        {
            if (FromBaseTable(baseline, font, vertical) is { } real)
                return real;

            var descent = font.Height - font.Ascent;
            return baseline switch
            {
                "ideographic" => -0.12 * font.Size,
                "hanging" => 0.8 * font.Size,
                "mathematical" => (font.XHeightEm ?? 0.5) * font.Size,
                "middle" => (font.XHeightEm ?? 0.5) * font.Size / 2,
                "central" => (font.Ascent - descent) / 2,
                "text-top" or "text-before-edge" or "before-edge" => font.Ascent,
                "text-bottom" or "text-after-edge" or "after-edge" => -descent,
                _ => 0,
            };
        }

        private static double? FromBaseTable(string baseline, Font font, bool vertical)
        {
            switch (baseline)
            {
                case "ideographic":
                case "hanging":
                case "mathematical":
                    var tag = baseline switch { "ideographic" => "ideo", "hanging" => "hang", _ => "math" };
                    return font.GetBaselineHeightEm(tag, vertical) * font.Size;
                case "central":
                    return font.GetBaselineHeightEm("icfb", vertical) is { } bottom && font.GetBaselineHeightEm("icft", vertical) is { } top
                        ? (bottom + top) / 2 * font.Size
                        : null;
                default:
                    return null;
            }
        }

        private static bool IsClusterContinuation(System.Text.Rune rune)
        {
            if (rune.Value is 0x200C or 0x200D or (>= 0xFE00 and <= 0xFE0F) or (>= 0xE0100 and <= 0xE01EF))
                return true;

            return System.Text.Rune.GetUnicodeCategory(rune) is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.EnclosingMark;
        }

        private static bool IsWithin(SvgTextElement? candidate, SvgTextElement container)
        {
            for (var r = candidate; r is not null; r = r.ParentRun)
            {
                if (ReferenceEquals(r, container))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Applies <c>textLength</c> (SVG 2 §11.4) along the inline axis (<c>x</c> for horizontal text, <c>y</c> under a vertical writing mode, or the
        /// distance along a <c>&lt;textPath&gt;</c>, which the caller lays out in <see cref="GlyphInfo.Px"/>). With <c>lengthAdjust="spacing"</c> the
        /// characters a text content element holds are spread (or squeezed) by adding the same extra space after each addressable character but the last;
        /// with <c>spacingAndGlyphs</c> the positions and advances are scaled about the element's start and the glyphs are stretched
        /// (<see cref="GlyphInfo.GlyphScale"/>). Either way the element spans exactly its <c>textLength</c>. Inner elements are adjusted before the ones
        /// containing them, and the text after an adjusted element - up to the next absolutely positioned chunk - moves with it.
        /// </summary>
        private static void ApplyTextLength(List<GlyphInfo> glyphs, bool isVertical)
        {
            double Pos(GlyphInfo gi) => isVertical ? gi.Py : gi.Px;
            void Move(GlyphInfo gi, double d) { if (isVertical) gi.Py += d; else gi.Px += d; }
            bool Absolute(GlyphInfo gi) => isVertical ? gi.Y is not null : gi.X is not null;

            var adjusted = new List<SvgTextElement>();
            foreach (var gi in glyphs)
            {
                for (var r = gi.Run; r is not null; r = r.ParentRun)
                {
                    if (r.TextLength is not null && !adjusted.Contains(r))
                        adjusted.Add(r);
                }
            }

            // Innermost first: a run nested deeper has a longer ancestor chain.
            static int Depth(SvgTextElement r) { var d = 0; for (var p = r.ParentRun; p is not null; p = p.ParentRun) d++; return d; }
            foreach (var run in adjusted.OrderByDescending(Depth))
            {
                var first = glyphs.FindIndex(gi => IsWithin(gi.Run, run));
                var last = glyphs.FindLastIndex(gi => IsWithin(gi.Run, run));
                if (first < 0 || last < first)
                    continue;

                var natural = Pos(glyphs[last]) + glyphs[last].Advance - Pos(glyphs[first]);
                var delta = run.TextLength!.Value - natural;
                if (delta == 0)
                    continue;

                if (run.LengthAdjust == "spacingAndGlyphs")
                {
                    if (natural <= 0 || run.TextLength.Value <= 0)
                        continue;

                    var scale = run.TextLength.Value / natural;
                    var origin = Pos(glyphs[first]);
                    for (var i = first; i <= last; i++)
                    {
                        var gi = glyphs[i];
                        Move(gi, (Pos(gi) - origin) * (scale - 1));
                        gi.Advance *= scale;
                        gi.GapAdded *= scale;
                        gi.GlyphScale *= scale;
                    }

                    for (var i = last + 1; i < glyphs.Count && !Absolute(glyphs[i]); i++)
                        Move(glyphs[i], delta);
                    continue;
                }

                // Characters of one complex-script shaping run are one unit: only its first glyph takes a gap.
                var units = new List<int>();
                for (var i = first; i <= last; i++)
                {
                    if (glyphs[i].ShapingRunFirst is { } runFirst && !ReferenceEquals(runFirst, glyphs[i]))
                        continue;
                    units.Add(i);
                }

                if (units.Count < 2)
                    continue;

                var perGap = delta / (units.Count - 1);
                var shiftAfter = 0.0;
                for (var u = 0; u < units.Count; u++)
                {
                    var index = units[u];
                    var shift = u * perGap;
                    Move(glyphs[index], shift);
                    glyphs[index].SpacingAdjusted = true;
                    if (u < units.Count - 1)
                    {
                        glyphs[index].Advance += perGap;
                        glyphs[index].GapAdded += perGap;
                        shiftAfter = (u + 1) * perGap;
                    }

                    // The members of a shaping run follow its first glyph.
                    for (var m = index + 1; m <= last && glyphs[m].ShapingRunFirst is { } f && ReferenceEquals(f, glyphs[index]); m++)
                        Move(glyphs[m], shift);
                }

                for (var i = last + 1; i < glyphs.Count && !Absolute(glyphs[i]); i++)
                    Move(glyphs[i], shiftAfter);
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
        private static void LayoutGlyphs(Canvas g, List<GlyphInfo> glyphs, bool isVertical, bool measureOnly = false)
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

                    gi.Px = penX + VerticalCrossShift(gi.Run);
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
                    // the font's own line height, not its measured width (Canvas.DrawString always
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
                    // Auto-wrapped text (measureOnly) places its own lines: the glyphs are only measured here, along one unbroken line.
                    if (!measureOnly)
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
                    }

                    gi.Px = penX;
                    gi.Py = penY + BaselineOffset(gi.Run);

                    if (gi.ShapingRunFirst is { } shapingFirst)
                    {
                        // A complex-script shaping run is measured once, as a whole shaped string, on
                        // its own first glyph - a real joined/reordered glyph run's total advance is not
                        // generally the sum of its component characters' isolated-form widths (most
                        // visibly: a lam-alef ligature consumes two characters into one glyph with one
                        // advance). Every other member gets Advance 0 - see ShapingRunFirst's own
                        // remarks on why that's exactly right, not merely a placeholder.
                        if (ReferenceEquals(gi, shapingFirst))
                        {
                            gi.Size = g.MeasureString(shapingFirst.RunText!, gi.Font, ResolveShapingFeatures(gi));
                            shapingFirst.RunMeasuredWidth = gi.Size.Width;
                            gi.Advance = gi.Size.Width;
                        }
                        else
                        {
                            gi.Size = new Size(0, gi.Font.Height);
                            gi.Advance = 0;
                        }
                    }
                    else
                    {
                        gi.Size = g.MeasureString(gi.Glyph, gi.Font, gi.Run.ShapingFeatures);
                        gi.Advance = gi.Size.Width + gi.Run.LetterSpacing;
                        if (gi.Run.WordSpacing != 0 && IsWhitespaceGlyph(gi.Glyph))
                            gi.Advance += gi.Run.WordSpacing;
                    }

                    penX += gi.Advance;
                }
            }

            if (measureOnly)
                return;

            ApplyTextLength(glyphs, isVertical);

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
        /// property, the same <see cref="VerticalOrientation"/> the HTML pipeline shares.
        /// </summary>
        private static bool IsUprightGlyph(GlyphInfo gi) => gi.Run.TextOrientation switch
        {
            TextOrientation.Upright => true,
            TextOrientation.Sideways => false,
            _ => System.Text.Rune.DecodeFromUtf16(gi.Glyph, out var rune, out _) == System.Buffers.OperationStatus.Done
                 && VerticalOrientation.IsEffectivelyUpright(rune)
        };

        /// <summary>Whether <paramref name="glyph"/> (one <see cref="System.Text.Rune"/>-worth of
        /// text, per <see cref="GlyphInfo"/>'s own per-character granularity) is whitespace - the same
        /// test <c>TextWhitespaceState.Collapse</c> uses, so <c>word-spacing</c> targets exactly the
        /// space characters that survive collapsing.</summary>
        private static bool IsWhitespaceGlyph(string glyph) => glyph.Length > 0 && char.IsWhiteSpace(glyph[0]);

        /// <summary>
        /// Paints the laid-out character stream. Under <c>horizontal-tb</c>: a maximal contiguous group
        /// of same-run, unrotated, in-flow characters is painted as one <see cref="Canvas.DrawString(string, Font, PaintColor, PaintPoint, Size, double, FontPalette?, ShapeSettings?)"/>
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
        private static void PaintGlyphs(Canvas g, SvgDocument document, List<GlyphInfo> glyphs, double opacity, bool isVertical)
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
                var startIsWordSpacedWhitespace = (start.Run.WordSpacing != 0 && IsWhitespaceGlyph(start.Glyph)) || ((start.SpacingAdjusted || start.IsScaled) && start.ShapingRunFirst is null);
                while (!startIsWordSpacedWhitespace && i < glyphs.Count)
                {
                    var gc = glyphs[i];
                    // A complex-script shaping run's own members (sharing ShapingRunFirst) always merge
                    // together (ResolveComplexScriptRuns already required them to share Run and carry no
                    // mid-run explicit position/rotate); a boundary between two different runs, or
                    // between a run and plain text, always breaks the batch - each needs its own
                    // ShapeSettings (see ResolveShapingFeatures), so merging them would apply one
                    // run's joining forms/USE categories to the other's text.
                    if (gc.LineIndex != start.LineIndex || start.TrailingHyphen || !ReferenceEquals(gc.Run, start.Run) || !ReferenceEquals(gc.Font, start.Font) || ((gc.SpacingAdjusted || gc.IsScaled) && gc.ShapingRunFirst is null) || (gc.Rotate ?? 0) != 0
                        || gc.X is not null || gc.Y is not null || (gc.Dx ?? 0) != 0 || (gc.Dy ?? 0) != 0
                        || !ReferenceEquals(gc.ShapingRunFirst, start.ShapingRunFirst))
                        break;
                    builder.Append(gc.Glyph);
                    logicalBuilder.Append(gc.LogicalGlyph ?? gc.Glyph);
                    i++;

                    if (start.Run.WordSpacing != 0 && IsWhitespaceGlyph(gc.Glyph))
                        break;
                }

                // A wrapped line that was broken inside a word ends with the hyphen the break adds.
                if (glyphs[i - 1].TrailingHyphen)
                {
                    builder.Append('-');
                    logicalBuilder.Append('-');
                }

                var text = builder.ToString();
                if (text.Length == 0)
                    continue;

                var logicalText = logicalBuilder.ToString();
                if (logicalText == text) logicalText = null;
                var features = ResolveShapingFeatures(start);
                var size = g.MeasureString(text, font, features);
                if (start.IsScaled)
                {
                    // textLength's lengthAdjust="spacingAndGlyphs": the glyphs are stretched along the line about their own pen position.
                    var scaleTransform = GlyphTransform(start, 0);
                    PaintTextShadows(g, start.Run, text, font, start.Px, start.Py - font.Ascent, size, opacity * start.Opacity,
                        start.Run.LetterSpacing, features, logicalText, scaleTransform);
                    g.PushTransform(scaleTransform);
                    PaintTextGlyphs(g, document, start.Run, text, font, start.Px, start.Py - font.Ascent, size, opacity * start.Opacity,
                        start.Run.LetterSpacing, features, logicalText, paintShadows: false);
                    g.PopTransform();
                    continue;
                }

                PaintTextGlyphs(g, document, start.Run, text, font, start.Px, start.Py - font.Ascent, size, opacity * start.Opacity,
                    start.Run.LetterSpacing, features, logicalText);
            }
        }

        /// <summary>
        /// The matrix a glyph paints under when it is rotated <paramref name="degrees"/> clockwise and/or stretched by <c>textLength</c>: stretched along
        /// its own inline axis first, then rotated, both about its pen position <c>(Px, Py)</c>.
        /// </summary>
        private static Matrix3x2 GlyphTransform(GlyphInfo gi, double degrees)
        {
            var radians = degrees * (Math.PI / 180.0);
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            var toOrigin = new Matrix3x2(1, 0, 0, 1, (float)-gi.Px, (float)-gi.Py);
            var scale = Matrix3x2.CreateScale((float)gi.GlyphScale, 1f);
            var rotate = new Matrix3x2((float)cos, (float)sin, (float)-sin, (float)cos, 0, 0);
            var fromOrigin = new Matrix3x2(1, 0, 0, 1, (float)gi.Px, (float)gi.Py);
            return MultiplyMatrix(MultiplyMatrix(MultiplyMatrix(toOrigin, scale), rotate), fromOrigin);
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
        private static void PaintTextDecorations(Canvas g, List<GlyphInfo> glyphs, double opacity)
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
                    DrawDecorationSpan(g, decorator, font, start, end, opacity, glyphs);
            }
        }

        private static void DrawDecorationSpan(Canvas g, SvgTextElement decorator, Font font, GlyphInfo start, GlyphInfo end, double opacity, List<GlyphInfo> glyphs)
        {
            var x1 = start.Px;
            var x2 = end.Px + end.Advance;
            if (x2 <= x1)
                return;

            // currentColor (SvgTreeBuilder resolves an explicit color eagerly, leaving
            // TextDecorationColor null for both "unset" and literal "currentColor") falls back to the
            // decorator's own solid fill - SVG has no separate tracked `color` property the way HTML
            // does, and the text's own fill is the closest available proxy for what a reader perceives
            // as "this text's color". Resolved through ResolveInMarker first (a no-op outside a marker)
            // so a context-fill keyword on marker text isn't mistaken for "no solid color" and falls
            // back to black instead of the shape it is on.
            var resolvedFill = ResolveInMarker(decorator.Fill);
            var color = decorator.TextDecorationColor
                ?? (resolvedFill.Kind == SvgPaintKind.Solid ? resolvedFill.PaintColor : PaintColor.Black);
            var actualColor = ApplyOpacity(color, opacity * decorator.Opacity * decorator.FillOpacity);

            // `auto` keeps the fixed one-unit line this renderer always drew.
            var thickness = decorator.TextDecorationThicknessFromFont ? font.UnderlineThickness : decorator.TextDecorationThickness ?? 1;
            if (thickness <= 0)
                return;

            var style = Map.TextDecorationStyleModes.GetValueOrDefault(decorator.TextDecorationStyle, TextDecorationStyleMode.Solid);
            var pen = g.GetPen(actualColor);
            pen.Width = thickness;
            pen.DashStyle = TextDecorationStyleMapper.ToDashStyle(style);

            var span = new DecorationInterval(x1, x2);
            var top = start.Py - font.Ascent;

            foreach (var line in decorator.TextDecorationLine.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                double y = line switch
                {
                    "underline" => ResolveUnderlineY(decorator, font, start.Py, top),
                    "line-through" => top + font.Height / 2,
                    "overline" => top,
                    _ => double.NaN,
                };

                if (double.IsNaN(y))
                    continue;

                // css-text-decor-4 §2.5: only underlines and overlines skip ink - a line-through is meant to cross the glyphs.
                IReadOnlyList<DecorationInterval> exclusions = [];
                if (line is "underline" or "overline" && decorator.TextDecorationSkipInk != "none")
                {
                    var found = new List<DecorationInterval>();
                    AddInkExclusions(g, glyphs, start, end, y, thickness, decorator.TextDecorationSkipInk == "auto", found);
                    exclusions = found;
                }

                foreach (var segment in DecorationSegments.Subtract(span, exclusions))
                    FragmentPainter.StrokeDecorationSegment(g, pen, thickness, actualColor, style, line, segment.Start, segment.End, y,
                        isVertical: false, underSign: 1, atBlockStart: line == "overline");
            }
        }

        /// <summary>The underline's vertical position: <c>auto</c> hangs from the decorator's font as it always has; <c>from-font</c> uses the font's own underline position; <c>under</c> sits at the bottom of the line box. <c>text-underline-offset</c> then moves it further from the text.</summary>
        private static double ResolveUnderlineY(SvgTextElement decorator, Font font, double baseline, double top)
        {
            var position = decorator.TextUnderlinePosition;
            var basePosition = position.Contains("from-font", StringComparison.Ordinal) ? baseline - font.UnderlinePosition
                : position.Contains("under", StringComparison.Ordinal) ? top + font.Height
                : Math.Round(top + font.UnderlineOffset);
            return basePosition + decorator.TextUnderlineOffset;
        }

        /// <summary>
        /// Collects, for the glyphs from <paramref name="start"/> to <paramref name="end"/>, the horizontal stretches where their ink crosses the band a
        /// decoration line at <paramref name="y"/> would cover, each dilated by the clearance the line keeps from the ink (capped as HTML's is).
        /// Under <c>auto</c> (UA discretion) CJK characters are left unskipped, as css-text-decor-4 §2.5 suggests.
        /// </summary>
        private static void AddInkExclusions(Canvas g, List<GlyphInfo> glyphs, GlyphInfo start, GlyphInfo end, double y, double thickness, bool isAuto, List<DecorationInterval> into)
        {
            var from = glyphs.IndexOf(start);
            var to = glyphs.IndexOf(end);
            if (from < 0 || to < from)
                return;

            var half = thickness / 2;
            var clearance = Math.Min(thickness, FragmentPainter.MaximumInkSkipClearanceCssPixels * PeachPDF.CSS.Length.PointsPerPx * g.PixelsPerPoint);

            for (var i = from; i <= to; i++)
            {
                var gi = glyphs[i];

                // A complex-script shaping run is measured once, as the whole run, from its first glyph; its other members add nothing.
                if (gi.ShapingRunFirst is not null && !ReferenceEquals(gi.ShapingRunFirst, gi))
                    continue;

                var text = gi.ShapingRunFirst is not null ? gi.RunText ?? gi.Glyph : gi.Glyph;
                if (string.IsNullOrWhiteSpace(text) || (isAuto && IsCjk(text)))
                    continue;

                var font = gi.Font;
                var crossings = g.GetInkCrossings(text, font, new PaintPoint(gi.Px, gi.Py - font.Ascent), y - half, y + half, gi.Run.LetterSpacing, ResolveShapingFeatures(gi));
                if (crossings is null)
                    continue;

                // A glyph textLength stretched paints about its own pen position, so its crossings are stretched the same way.
                foreach (var crossing in crossings)
                    into.Add(new DecorationInterval(gi.Px + (crossing.Start - gi.Px) * gi.GlyphScale, gi.Px + (crossing.End - gi.Px) * gi.GlyphScale).Dilated(clearance));
            }
        }

        /// <summary>
        /// Paints <paramref name="run"/>'s <c>text-shadow</c> layers underneath the text (CSS Text Decoration 3 §4.1: the first shadow is on top, so they
        /// paint last to first), the way <c>FragmentPainter.PaintTextShadows</c> does for HTML: a shadow with no blur is the text drawn again at an offset
        /// in the shadow colour and stays vector; a blurred one is drawn into a layer that is Gaussian-blurred (radius = twice the standard deviation).
        /// A glyph painted under <paramref name="glyphTransform"/> (a rotation, a stretch, a position along a path) gets its offset applied after that
        /// transform, in user space; a blurred one is drawn through the transform into a layer sized to the envelope of the transformed glyph box.
        /// </summary>
        private static void PaintTextShadows(Canvas g, SvgTextElement run, string text, Font font, double drawX, double drawY, Size size, double opacity,
            double letterSpacing, ShapeSettings? features, string? logicalText, Matrix3x2? glyphTransform = null)
        {
            if (run.TextShadows.Count == 0 || g.InvisibleText)
                return;

            var fill = ResolveInMarker(run.Fill);
            var textColor = fill.Kind == SvgPaintKind.Solid ? fill.PaintColor : PaintColor.Black;

            for (var i = run.TextShadows.Count - 1; i >= 0; i--)
            {
                var shadow = run.TextShadows[i];
                var color = ApplyOpacity(shadow.Color ?? textColor, opacity * run.FillOpacity);
                if (color.A == 0)
                    continue;

                if (glyphTransform is { } transform)
                {
                    var shadowMatrix = MultiplyMatrix(transform, Matrix3x2.CreateTranslation((float)shadow.Dx, (float)shadow.Dy));
                    if (shadow.Blur <= 0)
                    {
                        g.PushTransform(shadowMatrix);
                        g.DrawString(text, font, color, new PaintPoint(drawX, drawY), size, letterSpacing, run.Palette, features, logicalText);
                        g.PopTransform();
                        continue;
                    }

                    // The layer's bounds are in the coordinates of the canvas that began it, so they are the transformed glyph box's envelope.
                    var envelope = SvgGeometryBounds.TransformBounds(new Rect(drawX, drawY, size.Width, size.Height), shadowMatrix);
                    var spread = 1.5 * shadow.Blur + size.Height * 0.25;
                    var clipBox = g.GetClip();
                    var envLeft = Math.Max(envelope.X - spread, clipBox.X - spread);
                    var envTop = Math.Max(envelope.Y - spread, clipBox.Y - spread);
                    var envRight = Math.Min(envelope.Right + spread, clipBox.Right + spread);
                    var envBottom = Math.Min(envelope.Bottom + spread, clipBox.Bottom + spread);
                    if (envRight <= envLeft || envBottom <= envTop)
                        continue;

                    using var transformedLayer = g.BeginLayer(new LayerOptions(Bounds: new Rect(envLeft, envTop, envRight - envLeft, envBottom - envTop), Effects: [new BlurEffect(shadow.Blur / 2)]));
                    if (transformedLayer is null)
                        continue;

                    transformedLayer.Canvas.PushTransform(shadowMatrix);
                    transformedLayer.Canvas.DrawString(text, font, color, new PaintPoint(drawX, drawY), size, letterSpacing, run.Palette, features, logicalText);
                    transformedLayer.Canvas.PopTransform();
                    continue;
                }

                var point = new PaintPoint(drawX + shadow.Dx, drawY + shadow.Dy);
                if (shadow.Blur <= 0)
                {
                    g.DrawString(text, font, color, point, size, letterSpacing, run.Palette, features, logicalText);
                    continue;
                }

                // Glyph ink can overhang the run's own box a little; the padding keeps it.
                var margin = 1.5 * shadow.Blur + size.Height * 0.25;
                var clip = g.GetClip();
                var left = Math.Max(point.X - margin, clip.X - margin);
                var top = Math.Max(point.Y - margin, clip.Y - margin);
                var right = Math.Min(point.X + size.Width + margin, clip.Right + margin);
                var bottom = Math.Min(point.Y + size.Height + margin, clip.Bottom + margin);
                if (right <= left || bottom <= top)
                    continue;

                using var layer = g.BeginLayer(new LayerOptions(Bounds: new Rect(left, top, right - left, bottom - top), Effects: [new BlurEffect(shadow.Blur / 2)]));
                layer?.Canvas.DrawString(text, font, color, point, size, letterSpacing, run.Palette, features, logicalText);
            }
        }

        private static bool IsCjk(string text)
        {
            foreach (var rune in text.EnumerateRunes())
            {
                var v = rune.Value;
                if (v is >= 0x3040 and <= 0x30FF or >= 0x3400 and <= 0x9FFF or >= 0xAC00 and <= 0xD7AF or >= 0xF900 and <= 0xFAFF or >= 0x20000 and <= 0x2FFFF)
                    return true;
            }

            return false;
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
        private static void PaintRotatedGlyph(Canvas g, SvgDocument document, GlyphInfo start, Font font, double degrees, double opacity)
        {
            var glyphSize = start.Size;
            var glyphTransform = GlyphTransform(start, degrees);
            // A shadow's offset is in user space, so it is applied after the glyph's rotation rather than inside it.
            PaintTextShadows(g, start.Run, start.Glyph, font, start.Px, start.Py - font.Ascent, glyphSize, opacity * start.Opacity,
                start.Run.LetterSpacing, start.Run.ShapingFeatures, start.LogicalGlyph, glyphTransform);
            g.PushTransform(glyphTransform);
            PaintTextGlyphs(g, document, start.Run, start.Glyph, font, start.Px, start.Py - font.Ascent, glyphSize, opacity * start.Opacity,
                start.Run.LetterSpacing, start.Run.ShapingFeatures, start.LogicalGlyph, paintShadows: false);
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
        /// When the font also carries a real <c>VORG</c> table (<see cref="Font.HasVerticalOrigin"/> -
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
        /// whenever either <see cref="Font.HasVerticalMetrics"/> or <see cref="Font.HasVerticalOrigin"/>
        /// is true, not just the former: a VORG-shifted anchor can push the painted span past the
        /// reserved cell even when <see cref="GlyphInfo.Advance"/> is the line-height fallback (its "the
        /// advance already equals the full painted span" guarantee assumes an unshifted anchor).
        /// </summary>
        private static void PaintUprightGlyph(Canvas g, SvgDocument document, GlyphInfo start, Font font, double opacity)
        {
            var glyphSize = start.Size;
            var drawX = start.Px - glyphSize.Width / 2;
            var y = start.Py + start.OriginYOffset;

            // textLength's lengthAdjust="spacingAndGlyphs" stretches an upright glyph down the column, about its own pen position.
            var scaled = start.IsScaled;
            var paintShadows = !scaled;
            var columnScale = Matrix3x2.Identity;
            if (scaled)
            {
                columnScale = MultiplyMatrix(MultiplyMatrix(new Matrix3x2(1, 0, 0, 1, (float)-start.Px, (float)-start.Py), Matrix3x2.CreateScale(1f, (float)start.GlyphScale)),
                    new Matrix3x2(1, 0, 0, 1, (float)start.Px, (float)start.Py));
                PaintTextShadows(g, start.Run, start.Glyph, font, drawX, y, glyphSize, opacity * start.Opacity,
                    start.Run.LetterSpacing, start.Run.ShapingFeatures, start.LogicalGlyph, columnScale);
                g.PushTransform(columnScale);
            }

            if (font.HasVerticalMetrics || font.HasVerticalOrigin)
            {
                // Only the block (Y) axis needs bounding - the cross axis has no overlap risk to guard
                // against, so this just needs to be generous enough to never itself clip real glyph ink.
                // An actually-unbounded Rect (double.MinValue/MaxValue) breaks under the viewBox-to-
                // viewport transform PushTransform/RenderInto already has active here (the extreme
                // coordinates overflow through that matrix multiply), which silently produced an empty
                // effective clip and made every upright glyph invisible - a finite, merely-generous margin
                // avoids that without reintroducing any real cross-axis clipping risk.
                var crossAxisMargin = Math.Max(glyphSize.Width, font.Size) * 8;
                g.PushClip(new Rect(start.Px - crossAxisMargin, start.Py, crossAxisMargin * 2, start.Advance / start.GlyphScale));
                PaintTextGlyphs(g, document, start.Run, start.Glyph, font, drawX, y, glyphSize, opacity * start.Opacity,
                    start.Run.LetterSpacing, start.Run.ShapingFeatures, start.LogicalGlyph, paintShadows: paintShadows);
                g.PopClip();
            }
            else
            {
                PaintTextGlyphs(g, document, start.Run, start.Glyph, font, drawX, y, glyphSize, opacity * start.Opacity,
                    start.Run.LetterSpacing, start.Run.ShapingFeatures, start.LogicalGlyph, paintShadows: paintShadows);
            }

            if (scaled)
                g.PopTransform();
        }

        /// <summary>
        /// Paints one straight-baseline group of characters (<paramref name="text"/>, all sharing one run's
        /// font/fill/stroke) at a given top-left origin. Plain solid, non-stroked text keeps the fast
        /// <see cref="Canvas.DrawString(string, Font, PaintColor, PaintPoint, Size, double, FontPalette?, ShapeSettings?)"/> path (a single-color PDF text show, so it stays
        /// selectable and tagged-PDF-friendly). A gradient/pattern <c>fill</c> or any <c>stroke</c>
        /// needs the glyphs as an addressable vector path (<see cref="Canvas.GetTextOutline"/>),
        /// filled/stroked through the same brush/pen machinery shapes use, with the same text drawn again
        /// invisibly so it stays selectable (<see cref="PaintInvisibleText"/>). A bitmap-only font yields no
        /// outline, so it goes through the raster backend instead (<see cref="PaintBitmapGlyphs"/>).
        /// <paramref name="logicalText"/> is <paramref name="text"/>'s true logical-order source,
        /// positionally aligned with it (see <c>PeachDrawing.Text.Internal.Fonts.CMapInfo.AddShapedText</c>'s own remarks) -
        /// null (the common case) when this run of characters was never bidi-mirrored.
        /// </summary>
        private static void PaintTextGlyphs(Canvas g, SvgDocument document, SvgTextElement run, string text, Font font, double drawX, double drawY, Size size, double opacity,
            double letterSpacing = 0, ShapeSettings? features = null, string? logicalText = null, bool paintShadows = true)
        {
            if (paintShadows)
                PaintTextShadows(g, run, text, font, drawX, drawY, size, opacity, letterSpacing, features, logicalText);

            // Inside a marker, context-fill / context-stroke are the paints of the shape the marker is drawn on - same as a shape's own
            // fill/stroke (PaintShape). Outside a marker this is a no-op (the tree builder already resolved these through `use`).
            var fill = ResolveInMarker(run.Fill);
            var stroke = ResolveInMarker(run.Stroke);

            var hasStroke = stroke.Kind != SvgPaintKind.None && run.StrokeWidth > 0;
            var needsOutline = fill.Kind is SvgPaintKind.GradientRef or SvgPaintKind.PatternRef || hasStroke;

            if (!needsOutline)
            {
                // Fast path: solid fill (or no fill at all) with no stroke.
                if (fill.Kind != SvgPaintKind.Solid)
                    return;

                var solid = ApplyOpacity(fill.PaintColor, opacity * run.FillOpacity);
                g.DrawString(text, font, solid, new PaintPoint(drawX, drawY), size, letterSpacing, fontPalette: run.Palette, features: features, logicalText: logicalText);
                return;
            }

            // DrawString positions from the top-left of the line box; the outline places the baseline
            // directly, so shift down by the ascent. The measured box (top-left drawX/drawY, size) is
            // the objectBoundingBox reference for gradient/pattern paint - SvgGeometryBounds can't
            // measure text statically.
            var baseline = new PaintPoint(drawX, drawY + font.Ascent);
            var outline = g.GetTextOutline(text, font, baseline, letterSpacing, features);

            if (outline is null)
            {
                // No outline source (a bitmap-only font): the glyphs can't be traced, so draw them through the raster backend and
                // let the gradient/pattern fill show through their coverage. The stroke is a band around the coverage edge.
                PaintBitmapGlyphs(g, document, run, text, font, new PaintPoint(drawX, drawY), size, opacity, letterSpacing, features, logicalText, fill, stroke, hasStroke, new Rect(drawX, drawY, size.Width, size.Height));
                return;
            }

            // `size` comes from Canvas.MeasureString, which (like HTML's own CssBox/CssLayoutEngine
            // word measurement) has no letterSpacing parameter of its own - widen it the same
            // established way those callers do, via CountShapedGlyphs, so the objectBoundingBox
            // reference actually bounds the letter-spaced outline painted below rather than the
            // narrower unspaced advance.
            var spacedWidth = size.Width + (letterSpacing != 0 ? g.CountShapedGlyphs(text, font, features) * letterSpacing : 0);
            var textBounds = new Rect(drawX, drawY, spacedWidth, size.Height);

            PaintOutlinedGlyph(g, document, run, fill, stroke, hasStroke, outline, opacity, textBounds);
            PaintInvisibleText(g, run, text, font, new PaintPoint(drawX, drawY), size, letterSpacing, features, logicalText);
        }

        /// <summary>
        /// Fills and strokes one glyph (or run) outline through the same brush/pen machinery shapes use, fill then stroke unless
        /// <c>paint-order</c> says otherwise (SVG 2 §13.6). A gradient/pattern that came through context-fill/context-stroke is measured
        /// against the context element (<see cref="ContextBounds"/>), not <paramref name="bounds"/>, the same rule <see cref="PaintShape"/> follows.
        /// Disposes <paramref name="outline"/>.
        /// </summary>
        private static void PaintOutlinedGlyph(Canvas g, SvgDocument document, SvgTextElement run, SvgPaint fill, SvgPaint stroke, bool hasStroke, GraphicsPath outline, double opacity, Rect bounds)
        {
            void PaintFill()
            {
                if (fill.Kind == SvgPaintKind.None)
                    return;

                var fillBounds = ContextBounds(g, fill) ?? bounds;
                if (fill.Kind == SvgPaintKind.PatternRef)
                {
                    PaintPatternFill(g, document, run, outline, opacity * run.FillOpacity, fillBounds, fill);
                }
                else
                {
                    var brush = ResolvePaintBrush(g, document, run, fill, opacity * run.FillOpacity, fillBounds);
                    if (brush is not null)
                        g.DrawPath(brush, outline);
                }
            }

            void PaintStroke()
            {
                if (!hasStroke)
                    return;

                var strokeBounds = ContextBounds(g, stroke) ?? bounds;
                var pen = ResolveStrokePen(g, document, run, opacity * run.StrokeOpacity, strokeBounds, stroke);
                if (pen is not null)
                    g.DrawPath(pen, outline);
            }

            if (run.StrokeFirst)
            {
                PaintStroke();
                PaintFill();
            }
            else
            {
                PaintFill();
                PaintStroke();
            }

            outline.Dispose();
        }

        /// <summary>
        /// Draws <paramref name="text"/> a second time as invisible text (PDF text render mode 3) over glyphs that were painted as vector
        /// outlines or a bitmap, so it stays selectable and searchable - the same device <c>FragmentPainter.PaintSelectableText</c> uses for
        /// an HTML subtree drawn as a bitmap.
        /// </summary>
        private static void PaintInvisibleText(Canvas g, SvgTextElement run, string text, Font font, PaintPoint topLeft, Size size, double letterSpacing, ShapeSettings? features, string? logicalText)
        {
            g.InvisibleText = true;
            try
            {
                g.DrawString(text, font, PaintColor.Black, topLeft, size, letterSpacing, fontPalette: run.Palette, features: features, logicalText: logicalText);
            }
            finally
            {
                g.InvisibleText = false;
            }
        }

        /// <summary>
        /// Paints a glyph run whose font has no outlines to trace (a bitmap-only font), through the raster backend: the run is drawn black
        /// into a raster surface to get its coverage, each paint (a gradient/pattern fill, and a stroke) is painted over the same area into
        /// a surface of its own and cut to a mask made from that coverage, and the result is drawn. The fill's mask is the coverage; the
        /// stroke's is a band centred on the edge of the glyphs (the coverage grown by half the stroke width, less the coverage shrunk by it).
        /// A solid fill with no stroke is the ordinary text show. <paramref name="area"/> is the run's box in the current coordinate system.
        /// </summary>
        private static void PaintBitmapGlyphs(Canvas g, SvgDocument document, SvgTextElement run, string text, Font font, PaintPoint topLeft, Size size, double opacity,
            double letterSpacing, ShapeSettings? features, string? logicalText, SvgPaint fill, SvgPaint stroke, bool hasStroke, Rect area)
        {
            if (fill.Kind == SvgPaintKind.Solid && !hasStroke)
            {
                g.DrawString(text, font, ApplyOpacity(fill.PaintColor, opacity * run.FillOpacity), topLeft, size, letterSpacing, fontPalette: run.Palette, features: features, logicalText: logicalText);
                return;
            }

            var rasterFill = fill.Kind != SvgPaintKind.None;
            if (!rasterFill && !hasStroke)
                return;

            // Room for the bitmap's own overhang past the advance box, and for a stroke's reach.
            var margin = area.Height * 0.25 + (hasStroke ? run.StrokeWidth : 0);
            var bounds = new Rect(area.X - margin, area.Y - margin, area.Width + 2 * margin, area.Height + 2 * margin);
            using var coverage = g.BeginRasterSurface(bounds);
            if (coverage is null)
                return;

            coverage.Graphics.DrawString(text, font, PaintColor.Black, topLeft, size, letterSpacing, fontPalette: run.Palette, features: features, logicalText: logicalText);

            var surface = coverage.Surface;
            var cover = new byte[surface.Width * surface.Height];
            var pixels = surface.Pixels;
            for (var i = 0; i < cover.Length; i++)
                cover[i] = pixels[i * 4 + 3];

            byte[]? band = null;
            if (hasStroke)
            {
                // Half the stroke width, in pixels of the surface's grid.
                var radius = Math.Clamp((int)Math.Round(run.StrokeWidth / 2 * Math.Max(surface.PixelsPerUnitX, surface.PixelsPerUnitY)), 1, 48);
                var grown = MorphologyFilter(cover, surface.Width, surface.Height, radius, grow: true);
                var shrunk = MorphologyFilter(cover, surface.Width, surface.Height, radius, grow: false);
                band = new byte[cover.Length];
                for (var i = 0; i < band.Length; i++)
                    band[i] = (byte)Math.Max(0, grown[i] - shrunk[i]);
            }

            void PaintThroughMask(SvgPaint paint, double paintOpacity, byte[] mask)
            {
                using var layer = g.BeginRasterSurface(bounds);
                if (layer is null)
                    return;

                using (var box = layer.Graphics.GetGraphicsPath())
                {
                    box.Start(bounds.X, bounds.Y);
                    box.LineTo(bounds.Right, bounds.Y);
                    box.LineTo(bounds.Right, bounds.Bottom);
                    box.LineTo(bounds.X, bounds.Bottom);
                    box.CloseFigure();

                    var paintBounds = ContextBounds(g, paint) ?? area;
                    if (paint.Kind == SvgPaintKind.PatternRef)
                    {
                        PaintPatternFill(layer.Graphics, document, run, box, paintOpacity, paintBounds, paint);
                    }
                    else if (ResolvePaintBrush(layer.Graphics, document, run, paint, paintOpacity, paintBounds) is { } brush)
                    {
                        layer.Graphics.DrawPath(brush, box);
                    }
                }

                // Both surfaces cover the same pixels (premultiplied RGBA8), so cutting the paint to the mask is a per-pixel scale.
                var layerPixels = layer.Surface.Pixels;
                for (var i = 0; i < mask.Length && i * 4 + 3 < layerPixels.Length; i++)
                {
                    var alpha = mask[i];
                    if (alpha == 255)
                        continue;

                    for (var c = 0; c < 4; c++)
                        layerPixels[i * 4 + c] = (byte)((layerPixels[i * 4 + c] * alpha + 127) / 255);
                }

                g.DrawRaster(layer.Surface);
            }

            void PaintFill()
            {
                if (rasterFill)
                    PaintThroughMask(fill, opacity * run.FillOpacity, cover);
            }

            void PaintStroke()
            {
                if (band is not null)
                    PaintThroughMask(stroke, opacity * run.StrokeOpacity, band);
            }

            if (run.StrokeFirst)
            {
                PaintStroke();
                PaintFill();
            }
            else
            {
                PaintFill();
                PaintStroke();
            }

            PaintInvisibleText(g, run, text, font, topLeft, size, letterSpacing, features, logicalText);
        }

        /// <summary>
        /// Grows (a maximum filter) or shrinks (a minimum filter) an 8-bit coverage map by <paramref name="radius"/> pixels, as two passes
        /// of a square window - cheap, and close enough to a round one for the thin band a stroke makes.
        /// </summary>
        private static byte[] MorphologyFilter(byte[] source, int width, int height, int radius, bool grow)
        {
            var horizontal = new byte[source.Length];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var value = grow ? (byte)0 : (byte)255;
                    for (var k = Math.Max(0, x - radius); k <= Math.Min(width - 1, x + radius); k++)
                    {
                        var v = source[y * width + k];
                        value = grow ? Math.Max(value, v) : Math.Min(value, v);
                    }

                    horizontal[y * width + x] = value;
                }
            }

            var result = new byte[source.Length];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var value = grow ? (byte)0 : (byte)255;
                    for (var k = Math.Max(0, y - radius); k <= Math.Min(height - 1, y + radius); k++)
                    {
                        var v = horizontal[k * width + x];
                        value = grow ? Math.Max(value, v) : Math.Min(value, v);
                    }

                    result[y * width + x] = value;
                }
            }

            return result;
        }

        /// <summary>
        /// A <c>&lt;textPath&gt;</c> run laid out along its path: the measured path, which way it is read, and each glyph that lands on it.
        /// </summary>
        private sealed class TextPathLayout(SvgTextPathGeometry geometry, bool right, List<PlacedPathGlyph> glyphs)
        {
            public SvgTextPathGeometry Geometry { get; } = geometry;

            /// <summary>Whether the path is read from its far end (<c>side="right"</c>).</summary>
            public bool Right { get; } = right;

            public List<PlacedPathGlyph> Glyphs { get; } = glyphs;
        }

        /// <summary>One glyph of a <see cref="TextPathLayout"/> with where it sits on the path.</summary>
        private sealed class PlacedPathGlyph(GlyphInfo info, double advance, double mid, double extraDy, Matrix3x2 frame)
        {
            public GlyphInfo Info { get; } = info;

            /// <summary>The glyph's natural advance (before any <c>textLength</c> stretch), which is the width of its cell.</summary>
            public double Advance { get; } = advance;

            /// <summary>Where the glyph's centre falls, measured along the path.</summary>
            public double Mid { get; } = mid;

            /// <summary>How far the glyph sits off the path's own baseline, along its normal (<c>dy</c>, baseline alignment and shift).</summary>
            public double ExtraDy { get; } = extraDy;

            /// <summary>The rigid frame (translate to the point on the path, turn to its tangent plus any per-character <c>rotate</c>), scaled along the glyph for <c>textLength</c>. The glyph's own origin is its centre on the baseline.</summary>
            public Matrix3x2 Frame { get; } = frame;
        }

        /// <summary>
        /// Lays a <c>&lt;textPath&gt;</c>'s glyphs along its referenced path (a <c>&lt;path&gt;</c> or a basic
        /// shape): the run's own text and any nested <c>&lt;tspan&gt;</c>s are flattened in document order,
        /// and each glyph is placed at its own midpoint distance along the path (honoring <c>startOffset</c>,
        /// <c>text-anchor</c>, per-character <c>dx</c>/<c>dy</c>/<c>rotate</c>, and <c>side</c>). A glyph whose
        /// midpoint falls off the path is dropped. Null when the run has no usable path or no glyphs.
        /// </summary>
        private static TextPathLayout? LayoutTextPath(Canvas g, SvgTextElement run)
        {
            if (run.PathData is not { } segments)
                return null;

            var geometry = new SvgTextPathGeometry(segments);
            var totalLength = geometry.TotalLength;
            if (totalLength <= 0)
                return null;

            // Flatten the textPath's own text plus nested <tspan>s (a nested <textPath> is out of scope and
            // dropped). Each glyph carries its owning run (font/paint) and its assigned dx/dy/rotate.
            var glyphs = new List<GlyphInfo>();
            var ignoredTextPaths = new List<(SvgTextElement, double)>();
            var overrides = new List<EmbeddingSpan>();
            FlattenRun(run, 1.0, glyphs, ignoredTextPaths, overrides);
            if (glyphs.Count == 0)
                return null;

            // A <textPath> always flows its glyphs along the path's own tangent, regardless of
            // writing-mode - there is no vertical variant of this layout (out of scope, matching real
            // UA behavior: a vertical <text> containing a <textPath> still lays that descendant out
            // horizontally along the path).
            ApplyBidiReordering(run, glyphs, overrides, isVertical: false);

            // Px holds each glyph's distance along the path from the start of the text (dx shifts the position along the path), so textLength
            // - which spreads or stretches that distance - can be applied by the same code as straight text.
            var along = 0.0;
            foreach (var gi in glyphs)
            {
                gi.Advance = g.MeasureString(gi.Glyph, gi.Font, gi.Run.ShapingFeatures).Width + gi.Run.LetterSpacing;
                if (gi.Run.WordSpacing != 0 && IsWhitespaceGlyph(gi.Glyph))
                    gi.Advance += gi.Run.WordSpacing;
                var glyphDx = gi.Dx ?? 0;
                gi.Px = along + glyphDx;
                along += gi.Advance + glyphDx;
            }

            ApplyTextLength(glyphs, isVertical: false);

            double runWidth = 0;
            foreach (var gi in glyphs)
                runWidth += gi.Advance;

            var startOffset = (run.StartOffsetIsPercent ? run.StartOffset * totalLength : run.StartOffset)
                + run.TextAnchor switch
                {
                    SvgTextAnchor.Middle => -runWidth / 2,
                    SvgTextAnchor.End => -runWidth,
                    _ => 0,
                };

            var right = run.Side == SvgTextPathSide.Right;
            var placed = new List<PlacedPathGlyph>(glyphs.Count);
            foreach (var gi in glyphs)
            {
                // The run's baseline alignment and shift move the glyph off the path's own baseline, along the path's normal, like a dy does.
                var extraDy = (gi.Dy ?? 0) + BaselineOffset(gi.Run);

                // The part of the advance that is the glyph's own (textLength's spacing adds a gap after it), and the glyph's natural advance before any stretch.
                var slot = gi.Advance - gi.GapAdded;
                var advance = slot / gi.GlyphScale;

                var mid = startOffset + gi.Px + slot / 2;

                // side="right" reads the path in reverse (measured from the far end, glyphs flipped 180°); dy offsets the glyph
                // perpendicular to the path; the glyph turns to the tangent plus any per-character rotate. A glyph centred off
                // either end of the path is not rendered.
                if (PathText.GetGlyphFrame(geometry.Measure, mid, right ? PathTextSide.Right : PathTextSide.Left, extraDy, gi.Rotate ?? 0) is not { } frame)
                    continue;

                // A glyph textLength stretched is scaled along the path about its centre, inside the glyph's frame.
                placed.Add(new PlacedPathGlyph(gi, advance, mid, extraDy, gi.IsScaled ? MultiplyMatrix(Matrix3x2.CreateScale((float)gi.GlyphScale, 1f), frame) : frame));
            }

            return placed.Count == 0 ? null : new TextPathLayout(geometry, right, placed);
        }

        /// <summary>
        /// Where the glyph-local point (<paramref name="lx"/>, <paramref name="ly"/>) lands when the glyph is bent along the path
        /// (<c>method="stretch"</c>): <paramref name="lx"/> (from the glyph's centre) picks the distance along the path and
        /// <paramref name="ly"/> (from the baseline, y down) the offset along the path's normal at that distance. A point past either end
        /// of the path takes the end's position and direction.
        /// </summary>
        private static PaintPoint WarpPathPoint(TextPathLayout layout, PlacedPathGlyph placed, double lx, double ly)
        {
            var scale = placed.Info.IsScaled ? placed.Info.GlyphScale : 1.0;
            var length = layout.Geometry.TotalLength;

            // A per-character rotate turns the glyph about its own origin before it is bent, as it does the rigid frame.
            if (placed.Info.Rotate is { } degrees && degrees != 0)
            {
                var radians = degrees * (Math.PI / 180.0);
                (lx, ly) = (lx * Math.Cos(radians) - ly * Math.Sin(radians), lx * Math.Sin(radians) + ly * Math.Cos(radians));
            }

            var distance = placed.Mid + lx * scale;
            var sample = layout.Geometry.Measure.PointAtLength(Math.Clamp(layout.Right ? length - distance : distance, 0, length));
            var tangent = (sample.TangentDegrees + (layout.Right ? 180 : 0)) * (Math.PI / 180.0);
            var offset = ly + placed.ExtraDy;
            return new PaintPoint(sample.X - Math.Sin(tangent) * offset, sample.Y + Math.Cos(tangent) * offset);
        }

        /// <summary>
        /// Bends a glyph outline (built centred on the origin at its baseline) along the path, point by point: it is flattened, and each
        /// edge cut into short pieces so a long straight edge follows the curve instead of cutting across it. Disposes
        /// <paramref name="outline"/>.
        /// </summary>
        private static GraphicsPath WarpOutline(Canvas g, GraphicsPath outline, TextPathLayout layout, PlacedPathGlyph placed)
        {
            var warped = g.GetGraphicsPath();
            warped.FillMode = FillMode.Nonzero;

            // About 1/32 of an em: fine enough that a bend is smooth, coarse enough that a glyph stays a few hundred points.
            var step = Math.Max(placed.Info.Font.Height / 32.0, 0.05);

            foreach (var contour in outline.Flatten(0.05))
            {
                var points = contour.Points;
                if (points.Count == 0)
                    continue;

                var first = WarpPathPoint(layout, placed, points[0].X, points[0].Y);
                warped.AddMove(first.X, first.Y);

                void LineThrough(PaintPoint from, PaintPoint to)
                {
                    var pieces = Math.Clamp((int)Math.Ceiling(Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y)) / step), 1, 64);
                    for (var k = 1; k <= pieces; k++)
                    {
                        var t = (double)k / pieces;
                        var p = WarpPathPoint(layout, placed, from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
                        warped.LineTo(p.X, p.Y);
                    }
                }

                for (var i = 1; i < points.Count; i++)
                    LineThrough(points[i - 1], points[i]);

                if (contour.Closed)
                {
                    LineThrough(points[^1], points[0]);
                    warped.CloseFigure();
                }
            }

            outline.Dispose();
            return warped;
        }

        /// <summary>
        /// The bounding box of the laid-out text in user space: the union of every glyph's cell (its advance wide, from the font's ascent
        /// to its descent - the box SVG measures a text's bounding box from), turned to the path, or bent along it for
        /// <c>method="stretch"</c>. This is what <c>objectBoundingBox</c> paint, clipping and masking on a <c>&lt;textPath&gt;</c> measure against.
        /// </summary>
        private static Rect TextPathBounds(TextPathLayout layout, bool stretch)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

            void Extend(double x, double y)
            {
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }

            foreach (var placed in layout.Glyphs)
            {
                var half = placed.Advance / 2;
                var top = -placed.Info.Font.Ascent;
                var bottom = top + placed.Info.Font.Height;

                // A bent cell is not a rectangle, so its edges are sampled; a turned one has only its corners.
                var columns = stretch ? 8 : 1;
                for (var c = 0; c <= columns; c++)
                {
                    var x = -half + placed.Advance * c / columns;
                    foreach (var y in (ReadOnlySpan<double>)[top, bottom])
                    {
                        if (stretch)
                        {
                            var p = WarpPathPoint(layout, placed, x, y);
                            Extend(p.X, p.Y);
                        }
                        else
                        {
                            var p = Vector2.Transform(new Vector2((float)x, (float)y), placed.Frame);
                            Extend(p.X, p.Y);
                        }
                    }
                }
            }

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// Renders a <c>&lt;textPath&gt;</c> (see <see cref="LayoutTextPath"/> for the placement). Each glyph paints in its own run's
        /// font/fill/stroke via <see cref="PaintPathGlyph"/>.
        /// </summary>
        private static void RenderTextPath(Canvas g, SvgDocument document, SvgTextElement run, double inheritedOpacity)
        {
            if (LayoutTextPath(g, run) is not { } layout)
                return;

            var opacity = inheritedOpacity * run.Opacity;
            var stretch = run.Method == SvgTextPathMethod.Stretch;
            var bounds = TextPathBounds(layout, stretch);

            foreach (var placed in layout.Glyphs)
            {
                var gi = placed.Info;

                // A shadow's offset is in user space, so it is applied after the frame, the way a rotated straight glyph's is.
                if (gi.Run.TextShadows.Count > 0)
                {
                    var shadowSize = g.MeasureString(gi.Glyph, gi.Font, gi.Run.ShapingFeatures);
                    PaintTextShadows(g, gi.Run, gi.Glyph, gi.Font, -placed.Advance / 2, -gi.Font.Ascent, shadowSize, opacity * gi.Opacity,
                        0, gi.Run.ShapingFeatures, gi.LogicalGlyph, placed.Frame);
                }

                PaintPathGlyph(g, document, layout, placed, stretch, opacity * gi.Opacity, bounds);
            }
        }

        /// <summary>
        /// Paints one glyph of a <c>&lt;textPath&gt;</c>. Plain solid, unstroked text turned to the path keeps the fast text show under the
        /// glyph's frame (selectable as it is). Anything that needs the glyph as geometry - a gradient/pattern fill, a stroke, or being
        /// bent along the path - paints the outline in user space (so a <c>userSpaceOnUse</c> paint server stays fixed and an
        /// <c>objectBoundingBox</c> one measures against the whole text, <paramref name="bounds"/>) and adds invisible text in the glyph's
        /// frame so it stays selectable. A font with no outlines (a bitmap font) is turned rigidly and filled through its raster coverage.
        /// <paramref name="opacity"/> already includes the glyph's own.
        /// </summary>
        private static void PaintPathGlyph(Canvas g, SvgDocument document, TextPathLayout layout, PlacedPathGlyph placed, bool stretch, double opacity, Rect bounds)
        {
            var gi = placed.Info;
            var run = gi.Run;

            // Inside a marker, context-fill / context-stroke are the paints of the shape the marker is drawn on - see PaintTextGlyphs.
            var fill = ResolveInMarker(run.Fill);
            var stroke = ResolveInMarker(run.Stroke);

            var hasStroke = stroke.Kind != SvgPaintKind.None && run.StrokeWidth > 0;
            var needsOutline = fill.Kind is SvgPaintKind.GradientRef or SvgPaintKind.PatternRef || hasStroke || stretch;

            var leftX = -placed.Advance / 2;
            var glyphSize = g.MeasureString(gi.Glyph, gi.Font, run.ShapingFeatures);
            var topLeft = new PaintPoint(leftX, -gi.Font.Ascent);

            if (!needsOutline)
            {
                if (fill.Kind != SvgPaintKind.Solid)
                    return;

                g.PushTransform(placed.Frame);
                g.DrawString(gi.Glyph, gi.Font, ApplyOpacity(fill.PaintColor, opacity * run.FillOpacity), topLeft, glyphSize, letterSpacing: 0, fontPalette: run.Palette, features: run.ShapingFeatures, logicalText: gi.LogicalGlyph);
                g.PopTransform();
                return;
            }

            var outline = g.GetTextOutline(gi.Glyph, gi.Font, new PaintPoint(leftX, 0), features: run.ShapingFeatures);
            if (outline is null)
            {
                g.PushTransform(placed.Frame);
                PaintBitmapGlyphs(g, document, run, gi.Glyph, gi.Font, topLeft, glyphSize, opacity, 0, run.ShapingFeatures, gi.LogicalGlyph, fill, stroke, hasStroke, new Rect(leftX, -gi.Font.Ascent, glyphSize.Width, glyphSize.Height));
                g.PopTransform();
                return;
            }

            if (stretch)
            {
                outline = WarpOutline(g, outline, layout, placed);
            }
            else
            {
                outline.Transform(placed.Frame);
            }

            PaintOutlinedGlyph(g, document, run, fill, stroke, hasStroke, outline, opacity, bounds);

            // Text that paints nothing (fill none, no stroke) is not supplied either, as for unstretched text.
            if (fill.Kind != SvgPaintKind.None || hasStroke)
            {
                g.PushTransform(placed.Frame);
                PaintInvisibleText(g, run, gi.Glyph, gi.Font, topLeft, glyphSize, 0, run.ShapingFeatures, gi.LogicalGlyph);
                g.PopTransform();
            }
        }

        private static void RenderElement(Canvas g, SvgDocument document, SvgElement element, double inheritedOpacity, (double Width, double Height) viewport)
        {
            // A backdrop repaint ends where the element it is repainting for begins.
            if (SvgBackdropSlot.Get(g) is { } backdrop && backdrop.ShouldSkip(element))
                return;

            var opacity = inheritedOpacity * element.Opacity;
            var pushedTransform = false;
            var pushedClip = false;

            if (element.Transform is { } transform)
            {
                g.PushTransform(transform);
                pushedTransform = true;
            }

            GraphicsPath? clipPath = null;

            if (element.ClipPathRef is { } clipRef && document.ClipPaths.TryGetValue(clipRef, out var clipDefinition))
            {
                // objectBoundingBox: map the clipPath's 0..1 child geometry onto the referencing
                // element's bounding box (SVG 1.1 §14.3.5). The clip is built in the element's local space
                // (element.Transform is already pushed above), the same space GetBoundingBox reports, so
                // the mapping is Matrix3x2(w, 0, 0, h, x, y). A missing/zero bbox falls back to no mapping.
                Matrix3x2? unitsMatrix = null;
                if (!clipDefinition.ClipPathUnitsUserSpaceOnUse &&
                    ElementBounds(g, element) is { Width: > 0, Height: > 0 } bbox)
                {
                    unitsMatrix = new Matrix3x2((float)bbox.Width, 0, 0, (float)bbox.Height, (float)bbox.X, (float)bbox.Y);
                }

                clipPath = BuildClipPath(g, clipDefinition, unitsMatrix);

                if (clipPath is not null)
                {
                    g.PushClip(clipPath);
                    pushedClip = true;
                }
            }

            // Checked FIRST: SVG's own compositing pipeline applies filter, then clip-path, then mask,
            // then opacity (SVG 2 §3, CSS Masking/Compositing). clip-path is already pushed onto `g`
            // above, so whichever branch below draws the final composited result through the ordinary
            // paint path picks up that clip for free - "filter, then clip" falls out without extra code.
            // An element with a filter never ALSO goes through the mask/opacity-group branches below in
            // this implementation (same mutually-exclusive shape those two branches already have with
            // each other) - combining a filter with its own element's mask/opacity on top is a narrow
            // edge case left for a future change, not attempted here.
            if (element.FilterRef is { } filterRef && document.Filters.TryGetValue(filterRef, out var filter))
                RenderFilteredElementContent(g, document, element, filter, opacity, viewport);
            else if (element.MaskRef is { } maskRef && document.Masks.TryGetValue(maskRef, out var mask))
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
            SvgUseElement { Target: SvgUseElement inner } => NeedsContainerOpacityGroup(inner),
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
        private static void RenderContainerOpacityGroup(Canvas g, SvgDocument document, SvgElement element, double inheritedOpacity, (double Width, double Height) viewport)
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
            // A descendant's own `transform` IS folded into the bounds (UnionOpacityGroupBounds composes it the
            // same way SvgGeometryBounds.UnionAll does), so a child carrying a translate/scale is still sized
            // correctly, not just approximately. A <use> of a <use> of a container is routed here too
            // (NeedsContainerOpacityGroup unwraps <use> chains).
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

            using var layer = g.BeginLayer(new LayerOptions(element.Opacity, Bounds: new Rect(x, y, width, height)));
            if (layer is null)
            {
                // No page/document context (a measure-only pass - BeginLayer returns null there) - keep
                // the graceful direct fallback rather than throwing. Tested by
                // Opacity_SvgGroupOpacity_NoPageContext_FallsBackToDirectRender.
                RenderElementSwitch(g, document, element, inheritedOpacity * element.Opacity, viewport);
                return;
            }

            RenderElementSwitch(layer.Canvas, document, element, inheritedOpacity, viewport);
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
        private static Rect? GetOpacityGroupBounds(Canvas g, SvgElement element, (double Width, double Height) viewport) => element switch
        {
            SvgTextElement text => MeasureTextBounds(g, text),
            SvgImageElement { Width: > 0, Height: > 0 } image => new Rect(image.X, image.Y, image.Width, image.Height),
            SvgForeignObjectElement { Width: > 0, Height: > 0 } foreign => new Rect(foreign.X, foreign.Y, foreign.Width, foreign.Height),
            SvgNestedSvgElement { Width: > 0, Height: > 0 } nestedSvg => new Rect(nestedSvg.X, nestedSvg.Y, nestedSvg.Width, nestedSvg.Height),
            SvgUseElement { Target: SvgSymbolElement } use => new Rect(use.X, use.Y, use.Width ?? viewport.Width, use.Height ?? viewport.Height),
            SvgUseElement { Target: SvgNestedSvgElement nestedTarget } use => new Rect(use.X, use.Y, use.Width ?? nestedTarget.Width, use.Height ?? nestedTarget.Height),
            SvgUseElement { Target: { } target } use => OffsetBounds(GetOpacityGroupBounds(g, target, viewport), use.X, use.Y),
            SvgGroupElement group => UnionOpacityGroupBounds(g, group.Children, viewport),
            _ => SvgGeometryBounds.GetBoundingBox(element),
        };

        private static Rect? OffsetBounds(Rect? rect, double dx, double dy) =>
            rect is { } r ? new Rect(r.X + dx, r.Y + dy, r.Width, r.Height) : null;

        private static Rect? UnionOpacityGroupBounds(Canvas g, IEnumerable<SvgElement> elements, (double Width, double Height) viewport)
        {
            Rect? result = null;

            foreach (var element in elements)
            {
                if (GetOpacityGroupBounds(g, element, viewport) is not { } b)
                    continue;

                // Same composition SvgGeometryBounds.UnionAll makes: a child's own transform has to be folded in
                // before unioning, or a translated/scaled child is sized as if it sat at its own untransformed
                // position, silently clipping it against the tile's margin (or, previously, the tile itself).
                if (element.Transform is { } transform)
                    b = SvgGeometryBounds.TransformBounds(b, transform);

                result = result is { } r ? UnionRects(r, b) : b;
            }

            return result;
        }

        private static Rect UnionRects(Rect a, Rect b)
        {
            var minX = Math.Min(a.X, b.X);
            var minY = Math.Min(a.Y, b.Y);
            var maxX = Math.Max(a.X + a.Width, b.X + b.Width);
            var maxY = Math.Max(a.Y + a.Height, b.Y + b.Height);
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// The bounding box <c>objectBoundingBox</c> units resolve against: <see cref="SvgGeometryBounds.GetBoundingBox"/> for geometry, and for
        /// a <c>&lt;text&gt;</c> (which that can't measure without a font) its glyph cells as laid out for painting.
        /// </summary>
        private static Rect? ElementBounds(Canvas g, SvgElement element) =>
            element is SvgTextElement text ? MeasureTextBounds(g, text, forPaintExtent: false) : SvgGeometryBounds.GetBoundingBox(element);

        /// <summary>
        /// Local-space bounds of a <c>&lt;text&gt;</c> and its descendants, computed from the exact same
        /// flatten + layout <see cref="RenderText"/> paints with (so the opacity-group tile region and the
        /// painted glyphs can't drift). Each straight glyph contributes its measured box (rotated by its own
        /// per-character <c>rotate</c> about its position); a <c>&lt;textPath&gt;</c> descendant contributes
        /// its flattened path's bbox inflated by the font ascent. The tile's own -10%/+20% margin absorbs slack.
        /// With <paramref name="forPaintExtent"/> false it is instead the text's bounding box proper - the union of the glyph cells, with no
        /// shadow reach and a <c>&lt;textPath&gt;</c>'s cells where the glyphs actually sit - which is what <c>objectBoundingBox</c> units measure.
        /// </summary>
        private static Rect? MeasureTextBounds(Canvas g, SvgTextElement text, bool forPaintExtent = true)
        {
            Rect? result = null;

            var glyphs = new List<GlyphInfo>();
            var textPaths = new List<(SvgTextElement Run, double ParentOpacity)>();
            var overrides = new List<EmbeddingSpan>();
            FlattenRun(text, 1.0, glyphs, textPaths, overrides);

            if (glyphs.Count > 0)
            {
                var isVertical = IsVerticalWritingMode(text.WritingMode);
                if (!isVertical)
                    ResolveComplexScriptRuns(glyphs);
                LayoutTextBlock(g, text, glyphs, overrides, isVertical);
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
                        var rotated = new Rect(gi.Px, gi.Py - gi.Font.Ascent, size.Width * gi.GlyphScale, size.Height);
                        result = result is { } r1 ? UnionRects(r1, RotateRectBounds(rotated, explicitDegrees, gi.Px, gi.Py)) : RotateRectBounds(rotated, explicitDegrees, gi.Px, gi.Py);
                        continue;
                    }

                    // Matches PaintUprightGlyph/PaintRotatedGlyph's own box shapes exactly - see their
                    // remarks for why Py needs no ascent adjustment in the upright case.
                    var box = isVertical && gi.IsUpright
                        ? new Rect(gi.Px - size.Width / 2, gi.Py, size.Width, gi.Font.Height * gi.GlyphScale)
                        : isVertical
                            ? RotateRectBounds(new Rect(gi.Px, gi.Py - gi.Font.Ascent, size.Width * gi.GlyphScale, size.Height), 90.0, gi.Px, gi.Py)
                            : new Rect(gi.Px, gi.Py - gi.Font.Ascent, size.Width * gi.GlyphScale, size.Height);

                    result = result is { } r ? UnionRects(r, box) : box;
                }

                // A shadow paints outside the glyph boxes by its offset plus the reach of its blur.
                var reach = 0.0;
                foreach (var gi in forPaintExtent ? glyphs : [])
                {
                    foreach (var shadow in gi.Run.TextShadows)
                        reach = Math.Max(reach, Math.Max(Math.Abs(shadow.Dx), Math.Abs(shadow.Dy)) + 1.5 * shadow.Blur + gi.Size.Height * 0.25);
                }

                if (reach > 0 && result is { } measured)
                    result = new Rect(measured.X - reach, measured.Y - reach, measured.Width + 2 * reach, measured.Height + 2 * reach);
            }

            foreach (var (run, _) in textPaths)
            {
                if (!forPaintExtent)
                {
                    if (LayoutTextPath(g, run) is { } layout)
                    {
                        var cells = TextPathBounds(layout, run.Method == SvgTextPathMethod.Stretch);
                        result = result is { } placedSoFar ? UnionRects(placedSoFar, cells) : cells;
                    }

                    continue;
                }

                if (run.PathData is not { } pathSegments || run.Font is not { } pathFont)
                    continue;

                var geometry = new SvgTextPathGeometry(pathSegments);
                if (geometry.IsEmpty)
                    continue;

                var inflate = pathFont.Ascent;
                var pathBox = geometry.Bounds;
                foreach (var shadow in run.TextShadows)
                    inflate = Math.Max(inflate, pathFont.Ascent + Math.Max(Math.Abs(shadow.Dx), Math.Abs(shadow.Dy)) + 1.5 * shadow.Blur);
                var runBox = new Rect(pathBox.X - inflate, pathBox.Y - inflate, pathBox.Width + 2 * inflate, pathBox.Height + 2 * inflate);
                result = result is { } existing ? UnionRects(existing, runBox) : runBox;
            }

            return result;
        }

        /// <summary>Axis-aligned envelope of <paramref name="rect"/> rotated <paramref name="degrees"/> about (<paramref name="pivotX"/>, <paramref name="pivotY"/>) - matches the pivot <see cref="PaintGlyphs"/> rotates its glyphs around.</summary>
        private static Rect RotateRectBounds(Rect rect, double degrees, double pivotX, double pivotY)
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

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// Renders <paramref name="element"/> (which has its own <c>mask="url(#...)"</c>) into a
        /// fresh tile sized to the mask's resolved region, then composites that tile onto the page in
        /// one atomic placement (<see cref="Canvas.DrawImageMasked"/>) with the mask's own tile
        /// attached - see <see cref="Canvas.DrawImageMasked"/>'s doc comment for why this (rather
        /// than a simpler-looking "push the mask as ambient state, render normally, pop it" approach)
        /// is required for the mask to land in the same place as the content it's masking.
        /// </summary>
        /// <summary>
        /// Delegates to <see cref="SvgFilterEvaluator.Render(Canvas, SvgFilter, SvgElement, Rect?, Action{Canvas}, SvgFilterInputs?)"/>, supplying its <c>SourceGraphic</c> input
        /// as a callback that paints <paramref name="element"/>'s own ordinary content - the same
        /// <see cref="RenderElementSwitch"/> call <see cref="RenderMaskedElementContent"/> makes for its
        /// mask tile, at the same (already inheritedOpacity*element.Opacity-multiplied)
        /// <paramref name="opacity"/> that method already bakes into its own content tile, for
        /// consistency with this renderer's existing (not fully spec-order-strict, but already
        /// established) convention rather than introducing a second, different opacity-timing rule.
        /// </summary>
        private static void RenderFilteredElementContent(Canvas g, SvgDocument document, SvgElement element, SvgFilter filter, double opacity, (double Width, double Height) viewport) =>
            SvgFilterEvaluator.Render(g, filter, element, new Rect(0, 0, viewport.Width, viewport.Height), tg => RenderElementSwitch(tg, document, element, opacity, viewport),
                filter.RequiresRaster ? new RendererFilterInputs(g, document, element, viewport) : null);

        [ThreadStatic]
        private static SvgDocument? s_backdropDocument;

        [ThreadStatic]
        private static ISvgPageBackdrop? s_pageBackdrop;

        /// <summary>
        /// Binds the page behind <paramref name="document"/> for the duration of its paint (the HTML painter does this around an inline
        /// SVG whose filters read <c>BackgroundImage</c>), returning what was bound before so the caller can put it back. Bound to one
        /// document, so an unrelated SVG painted meanwhile (through the backdrop repaint of the page) never picks it up.
        /// </summary>
        internal static (SvgDocument? Document, ISvgPageBackdrop? Page) BindPageBackdrop(SvgDocument? document, ISvgPageBackdrop? page)
        {
            var previous = (s_backdropDocument, s_pageBackdrop);
            s_backdropDocument = document;
            s_pageBackdrop = page;
            return previous;
        }

        private static ISvgPageBackdrop? PageBackdropFor(SvgDocument document) =>
            ReferenceEquals(s_backdropDocument, document) ? s_pageBackdrop : null;

        /// <summary>How many backdrop repaints may nest inside one another: each one repaints part of the document.</summary>
        private const int MaxBackdropDepth = 3;

        /// <summary>How deep filter inputs that render other content (an <c>feImage</c> naming an element) may nest, which stops one that names an element filtered by itself.</summary>
        private const int MaxFilterInputDepth = 4;

        [ThreadStatic]
        private static int s_filterInputDepth;

        /// <summary>How many pattern tiles / marker instances may be painted inside one another. Content inherits from the definition's own ancestors, so a pattern or marker can end up painting itself (or a cycle of them); real documents nest a level or two.</summary>
        private const int MaxDefinitionNesting = 4;

        [ThreadStatic]
        private static int s_definitionNesting;

        /// <summary>The painted inputs of one raster filter evaluation, drawn with this renderer's own paint code.</summary>
        private sealed class RendererFilterInputs(Canvas owner, SvgDocument document, SvgElement element, (double Width, double Height) viewport) : SvgFilterInputs
        {
            // Inside a marker, context-fill / context-stroke (a filter's FillPaint/StrokePaint input on a marker shape) are the paints of
            // the shape the marker is drawn on - same resolution PaintShape/PaintTextGlyphs use; a no-op outside a marker.
            public override SvgPaint PaintOf(bool stroke) => ResolveInMarker(stroke ? element.Stroke : element.Fill);

            public override void PaintPaint(Canvas g, bool stroke, Rect region)
            {
                var paint = PaintOf(stroke);

                // The paint is the element's own, so an objectBoundingBox gradient or pattern is measured against the element, not the
                // region - unless it came through context-fill/context-stroke, which measures against the context element instead.
                var bounds = ContextBounds(g, paint) ?? SvgFilterEvaluator.ElementBounds(element, new Rect(0, 0, viewport.Width, viewport.Height));
                var rect = new SvgRectElement { X = region.X, Y = region.Y, Width = region.Width, Height = region.Height, Fill = paint, Stroke = SvgPaint.None };
                using var path = BuildRectPath(g, rect);

                if (paint.Kind == SvgPaintKind.PatternRef)
                {
                    PaintPatternFill(g, document, rect, path, 1.0, bounds);
                }
                else if (ResolvePaintBrush(g, document, rect, paint, 1.0, bounds) is { } brush)
                {
                    g.DrawPath(brush, path);
                }
            }

            public override void PaintImage(Canvas g, FeImage image, Rect subregion, double offsetX, double offsetY)
            {
                if (s_filterInputDepth >= MaxFilterInputDepth)
                    return;

                s_filterInputDepth++;
                try
                {
                    g.PushClip(subregion);

                    if (image.Image is { } stand)
                    {
                        // A stand-alone image fills the primitive subregion by its own preserveAspectRatio, like an <image> of that size.
                        RenderImage(g, new SvgImageElement
                        {
                            X = subregion.X,
                            Y = subregion.Y,
                            Width = subregion.Width,
                            Height = subregion.Height,
                            PreserveAspectRatio = stand.PreserveAspectRatio,
                            Image = stand.Image,
                            NestedDocument = stand.NestedDocument,
                        }, 1.0);
                    }
                    else if (image.Target is { } target)
                    {
                        // A referenced element keeps the filtered element's user space; a subregion x/y moves its origin.
                        var moved = offsetX != 0 || offsetY != 0;
                        if (moved)
                            g.PushTransform(new Matrix3x2(1, 0, 0, 1, (float)offsetX, (float)offsetY));

                        RenderElement(g, document, target, 1.0, viewport);

                        if (moved)
                            g.PopTransform();
                    }

                    g.PopClip();
                }
                finally
                {
                    s_filterInputDepth--;
                }
            }

            /// <summary>The bounding rectangle, in layout space, of <paramref name="region"/> (user space) under <paramref name="toLayout"/>.</summary>
            private static Rect LayoutBounds(Matrix3x2 toLayout, Rect region)
            {
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                for (var i = 0; i < 4; i++)
                {
                    var x = i % 2 == 0 ? region.Left : region.Right;
                    var y = i < 2 ? region.Top : region.Bottom;
                    var lx = x * toLayout.M11 + y * toLayout.M21 + toLayout.M31;
                    var ly = x * toLayout.M12 + y * toLayout.M22 + toLayout.M32;
                    minX = Math.Min(minX, lx);
                    maxX = Math.Max(maxX, lx);
                    minY = Math.Min(minY, ly);
                    maxY = Math.Max(maxY, ly);
                }

                return new Rect(minX, minY, maxX - minX, maxY - minY);
            }

            public override bool PaintBackdrop(Canvas g, Rect region)
            {
                // Only the graphics that paints the document's own content knows how to repaint what came before; a group's isolated
                // tile has no backdrop, and neither does a document that is not being painted with one.
                if (SvgBackdropSlot.Get(owner) is not { } context || context.Depth >= MaxBackdropDepth || !owner.CurrentTransform.TryInvert(out var toUserSpace))
                    return false;

                // The page layer: the page is drawn in layout space, so put layout space into this element's user space.
                if (context.Page is { } page)
                {
                    // Layout code culls against the current clip, so hand it the region in the space it paints in.
                    g.PushTransform(toUserSpace);
                    g.PushClip(LayoutBounds(owner.CurrentTransform, region));
                    page.Paint(g);
                    g.PopClip();
                    g.PopTransform();
                }

                // The SVG's own layer: everything before the element, painted from the root down and stopped at the element.
                var repaint = new SvgBackdropContext(context.Document, context.Page)
                {
                    Frame = context.Frame,
                    ViewportRect = context.ViewportRect,
                    ViewBoxMatrix = context.ViewBoxMatrix,
                    Viewport = context.Viewport,
                    Depth = context.Depth + 1,
                    StopElement = element,
                };

                // The document is clipped to its viewport when painted, so what overflows it is not part of the backdrop either.
                SvgBackdropSlot.Set(g, repaint);
                g.PushTransform(context.Frame.Then(toUserSpace));
                g.PushClip(context.ViewportRect);
                g.PushTransform(context.ViewBoxMatrix);

                foreach (var child in context.Document.Children)
                {
                    RenderElement(g, context.Document, child, 1.0, context.Viewport);
                    if (repaint.Stopped)
                        break;
                }

                g.PopTransform();
                g.PopClip();
                g.PopTransform();
                SvgBackdropSlot.Set(g, null);
                return true;
            }
        }

        private static void RenderMaskedElementContent(Canvas g, SvgDocument document, SvgElement element, SvgMask mask, double opacity, (double Width, double Height) viewport)
        {
            var (x, y, width, height) = ResolveMaskRect(g, element, mask);
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
                content.Graphics.PushTransform(new Matrix3x2(1, 0, 0, 1, (float)-x, (float)-y));

            RenderElementSwitch(content.Graphics, document, element, opacity, viewport);

            if (pushedOffset)
                content.Graphics.PopTransform();

            content.Graphics.Dispose();

            g.DrawImageMasked(content.Image, maskImage, new Rect(x, y, width, height));
        }

        private static void RenderElementSwitch(Canvas g, SvgDocument document, SvgElement element, double opacity, (double Width, double Height) viewport)
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
                    // The shape is closed implicitly for fill purposes only (SVG 1.1 §9.6); the stroke stays open.
                    using var fillPath = BuildPolylineFillPath(g, polyline);
                    PaintShape(g, document, polyline, graphicsPath, opacity, fillPath);
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

                case SvgForeignObjectElement foreign:
                    RenderForeignObject(g, foreign);
                    break;

                case SvgTextElement text:
                    RenderText(g, document, text, opacity);
                    break;

                case SvgUseElement { Target: { } target } use:
                {
                    var pushedUseOffset = use.X != 0 || use.Y != 0;
                    if (pushedUseOffset)
                        g.PushTransform(new Matrix3x2(1, 0, 0, 1, (float)use.X, (float)use.Y));

                    switch (target)
                    {
                        // A <symbol> has no size of its own - it's sized entirely by the referencing
                        // <use>'s width/height, defaulting to the current (ambient) viewport's size
                        // when <use> doesn't specify them (spec's 100% default). Passing `use` as
                        // RenderViewport's contextElement records this viewport's own children's frame
                        // under it, the same role the default arm's own s_paintContextFrames write plays
                        // below - see RenderViewport's contextElement doc and
                        // SvgGeometryBounds.GetUseTargetBoundingBox for the matching (pre-mapping) box.
                        case SvgSymbolElement symbol:
                            RenderViewport(g, document, 0, 0, use.Width ?? viewport.Width, use.Height ?? viewport.Height, symbol.ViewBox, symbol.PreserveAspectRatio, symbol.Children, opacity, use);
                            break;

                        // A nested <svg> target already has its own resolved size; <use>'s width/height
                        // only override it when actually specified.
                        case SvgNestedSvgElement nestedTarget:
                            RenderViewport(g, document, 0, 0, use.Width ?? nestedTarget.Width, use.Height ?? nestedTarget.Height, nestedTarget.ViewBox, nestedTarget.PreserveAspectRatio, nestedTarget.Children, opacity, use);
                            break;

                        default:
                        {
                            // The frame target's own content paints in (post target.Transform, matching the frame
                            // SvgGeometryBounds.GetBoundingBox(target) reports its bbox in) - what ContextBounds
                            // maps a gradient/pattern tagged with this use as its context element out of, once it
                            // reaches a shape further down that actually paints with it (SvgUseElement.Fill/Stroke's
                            // OfContextElement(use) in SvgTreeBuilder). Recorded under the use itself (a fresh
                            // object per use occurrence), not the target, so a use of a use resolves each level's
                            // context paint against its own frame.
                            var targetFrame = target.Transform is { } t ? MultiplyMatrix(t, g.CurrentTransform) : g.CurrentTransform;
                            var hadOuterFrame = s_paintContextFrames.TryGetValue(use, out var outerFrame);
                            s_paintContextFrames[use] = targetFrame;
                            try
                            {
                                RenderElement(g, document, target, opacity, viewport);
                            }
                            finally
                            {
                                if (hadOuterFrame) s_paintContextFrames[use] = outerFrame; else s_paintContextFrames.Remove(use);
                            }

                            break;
                        }
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
        /// luminosity source in <see cref="Canvas.DrawImageMasked"/>. Unlike <see cref="RenderViewport"/> (used for
        /// <c>&lt;pattern&gt;</c>/<c>&lt;symbol&gt;</c>/nested <c>&lt;svg&gt;</c>), a mask doesn't
        /// establish its own viewBox-scaled coordinate system - its content is drawn in ordinary
        /// user-space units, just positioned relative to the tile's own local origin rather than the
        /// mask region's <see cref="SvgMask.X"/>/<see cref="SvgMask.Y"/>.
        /// </summary>
        private static Image? BuildMaskTile(Canvas g, SvgDocument document, SvgElement owner, SvgMask mask)
        {
            var (x, y, width, height) = ResolveMaskRect(g, owner, mask);
            if (width <= 0 || height <= 0)
                return null;

            var tile = g.CreateTile(width, height);
            if (tile is not { } t)
                return null;

            var pushedOffset = x != 0 || y != 0;
            if (pushedOffset)
                t.Graphics.PushTransform(new Matrix3x2(1, 0, 0, 1, (float)-x, (float)-y));

            foreach (var child in mask.Children)
                RenderElement(t.Graphics, document, child, 1.0, (width, height));

            if (pushedOffset)
                t.Graphics.PopTransform();

            t.Graphics.Dispose();
            return t.Image;
        }

        /// <summary>Resolves a mask's region, same objectBoundingBox/userSpaceOnUse handling as <see cref="ResolveGradientPoint"/>/<see cref="ResolvePatternRect"/>.</summary>
        private static (double X, double Y, double Width, double Height) ResolveMaskRect(Canvas g, SvgElement owner, SvgMask mask)
        {
            if (mask.MaskUnitsUserSpaceOnUse)
                return (mask.X, mask.Y, mask.Width, mask.Height);

            if (ElementBounds(g, owner) is not { } bbox)
                return (mask.X, mask.Y, mask.Width, mask.Height);

            return (bbox.X + mask.X * bbox.Width, bbox.Y + mask.Y * bbox.Height, mask.Width * bbox.Width, mask.Height * bbox.Height);
        }

        private static void PaintShape(Canvas g, SvgDocument document, SvgElement element, GraphicsPath path, double opacity, GraphicsPath? fillPath = null)
        {
            // Per spec, <line> has no interior region - "fill" never applies to it, regardless of the
            // element's own/inherited fill paint (which otherwise defaults to solid black). Emitting a
            // fill op anyway would be visually harmless (PDF implicitly closes an open subpath before
            // filling, and a straight two-point "path" encloses zero area either way), but issuing a
            // real fill call is still wasted content-stream bytes and not what a real SVG renderer does.
            // Inside a marker, context-fill / context-stroke are the paints of the shape the marker is drawn on.
            var fill = ResolveInMarker(element.Fill);
            var stroke = ResolveInMarker(element.Stroke);

            if (element is not SvgLineElement && fill.Kind != SvgPaintKind.None)
            {
                // A gradient or pattern that came through context-fill is measured against the context element, not this one.
                var fillBounds = ContextBounds(g, fill);
                if (fill.Kind == SvgPaintKind.PatternRef)
                {
                    PaintPatternFill(g, document, element, fillPath ?? path, opacity * element.FillOpacity, fillBounds, fill);
                }
                else
                {
                    var brush = ResolvePaintBrush(g, document, element, fill, opacity * element.FillOpacity, fillBounds);
                    if (brush is not null)
                        g.DrawPath(brush, fillPath ?? path);
                }
            }

            if (stroke.Kind != SvgPaintKind.None && element.StrokeWidth > 0)
            {
                var pen = ResolveStrokePen(g, document, element, opacity * element.StrokeOpacity, ContextBounds(g, stroke), stroke);
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
        private static void PaintMarkers(Canvas g, SvgDocument document, SvgElement element, double opacity)
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

            // This shape is the context element of what its markers draw. Its own paint may itself be a context keyword (a shape inside a
            // marker), which is resolved against the marker it is in, before this shape's markers take over. A gradient/pattern paint keeps
            // whichever context element it already names (the nearest one wins - OfContextElement is a no-op once one is set) so a marker
            // nested inside another marker or a use still measures against the original context, not this shape; a plain gradient/pattern
            // authored directly on this shape's own fill/stroke gets tagged with this shape, so ContextBounds below has something to map from.
            var outer = s_markerContext;
            s_markerContext = new MarkerContext(ForMarker(element, ResolveInMarker(element.Fill)), ForMarker(element, ResolveInMarker(element.Stroke)));
            // The frame this shape's own content paints in - what a gradient/pattern paint tagged with this shape as its context element
            // needs mapped into the marker's own frame later (see ContextBounds). Saved/restored the same way s_markerContext is, in case
            // painting this shape's own markers somehow re-enters painting this same shape (already bounded by MaxDefinitionNesting).
            var hadOuterFrame = s_paintContextFrames.TryGetValue(element, out var outerFrame);
            s_paintContextFrames[element] = g.CurrentTransform;
            try
            {
                foreach (var vertex in vertices)
                {
                    var markerRef = vertex.IsStart ? element.MarkerStartRef : vertex.IsEnd ? element.MarkerEndRef : element.MarkerMidRef;

                    if (markerRef is not null && document.Markers.TryGetValue(markerRef, out var marker))
                        PaintMarker(g, document, marker, vertex, element.StrokeWidth, opacity);
                }
            }
            finally
            {
                s_markerContext = outer;
                if (hadOuterFrame) s_paintContextFrames[element] = outerFrame; else s_paintContextFrames.Remove(element);
            }
        }

        /// <summary>The paints of the shape whose markers are being drawn: what <c>context-fill</c> and <c>context-stroke</c> mean inside them.</summary>
        private readonly record struct MarkerContext(SvgPaint Fill, SvgPaint Stroke);

        [ThreadStatic]
        private static MarkerContext? s_markerContext;

        /// <summary>
        /// The frame (<see cref="Canvas.CurrentTransform"/> snapshot) each live context element's own content paints in, keyed by the
        /// element itself (a <c>use</c>, or the shape a marker is drawn on) - what <see cref="ContextBounds"/> maps a gradient/pattern's
        /// box out of, into whatever frame is active when the paint is actually resolved. Reference-keyed: <see cref="SvgElement"/> has no
        /// value equality, and a fresh instance is built per <c>use</c> occurrence, so the same key is never live for two different places
        /// in the tree at once. <see cref="ThreadStaticAttribute"/> like every other renderer-scoped field in this class.
        /// </summary>
        [ThreadStatic]
        private static Dictionary<SvgElement, Matrix3x2>? s_paintContextFramesField;

        private static Dictionary<SvgElement, Matrix3x2> s_paintContextFrames =>
            s_paintContextFramesField ??= new Dictionary<SvgElement, Matrix3x2>(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// A paint with its context keywords replaced, when it is drawn inside a marker (the only place the tree builder leaves them, because the
        /// context element differs for every instance). With no marker being drawn there is no context element, so no paint.
        /// </summary>
        private static SvgPaint ResolveInMarker(SvgPaint paint) => paint.Kind switch
        {
            SvgPaintKind.ContextFill => s_markerContext?.Fill ?? SvgPaint.None,
            SvgPaintKind.ContextStroke => s_markerContext?.Stroke ?? SvgPaint.None,
            _ => paint,
        };

        /// <summary>
        /// The paint a marker's content gets from <paramref name="element"/> (the shape the marker is drawn on): per SVG 2 (painting,
        /// "context paint"), a gradient/pattern paint server keeps the context element's own coordinate space and bounding box - tagging it
        /// with <paramref name="element"/> as its context element (a no-op if it already names one further out) is what lets
        /// <see cref="ContextBounds"/> later map that box into the marker content's frame via <see cref="s_paintContextFrames"/>, the same
        /// way a gradient/pattern reaching a shape through a <c>use</c> already does.
        /// </summary>
        private static SvgPaint ForMarker(SvgElement element, SvgPaint paint) => paint.OfContextElement(element);

        /// <summary>
        /// The box a gradient or pattern that came through context paint is measured against, remapped from the context element's own frame
        /// (recorded in <see cref="s_paintContextFrames"/> when its content began painting) into <paramref name="g"/>'s current one - the
        /// marker's placement matrix, or any transform between a <c>use</c>'s target and the shape actually painting, has moved the two
        /// apart. Null when the paint was the element's own (no context element), or when the context element's frame was never recorded.
        /// </summary>
        private static Rect? ContextBounds(Canvas g, SvgPaint paint)
        {
            if (paint.ContextElement is not { } context)
                return null;

            // What a use instantiates is drawn in the use's own coordinate system, already moved by its x and y (folded into the
            // recorded frame below, not applied here - see the use render switch). GetUseTargetBoundingBox (rather than plain
            // GetBoundingBox) is what measures a symbol/nested-svg target too - see its own remarks for why that can't just be
            // a new GetBoundingBox case instead.
            var geometrySource = context is SvgUseElement { Target: { } target } ? target : context;
            if (SvgGeometryBounds.GetUseTargetBoundingBox(geometrySource) is not { } box)
                return null;

            if (!s_paintContextFrames.TryGetValue(context, out var contextFrame))
                return box;

            if (!g.CurrentTransform.TryInvert(out var toCurrentFrame))
                return box;

            return TransformRectAabb(box, contextFrame.Then(toCurrentFrame));
        }

        /// <summary>The axis-aligned envelope of <paramref name="rect"/>'s four corners mapped through <paramref name="matrix"/>.</summary>
        private static Rect TransformRectAabb(Rect rect, Matrix3x2 matrix)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

            ReadOnlySpan<PaintPoint> corners =
            [
                new(rect.X, rect.Y), new(rect.Right, rect.Y),
                new(rect.X, rect.Bottom), new(rect.Right, rect.Bottom),
            ];

            foreach (var corner in corners)
            {
                var p = ApplyMatrix(corner, matrix);
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y);
                maxY = Math.Max(maxY, p.Y);
            }

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// Places one marker instance: establishes its own (markerWidth x markerHeight, optionally
        /// scaled by the host shape's stroke-width) viewport, rotated per <see cref="SvgMarkerElement.OrientAuto"/>/
        /// <see cref="SvgMarkerElement.OrientAngle"/> and positioned so (refX, refY) - resolved through
        /// the marker's own viewBox, if any - lands exactly on <paramref name="vertex"/>.
        /// </summary>
        private static void PaintMarker(Canvas g, SvgDocument document, SvgMarkerElement marker, MarkerVertex vertex, double strokeWidth, double opacity)
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
                var probeMatrix = ComputeViewportTransform(new Rect(0, 0, marker.MarkerWidth, marker.MarkerHeight), marker.ViewBox?.X ?? 0, marker.ViewBox?.Y ?? 0, viewBoxWidth, viewBoxHeight, marker.PreserveAspectRatio);
                var refPoint = ApplyMatrix(new PaintPoint(marker.RefX, marker.RefY), probeMatrix);
                refLocalX = refPoint.X;
                refLocalY = refPoint.Y;
            }

            var radians = rotation * (Math.PI / 180.0);
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);

            var preShift = new Matrix3x2(1, 0, 0, 1, (float)-refLocalX, (float)-refLocalY);
            var rotateScale = new Matrix3x2((float)(cos * scale), (float)(sin * scale), (float)(-sin * scale), (float)(cos * scale), 0, 0);
            var toVertex = new Matrix3x2(1, 0, 0, 1, (float)vertex.X, (float)vertex.Y);
            var placement = MultiplyMatrix(MultiplyMatrix(preShift, rotateScale), toVertex);

            if (s_definitionNesting >= MaxDefinitionNesting)
                return;

            g.PushTransform(placement);
            s_definitionNesting++;
            try
            {
                RenderViewport(g, document, 0, 0, marker.MarkerWidth, marker.MarkerHeight, marker.ViewBox, marker.PreserveAspectRatio, marker.Children, opacity);
            }
            finally
            {
                s_definitionNesting--;
            }

            g.PopTransform();
        }

        /// <summary>
        /// The bounding box <c>objectBoundingBox</c> paint (gradient/pattern) resolves against. Text
        /// runs pass their measured box as <paramref name="boundsOverride"/> because
        /// <see cref="SvgGeometryBounds.GetBoundingBox"/> can't measure a <c>&lt;text&gt;</c> statically;
        /// every non-text caller passes null and keeps the geometric bounds.
        /// </summary>
        private static Rect? OwnerBounds(SvgElement owner, Rect? boundsOverride)
            => boundsOverride ?? SvgGeometryBounds.GetBoundingBox(owner);

        private static Brush? ResolvePaintBrush(Canvas g, SvgDocument document, SvgElement owner, SvgPaint paint, double opacity, Rect? boundsOverride = null)
        {
            return paint.Kind switch
            {
                SvgPaintKind.Solid => g.GetSolidBrush(ApplyOpacity(paint.PaintColor, opacity)),
                SvgPaintKind.GradientRef when paint.ReferenceId is { } id && document.Gradients.TryGetValue(id, out var gradient)
                    => ResolveGradientBrush(g, owner, gradient, opacity, boundsOverride),
                _ => null,
            };
        }

        /// <summary>
        /// Fills <paramref name="path"/> with a tiled <c>&lt;pattern&gt;</c>: renders the pattern's own
        /// content once into a small Form XObject "tile" (via <see cref="Canvas.CreateTile"/>), then
        /// clips to the shape's own fill geometry and draws that SAME tile repeatedly across its
        /// bounding box. Each repeated draw is a reference to the one already-vector tile content, so
        /// this stays fully vector - never rasterizes, matching this renderer's core design principle
        /// - unlike a "render once to a bitmap, then repeat the bitmap" approach would.
        /// </summary>
        private static void PaintPatternFill(Canvas g, SvgDocument document, SvgElement element, GraphicsPath path, double opacity, Rect? boundsOverride = null,
            SvgPaint? paint = null)
        {
            if ((paint ?? element.Fill).ReferenceId is not { } id || !document.Patterns.TryGetValue(id, out var pattern))
                return;

            var (x, y, width, height) = ResolvePatternRect(element, pattern, boundsOverride);
            if (width <= 0 || height <= 0)
                return;

            if (s_definitionNesting >= MaxDefinitionNesting)
                return;

            var tile = g.CreateTile(width, height);
            if (tile is not { } t)
                return;

            s_definitionNesting++;
            try
            {
                RenderViewport(t.Graphics, document, 0, 0, width, height, pattern.ViewBox, pattern.PreserveAspectRatio, pattern.Children, opacity);
            }
            finally
            {
                s_definitionNesting--;
            }

            t.Graphics.Dispose();

            // The tile repeats from (x, y), with the pattern's own transform applied on top: a brush whose space starts at the cell's top-left
            // and whose transform carries both. Painting the shape with it fills exactly the part of the grid under the shape.
            var brushToUser = Matrix3x2.CreateTranslation((float)x, (float)y);
            if (pattern.PatternTransform is { } patternTransform)
                brushToUser = brushToUser.Then(patternTransform);

            using var brush = new TileBrush(t.Image, width, height, brushToUser);
            g.DrawPath(brush, path);
        }

        /// <summary>Resolves a pattern's tile rect, same objectBoundingBox/userSpaceOnUse handling as <see cref="ResolveGradientPoint"/>.</summary>
        private static (double X, double Y, double Width, double Height) ResolvePatternRect(SvgElement owner, SvgPattern pattern, Rect? boundsOverride = null)
        {
            if (pattern.PatternUnitsUserSpaceOnUse)
                return (pattern.X, pattern.Y, pattern.Width, pattern.Height);

            if (OwnerBounds(owner, boundsOverride) is not { } bbox)
                return (pattern.X, pattern.Y, pattern.Width, pattern.Height);

            return (bbox.X + pattern.X * bbox.Width, bbox.Y + pattern.Y * bbox.Height, pattern.Width * bbox.Width, pattern.Height * bbox.Height);
        }

        private static Brush? ResolveGradientBrush(Canvas g, SvgElement owner, SvgGradient gradient, double opacity, Rect? boundsOverride = null)
        {
            if (gradient.Stops.Count == 0)
                return null;

            var stops = gradient.Stops
                .Select(s => (PaintColor: ApplyOpacity(s.PaintColor, opacity), Position: s.Offset))
                .ToArray();

            var isRepeating = gradient.SpreadMethod != SvgSpreadMethod.Pad;
            var reflect = gradient.SpreadMethod == SvgSpreadMethod.Reflect;

            switch (gradient)
            {
                case SvgLinearGradient linear:
                {
                    var (x1, y1) = ResolveGradientPoint(owner, gradient, linear.X1, linear.Y1, boundsOverride);
                    var (x2, y2) = ResolveGradientPoint(owner, gradient, linear.X2, linear.Y2, boundsOverride);
                    var p1 = new PaintPoint(x1, y1);
                    var p2 = new PaintPoint(x2, y2);

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
                        (r, stops) = ExpandRadialSpread(owner, new PaintPoint(cx, cy), r, stops, reflect, boundsOverride);

                    // A rotation/skew turns the circle into a rotated ellipse, which two axis-aligned radii cannot
                    // state - the matrix travels with the brush instead. Translate/scale-only stays pre-applied.
                    if (gradient.GradientTransform is { } gt && (gt.M12 != 0 || gt.M21 != 0))
                        return g.GetRadialGradientBrush(new PaintPoint(cx, cy), r, r, stops, isRepeating, new PaintPoint(fx, fy),
                            GradientTransformInUserSpace(owner, gradient, gt, boundsOverride));

                    var center = ApplyMatrix(new PaintPoint(cx, cy), gradient.GradientTransform);
                    var focal = ApplyMatrix(new PaintPoint(fx, fy), gradient.GradientTransform);
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
        private static (PaintPoint P1, PaintPoint P2, (PaintColor PaintColor, double Position)[] Stops) ExpandLinearSpread(
            SvgElement owner, PaintPoint p1, PaintPoint p2, (PaintColor PaintColor, double Position)[] stops, bool reflect, Rect? boundsOverride = null)
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
                new PaintPoint(bbox.X, bbox.Y),
                new PaintPoint(bbox.X + bbox.Width, bbox.Y),
                new PaintPoint(bbox.X, bbox.Y + bbox.Height),
                new PaintPoint(bbox.X + bbox.Width, bbox.Y + bbox.Height),
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
            var newP1 = new PaintPoint(p1.X + kMin * dx, p1.Y + kMin * dy);
            var newP2 = new PaintPoint(p1.X + kMax * dx, p1.Y + kMax * dy);

            var expanded = new List<(PaintColor PaintColor, double Position)>(stops.Length * cycles);
            for (var k = kMin; k < kMax; k++)
            {
                var reflectedCycle = reflect && PositiveMod(k, 2) != 0;
                foreach (var stop in stops)
                {
                    var localPos = reflectedCycle ? 1 - stop.Position : stop.Position;
                    var newPos = (k - kMin + localPos) / cycles;
                    expanded.Add((stop.PaintColor, Math.Clamp(newPos, 0.0, 1.0)));
                }
            }

            expanded.Sort((a, b) => a.Position.CompareTo(b.Position));
            return (newP1, newP2, expanded.ToArray());
        }

        /// <summary>Radial counterpart of <see cref="ExpandLinearSpread"/> - tiles concentric rings outward from <paramref name="center"/> to cover <paramref name="owner"/>'s bounding box.</summary>
        private static (double R, (PaintColor PaintColor, double Position)[] Stops) ExpandRadialSpread(
            SvgElement owner, PaintPoint center, double r, (PaintColor PaintColor, double Position)[] stops, bool reflect, Rect? boundsOverride = null)
        {
            if (stops.Length < 2 || r < 1e-9 || OwnerBounds(owner, boundsOverride) is not { } bbox)
                return (r, stops);

            var corners = new[]
            {
                new PaintPoint(bbox.X, bbox.Y),
                new PaintPoint(bbox.X + bbox.Width, bbox.Y),
                new PaintPoint(bbox.X, bbox.Y + bbox.Height),
                new PaintPoint(bbox.X + bbox.Width, bbox.Y + bbox.Height),
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
            var expanded = new List<(PaintColor PaintColor, double Position)>(stops.Length * cycles);
            for (var k = 0; k < cycles; k++)
            {
                var reflectedCycle = reflect && k % 2 != 0;
                foreach (var stop in stops)
                {
                    var localPos = reflectedCycle ? 1 - stop.Position : stop.Position;
                    var newPos = (k + localPos) / cycles;
                    expanded.Add((stop.PaintColor, Math.Clamp(newPos, 0.0, 1.0)));
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
        private static (double X, double Y) ResolveGradientPoint(SvgElement owner, SvgGradient gradient, double rawX, double rawY, Rect? boundsOverride = null)
        {
            if (gradient.GradientUnitsUserSpaceOnUse)
                return (rawX, rawY);

            if (OwnerBounds(owner, boundsOverride) is not { } bbox)
                return (rawX, rawY);

            return (bbox.X + rawX * bbox.Width, bbox.Y + rawY * bbox.Height);
        }

        /// <summary>Same as <see cref="ResolveGradientPoint"/> but for a single scalar radius, scaled by the bounding box's spec-defined diagonal formula.</summary>
        private static double ResolveGradientRadius(SvgElement owner, SvgGradient gradient, double rawR, Rect? boundsOverride = null)
        {
            if (gradient.GradientUnitsUserSpaceOnUse)
                return rawR;

            if (OwnerBounds(owner, boundsOverride) is not { } bbox)
                return rawR;

            return rawR * Math.Sqrt((bbox.Width * bbox.Width + bbox.Height * bbox.Height) / 2.0);
        }

        private static Pen? ResolveStrokePen(Canvas g, SvgDocument document, SvgElement element, double opacity, Rect? boundsOverride = null,
            SvgPaint? paint = null)
        {
            Pen pen;
            var stroke = paint ?? element.Stroke;

            if (stroke.Kind == SvgPaintKind.Solid)
            {
                pen = g.GetPen(ApplyOpacity(stroke.PaintColor, opacity));
            }
            else if (stroke.Kind == SvgPaintKind.GradientRef &&
                     stroke.ReferenceId is { } id &&
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

        private static PaintColor ApplyOpacity(PaintColor color, double opacity)
        {
            if (opacity >= 1.0)
                return color;

            var alpha = (int)Math.Round(color.A * Math.Clamp(opacity, 0.0, 1.0));
            return PaintColor.FromArgb(alpha, color.R, color.G, color.B);
        }

        private static PaintPoint ApplyMatrix(PaintPoint p, Matrix3x2? matrix)
        {
            if (matrix is not { } m)
                return p;

            return new PaintPoint(p.X * m.M11 + p.Y * m.M21 + m.M31, p.X * m.M12 + p.Y * m.M22 + m.M32);
        }

        /// <summary>
        /// The user-space matrix equivalent of a <c>gradientTransform</c>. In <c>objectBoundingBox</c> units the
        /// transform acts inside the 0-1 box space (SVG 1.1 §13.2.2), so it is conjugated by the box matrix.
        /// </summary>
        private static Matrix3x2 GradientTransformInUserSpace(SvgElement owner, SvgGradient gradient, Matrix3x2 gt, Rect? boundsOverride)
        {
            if (gradient.GradientUnitsUserSpaceOnUse || OwnerBounds(owner, boundsOverride) is not { } bbox
                || bbox.Width <= 0 || bbox.Height <= 0)
                return gt;

            var box = new Matrix3x2((float)bbox.Width, 0, 0, (float)bbox.Height, (float)bbox.X, (float)bbox.Y);
            return Matrix3x2.Invert(box, out var inverse) ? inverse * gt * box : gt;
        }

        /// <summary>
        /// Transforms a radial gradient's radius as a pair of axis vectors (ignoring translation) -
        /// valid for the translate/scale-only <c>gradientTransform</c> subset. A rotated or skewed
        /// matrix travels with the brush instead (see <see cref="ResolveGradientBrush"/>).
        /// </summary>
        private static (double RadiusX, double RadiusY) ApplyMatrixToRadius(double r, Matrix3x2? matrix)
        {
            if (matrix is not { } m)
                return (r, r);

            return (Math.Abs(r * m.M11), Math.Abs(r * m.M22));
        }

        private static GraphicsPath BuildPath(Canvas g, SvgPathElement path)
        {
            var graphicsPath = g.GetGraphicsPath();
            graphicsPath.FillMode = path.FillRule;
            AppendPathSegments(graphicsPath, path.Segments);
            return graphicsPath;
        }

        private static GraphicsPath BuildCirclePath(Canvas g, SvgCircleElement circle)
        {
            var graphicsPath = g.GetGraphicsPath();
            graphicsPath.FillMode = circle.FillRule;
            AppendCircleGeometry(graphicsPath, circle);
            return graphicsPath;
        }

        private static GraphicsPath BuildPolygonPath(Canvas g, SvgPolygonElement polygon)
        {
            var graphicsPath = g.GetGraphicsPath();
            graphicsPath.FillMode = polygon.FillRule;
            AppendPolygonGeometry(graphicsPath, polygon);
            return graphicsPath;
        }

        private static GraphicsPath BuildPolylinePath(Canvas g, SvgPolylineElement polyline)
        {
            var graphicsPath = g.GetGraphicsPath();
            graphicsPath.FillMode = polyline.FillRule;
            AppendPolylineGeometry(graphicsPath, polyline);
            return graphicsPath;
        }

        /// <summary>The polyline's geometry closed back to its first point - for fill only; the stroke keeps the open path.</summary>
        private static GraphicsPath BuildPolylineFillPath(Canvas g, SvgPolylineElement polyline)
        {
            var graphicsPath = g.GetGraphicsPath();
            graphicsPath.FillMode = polyline.FillRule;
            AppendPolylinePoints(graphicsPath, polyline.Points);
            graphicsPath.CloseFigure();
            return graphicsPath;
        }

        private static GraphicsPath BuildRectPath(Canvas g, SvgRectElement rect)
        {
            var graphicsPath = g.GetGraphicsPath();
            graphicsPath.FillMode = rect.FillRule;
            AppendRectGeometry(graphicsPath, rect);
            return graphicsPath;
        }

        private static GraphicsPath BuildEllipsePath(Canvas g, SvgEllipseElement ellipse)
        {
            var graphicsPath = g.GetGraphicsPath();
            graphicsPath.FillMode = ellipse.FillRule;
            AppendEllipseGeometry(graphicsPath, ellipse);
            return graphicsPath;
        }

        private static GraphicsPath BuildLinePath(Canvas g, SvgLineElement line)
        {
            var graphicsPath = g.GetGraphicsPath();
            graphicsPath.FillMode = line.FillRule;
            AppendLineGeometry(graphicsPath, line);
            return graphicsPath;
        }

        /// <summary>
        /// Builds a clip path from a resolved <see cref="SvgClipPath"/>'s shapes, mapped through
        /// <paramref name="unitsMatrix"/>. Internal (not private) so <c>CssClipPathResolver</c> can
        /// reuse it for an HTML element's <c>clip-path: url(#id)</c> - every coordinate is baked
        /// directly into the returned path via <see cref="AppendClipShapeGeometry"/>/<see cref="AppendClipLeaf"/>
        /// rather than relying on any ambient <see cref="Canvas"/> transform still being pushed, so
        /// the caller can supply a synthetic matrix (translating/scaling into its own box-geometry
        /// space, already divided by <see cref="Canvas.PixelsPerPoint"/>) instead of the SVG-internal
        /// <c>objectBoundingBox</c>/<c>userSpaceOnUse</c> mapping <see cref="RenderElement"/> builds.
        /// </summary>
        internal static GraphicsPath? BuildClipPath(Canvas g, SvgClipPath clipPath, Matrix3x2? unitsMatrix)
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
        private static bool AppendClipShapeGeometry(Canvas g, GraphicsPath path, SvgElement shape, Matrix3x2? ambient)
        {
            var m = shape.Transform is { } t ? MultiplyMatrix(t, ambient ?? Matrix3x2.Identity) : ambient;

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
                        ? MultiplyMatrix(new Matrix3x2(1, 0, 0, 1, (float)use.X, (float)use.Y), m ?? Matrix3x2.Identity)
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
        private static bool AppendClipLeaf(Canvas g, GraphicsPath path, Matrix3x2? matrix, Action<GraphicsPath> build)
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
        /// (<see cref="PathSegmentKind.MoveTo"/>) uses <see cref="GraphicsPath.AddMove"/> rather than
        /// <see cref="GraphicsPath.Start"/> - safe even for the very first point of a brand new path
        /// (the underlying core path dedupes the resulting degenerate zero-length "connector" segment
        /// any subsequent draw call would otherwise implicitly add), and required for correctness when
        /// appending more than one subpath/shape into the same <see cref="GraphicsPath"/> (e.g. a
        /// multi-subpath <c>d</c> attribute, or a clip region built from several shapes).
        /// </summary>
        internal static void AppendPathSegments(GraphicsPath path, IReadOnlyList<PathSegment> segments)
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
        private static void AppendCircleGeometry(GraphicsPath path, SvgCircleElement circle)
        {
            var cx = circle.Cx;
            var cy = circle.Cy;
            var r = Math.Abs(circle.R);

            if (r <= 0)
                return;

            path.AddCircle(cx, cy, r);
        }

        private static void AppendPolygonGeometry(GraphicsPath path, SvgPolygonElement polygon)
        {
            AppendPolylinePoints(path, polygon.Points);
            path.CloseFigure();
        }

        /// <summary>
        /// Unlike <see cref="AppendPolygonGeometry"/>, deliberately does not close the figure - see
        /// <see cref="SvgPolylineElement"/>'s doc comment for the resulting (documented) fill/stroke
        /// simplification.
        /// </summary>
        private static void AppendPolylineGeometry(GraphicsPath path, SvgPolylineElement polyline) =>
            AppendPolylinePoints(path, polyline.Points);

        private static void AppendPolylinePoints(GraphicsPath path, PaintPoint[] points)
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
        private static void AppendRectGeometry(GraphicsPath path, SvgRectElement rect)
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
                path.AddRoundedRectangle(new Rect(x, y, width, height), 0);
                return;
            }

            path.AddRoundedRectangle(new Rect(x, y, width, height), rx, ry, rx, ry, rx, ry, rx, ry);
        }

        /// <summary>Same four-quarter-arc technique as <see cref="AppendCircleGeometry"/>, with independent x/y radii.</summary>
        private static void AppendEllipseGeometry(GraphicsPath path, SvgEllipseElement ellipse)
        {
            var cx = ellipse.Cx;
            var cy = ellipse.Cy;
            var rx = Math.Abs(ellipse.Rx);
            var ry = Math.Abs(ellipse.Ry);

            if (rx <= 0 || ry <= 0)
                return;

            path.AddEllipse(cx, cy, rx, ry);
        }

        /// <summary>An open (unclosed) two-point line - fill has no visible effect since it has zero area.</summary>
        private static void AppendLineGeometry(GraphicsPath path, SvgLineElement line)
        {
            path.AddMove(line.X1, line.Y1);
            path.LineTo(line.X2, line.Y2);
        }
    }
}

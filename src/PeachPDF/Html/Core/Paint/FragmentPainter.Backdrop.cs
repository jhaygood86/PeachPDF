using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Raster;
using PeachPDF.Raster.Filters;
using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Paint
{
    /// <summary>
    /// <c>backdrop-filter</c> (Filter Effects Level 2 §3.1): the filter applies to whatever was painted behind the element, inside its
    /// border box, and the result is drawn under the element's own background.
    /// </summary>
    /// <remarks>
    /// Paint is single-pass and forward-only, so what was painted behind an element is not kept anywhere. It is rebuilt on demand instead: a
    /// second painter walks the same fragment tree into a bitmap and stops at the moment it reaches the element, which is by construction
    /// exactly the content that came before it in paint order. The walk starts at the element's <em>backdrop root</em> - the nearest
    /// ancestor whose own group effect (opacity, filter, blend mode, clip-path, another backdrop-filter) isolates its content from what lies
    /// outside it - or at the page, over white paper, when there is none. Only what is inside the root is the backdrop.
    /// </remarks>
    internal sealed partial class FragmentPainter
    {
        /// <summary>How many backdrop-filter elements may nest their own backdrop repaint inside another's: each level repaints the page.</summary>
        private const int MaxBackdropDepth = 3;

        /// <summary>The page being painted, kept so a backdrop repaint can walk it again.</summary>
        private BoxFragment? _pageRoot;

        /// <summary>Set on a backdrop repaint painter: the fragment at which painting stops, because it and everything after it is not backdrop.</summary>
        private BoxFragment? _stopAt;

        /// <summary>True once <see cref="_stopAt"/> was reached; from then on nothing more is painted.</summary>
        private bool _stopped;

        /// <summary>How many backdrop repaints enclose this painter (0 for the page's own painter).</summary>
        private int _backdropDepth;

        /// <summary>
        /// Paints <paramref name="fragment"/>'s backdrop-filter, if it has one and it can be done, at the position its background is about to
        /// be painted (so it sits behind the background, borders and content).
        /// </summary>
        private void PaintBackdropFilter(RGraphics g, BoxFragment fragment, BoxDecorationGeometry geometry)
        {
            var box = fragment.Box;
            var functions = box.ActualBackdropFilterFunctions;
            if (functions.Count == 0 || _textOnly || _pageRoot is null || _backdropDepth >= MaxBackdropDepth)
                return;

            // A transformed element's backdrop lives in its parent's coordinate space, and a transformed ancestor between the element and its
            // backdrop root puts the two in different spaces: neither can be lined up with a bitmap taken in this space, so the filter is
            // not applied (the element paints normally).
            if (box.IsTransformed || !TryFindBackdropRoot(box, out var rootBox))
                return;

            var rootFragment = rootBox is null ? _pageRoot : FindFragment(_pageRoot, rootBox);
            if (rootFragment is null)
                return;

            var borderBox = geometry.DecorationRect;
            var region = Intersect(borderBox, g.GetClip());
            if (region.Width <= 0 || region.Height <= 0)
                return;

            var applied = new List<FilterGrammar.FilterFunction>();
            foreach (var function in functions)
            {
                // drop-shadow() shades the element's own silhouette; on a backdrop there is none.
                if (function.Name != "drop-shadow")
                    applied.Add(function);
            }

            using var scope = g.BeginRasterSurface(region);
            if (scope is null)
                return;

            // The page is paper: opaque white behind everything the document paints. A nested backdrop root starts transparent instead.
            if (rootBox is null)
            {
                FilterOps.Fill(scope.Surface, RColor.FromArgb(255, 255, 255, 255), 1.0);
                if (container.CanvasBackgroundBox is { } canvas)
                    PaintCanvasBackground(scope.Graphics, canvas, container.PageBoxRect);
            }

            var mirror = new FragmentPainter(container)
            {
                _stopAt = fragment,
                _taggingSuppressed = true,
                _backdropDepth = _backdropDepth + 1,
                _pageRoot = _pageRoot,
            };

            mirror.PaintTagged(scope.Graphics, rootFragment);
            if (!mirror._stopped)
            {
                // The element was never reached (it is not in this root's paint order); nothing sensible to filter.
                return;
            }

            FilterBackdrop(scope.Surface, applied, box, container.Adapter.MaxRasterPixels);

            // The bitmap covers the border box snapped outward to whole pixels, and the element's corners may be rounded: clip to the real
            // shape so the filtered backdrop does not spill past the box.
            var builder = box.HtmlContainer?.StructureTagBuilder;
            RGraphicsPath? shape = null;
            g.PushClip(borderBox);
            try
            {
                if (box.IsRounded)
                {
                    var radii = box.ComputeRadii(borderBox);
                    shape = RenderUtils.GetRoundRect(g, borderBox, radii.TLX, radii.TLY, radii.TRX, radii.TRY, radii.BRX, radii.BRY, radii.BLX, radii.BLY);
                    g.PushClip(shape);
                }

                using (_taggingSuppressed ? null : builder?.OpenArtifact(g))
                    g.DrawRaster(scope.Surface);

                if (shape is not null)
                    g.PopClip();
            }
            finally
            {
                g.PopClip();
                shape?.Dispose();
            }
        }

        /// <summary>
        /// Runs the filter list over the backdrop bitmap. The area beyond the border box is not backdrop, so the edges are extended by
        /// mirroring (Filter Effects 2 §3.1's edge handling) rather than by transparency, which would darken a blur at the box's edges.
        /// </summary>
        private static void FilterBackdrop(RasterSurface surface, IReadOnlyList<FilterGrammar.FilterFunction> functions, CssBox box, long maxPixels)
        {
            var margin = RasterFilterChain.InkMargin(functions, box);

            // The mirror is periodic, so a border wider than the surface only repeats what a narrower one already holds, and a huge blur
            // radius must not turn into a huge allocation: keep the padded surface inside the raster pixel budget.
            var padX = (int)Math.Min(Math.Ceiling(margin * surface.PixelsPerUnitX), surface.Width);
            var padY = (int)Math.Min(Math.Ceiling(margin * surface.PixelsPerUnitY), surface.Height);
            while ((padX > 0 || padY > 0) && (long)(surface.Width + 2 * padX) * (surface.Height + 2 * padY) > Math.Max(maxPixels, (long)surface.Width * surface.Height))
            {
                padX /= 2;
                padY /= 2;
            }

            if (padX == 0 && padY == 0)
            {
                RasterFilterChain.Apply(surface, functions, box, 1.0);
                return;
            }

            using var padded = FilterOps.MirrorPad(surface, padX, padY);
            RasterFilterChain.Apply(padded, functions, box, 1.0);
            FilterOps.CopyCentre(padded, surface, padX, padY);
        }

        /// <summary>
        /// Finds the ancestor whose group effect isolates <paramref name="box"/>'s backdrop (null when the page is the root). False when a
        /// transformed ancestor lies between them, which puts the backdrop in a different coordinate space.
        /// </summary>
        private static bool TryFindBackdropRoot(CssBox box, out CssBox? root)
        {
            for (var ancestor = box.ParentBox; ancestor is not null; ancestor = ancestor.ParentBox)
            {
                if (IsBackdropRoot(ancestor))
                {
                    root = ancestor;
                    return true;
                }

                if (ancestor.IsTransformed)
                {
                    root = null;
                    return false;
                }
            }

            root = null;
            return true;
        }

        /// <summary>Filter Effects 2 §3.1's list of what makes a backdrop root, as far as this renderer models it.</summary>
        private static bool IsBackdropRoot(CssBox box) =>
            !box.IsOpaque ||
            box.ActualFilterFunctions.Count > 0 ||
            box.ActualBackdropFilterFunctions.Count > 0 ||
            box.ActualMixBlendMode != BlendMode.Normal ||
            (!string.IsNullOrEmpty(box.ClipPath) && box.ClipPath != Keywords.None);

        private static BoxFragment? FindFragment(BoxFragment from, CssBox box)
        {
            if (ReferenceEquals(from.Box, box))
                return from;

            foreach (var child in from.Children)
            {
                if (FindFragment(child, box) is { } found)
                    return found;
            }

            return null;
        }
    }
}

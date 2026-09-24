using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Raster.Filters;
using System;

namespace PeachPDF.Html.Core.Paint
{
    /// <summary>
    /// Transparency flattening for documents that target a conformance level that forbids transparency (PDF/A-1, PDF/X-1a, PDF/X-3) and
    /// asked for <c>TransparencyPolicy.Flatten</c>: whatever needs transparency is rendered into an opaque bitmap in its place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What needs it is decided by the writer's own rules.</b> A box's own paint (decorations, text, replaced content) is tried on a
    /// scratch page whose transparency guard records instead of throwing (<see cref="PeachPDF.Adapters.TransparencyProbe"/>), so there is one
    /// list of transparent constructs, the guard's, and a construct added to it is flattened without touching this file. A group effect
    /// (opacity, blend mode, colour-matrix filter, raster filter, backdrop-filter) needs it by definition.
    /// </para>
    /// <para>
    /// <b>What is flattened is the box's whole subtree, composited.</b> The page is repainted into a bitmap (the same walk the backdrop
    /// filter uses) up to and including the box, over white paper, and the result replaces the box's region. Because the bitmap holds
    /// everything painted behind the box as well, it is opaque and equals what transparency would have produced. A box that needs it
    /// contains everything its descendants need, so descendants are not visited again. Boxes that need nothing stay vector, and text under a
    /// flattened region is supplied again as invisible text so it stays selectable.
    /// </para>
    /// <para>
    /// Not possible under a CSS <c>transform</c> (the repaint would be in a different coordinate space); such a box is painted normally and the
    /// guard rejects it, as it does without flattening.
    /// </para>
    /// </remarks>
    internal sealed partial class FragmentPainter
    {
        /// <summary>Paint only what this box draws itself, not its children: what a probe asks about.</summary>
        private bool _ownOnly;

        /// <summary>Set on a flatten repaint painter: the fragment after which painting stops.</summary>
        private BoxFragment? _stopAfter;

        private bool TryFlatten(RGraphics g, BoxFragment fragment, FilterEffectResolver.Resolved filter)
        {
            if (!g.FlattensTransparency || _pageRoot is null || _stopAt is not null || _stopAfter is not null || g.IsOffscreenTile)
                return false;

            // A fragment reached a second time (nested, then hoisted out for stacking order) was painted - or flattened - the first
            // time. Claiming it here keeps the second visit from composing an empty group, which would itself need transparency.
            if (_painted.Contains(fragment))
                return true;

            var box = fragment.Box;
            if (!HasGroupEffect(box, filter) && !OwnPaintNeedsTransparency(g, fragment))
                return false;

            // The repaint is in the page's coordinate space, which a transformed box or ancestor leaves.
            for (var b = box; b is not null; b = b.ParentBox)
            {
                if (b.IsTransformed)
                    return false;
            }

            return FlattenSubtree(g, fragment);
        }

        private static bool HasGroupEffect(CssBox box, FilterEffectResolver.Resolved filter) =>
            !box.IsOpaque ||
            box.ActualMixBlendMode != BlendMode.Normal ||
            filter.RequiresRaster ||
            filter.HasColorMatrix ||
            filter.OpacityMultiplier < 1.0 ||
            box.ActualBackdropFilterFunctions.Count > 0;

        private bool OwnPaintNeedsTransparency(RGraphics g, BoxFragment fragment)
        {
            var probe = g.CreateTransparencyProbe();
            if (probe is null)
                return false;

            return probe.Requires(scratch =>
                new FragmentPainter(container) { _ownOnly = true, _taggingSuppressed = true }.PaintContent(scratch, fragment));
        }

        private bool FlattenSubtree(RGraphics g, BoxFragment fragment)
        {
            if (SubtreeExtent(fragment) is not { } extent)
                return false;

            var bounds = Intersect(Inflate(extent, SubtreeBleed(fragment)), g.GetClip());
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                // Nothing of it is visible (outside the clip); nothing to paint, and nothing for its descendants to paint either.
                MarkPainted(fragment);
                return true;
            }

            using var scope = g.BeginRasterSurface(bounds);
            if (scope is null)
                return false;

            // Paper, then the page's canvas, then everything painted up to and including this box.
            FilterOps.Fill(scope.Surface, RColor.FromArgb(255, 255, 255, 255), 1.0);
            if (container.CanvasBackgroundBox is { } canvas)
                PaintCanvasBackground(scope.Graphics, canvas, container.PageBoxRect);

            var mirror = new FragmentPainter(container)
            {
                _stopAfter = fragment,
                _taggingSuppressed = true,
                _pageRoot = _pageRoot,
            };
            mirror.PaintTagged(scope.Graphics, _pageRoot!);

            var box = fragment.Box;
            var builder = _taggingSuppressed ? null : box.HtmlContainer?.StructureTagBuilder;
            using (builder?.OpenArtifact(g))
                g.DrawRaster(scope.Surface);

            PaintSelectableText(g, fragment);
            MarkPainted(fragment);
            return true;
        }

        /// <summary>Records <paramref name="fragment"/> and everything below it as painted, so a hoisted descendant is not painted again by an ancestor's stacking order.</summary>
        private void MarkPainted(BoxFragment fragment)
        {
            _painted.Add(fragment);
            foreach (var child in fragment.Children)
                MarkPainted(child);
        }
    }
}

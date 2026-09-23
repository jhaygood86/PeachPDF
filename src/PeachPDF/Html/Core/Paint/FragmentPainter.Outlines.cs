using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Utils;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Paint
{
    /// <summary>
    /// Outline paint order. An outline is not drawn where its box paints: it is collected into the
    /// nearest enclosing <i>outline scope</i> and drawn once that scope has painted its own content -
    /// before any of its positive <c>z-index</c> layers, and otherwise when the scope finishes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see href="https://www.w3.org/TR/css-position-4/#painting-order">CSS Positioned Layout 4's
    /// painting order</see> allows outlines either in-band or at the end of the stacking context, and
    /// recommends the latter so an outline stays visible. Drawing each outline as its own box finished
    /// let the next sibling's background paint straight over the ring — an outline spills outside its
    /// box by definition, into exactly the space a following sibling occupies.
    /// </para>
    /// <para>
    /// A scope is any box that paints as one self-contained unit: the root, a stacking context, a
    /// positioned box, a float, an atomic inline, a flex or grid item — the boxes Appendix E paints
    /// "as if" they created a stacking context — plus a box that <see cref="PaintFragment"/> wraps in
    /// graphics state of its own (a transform, a <c>clip-path</c>). The last group is what keeps a
    /// deferred outline inside the same transform, clip and opacity group its box was painted in: a
    /// scope flushes before <see cref="PaintFragment"/> pops any of them. The first group is what keeps
    /// hoisting out of it: every box <see cref="StackingOrder.Flatten"/> hoists is a scope, so no
    /// deferred outline ever crosses the ancestor-clip replay a hoisted participant needs. The only
    /// state left between a scope and a deferred box is the <c>overflow</c> clips of the boxes between
    /// them, which each outline records and replays.
    /// </para>
    /// <para>
    /// Deferral changes when an outline is drawn relative to other content, never relative to other
    /// outlines: they are drawn in the order they were collected, which is the order they were drawn
    /// in before.
    /// </para>
    /// <para>
    /// A scope's outlines go under its positive <c>z-index</c> layers, not over them. The spec's
    /// step 10 would draw them after those layers too, but that shows a ring through exactly the
    /// content authors raise to cover things - an overlay, a badge, a modal - and Chromium draws them
    /// underneath. Positioned content at <c>z-index: auto</c> or <c>0</c> still paints under them,
    /// where Chromium puts it above: keeping that is what stops a following positioned sibling from
    /// covering a ring the way a following in-flow one used to.
    /// </para>
    /// </remarks>
    internal sealed partial class FragmentPainter
    {
        /// <summary>
        /// Outlines waiting for the current scope to finish, or null outside any scope — painting that
        /// did not enter through <see cref="PaintTagged"/>, which then draws each outline in place.
        /// </summary>
        private List<DeferredOutline>? _deferredOutlines;

        /// <summary>
        /// The fragment whose paint opened the current scope — the one whose stacking loop draws the
        /// scope's outlines ahead of its positive <c>z-index</c> layers.
        /// </summary>
        private BoxFragment? _outlineScopeOwner;

        /// <summary>
        /// The <c>overflow</c> clips pushed since the current scope opened, outermost first. A deferred
        /// outline snapshots these, because by the time its scope draws it they have all been popped.
        /// </summary>
        private List<OverflowClipStep> _overflowClips = [];

        /// <summary>One rectangle of a box's outline and which of its edges are real ones.</summary>
        private readonly record struct OutlineRect(RRect Rect, bool HasLeftEdge, bool HasRightEdge, bool HasTopEdge, bool HasBottomEdge);

        /// <summary>One <c>overflow</c> clip as <see cref="RenderUtils.ClipGraphicsByOverflow"/> pushes it.</summary>
        private readonly record struct OverflowClipStep(RRect Rect, OverflowClipCurve? Curve);

        /// <summary>A box's outline, and the clips it has to be drawn under.</summary>
        private sealed record DeferredOutline(CssBox Box, List<OutlineRect> Rects, OverflowClipStep[] Clips);

        /// <summary>The enclosing scope's collector and clip record, set aside while a nested scope runs.</summary>
        private readonly record struct OutlineScopeState(
            List<DeferredOutline>? Outlines, List<OverflowClipStep> OverflowClips, BoxFragment? Owner);

        /// <summary>
        /// Whether <paramref name="box"/> paints as one unit whose outlines — its own and every one it
        /// holds — are drawn together once the rest of it has painted. See the class remarks.
        /// </summary>
        internal static bool EstablishesOutlineScope(CssBox box) =>
            box.IsRoot
            || box.IsPositioned
            || box.IsOutOfFlow
            || DomUtils.IsStackingContextBox(box)
            || box.IsTransformed
            || (box.ClipPath != Keywords.None && !string.IsNullOrEmpty(box.ClipPath))
            || DomUtils.IsAtomicInline(box)
            || box.ParentBox?.DerivedStyle.ActualDisplay is Keywords.Flex or Keywords.InlineFlex
                or Keywords.Grid or Keywords.InlineGrid;

        /// <summary>
        /// Opens the outline scope <paramref name="fragment"/>'s box establishes, if it establishes one —
        /// or, when nothing has opened a scope yet, one regardless, so every outline is drawn somewhere.
        /// </summary>
        /// <returns>the enclosing scope's state to hand back to <see cref="CloseOutlineScope"/>, or null
        /// when the box joins the enclosing scope instead</returns>
        private OutlineScopeState? OpenOutlineScope(BoxFragment fragment)
        {
            if (_deferredOutlines is not null && !EstablishesOutlineScope(fragment.Box)) return null;

            var outer = new OutlineScopeState(_deferredOutlines, _overflowClips, _outlineScopeOwner);
            _deferredOutlines = [];
            _overflowClips = [];
            _outlineScopeOwner = fragment;
            return outer;
        }

        /// <summary>
        /// Draws every outline this scope still holds and restores the enclosing scope.
        /// </summary>
        /// <param name="g">the device to draw to</param>
        /// <param name="outer">what <see cref="OpenOutlineScope"/> returned</param>
        /// <param name="builder">the tagged-PDF builder, or null when output is untagged</param>
        /// <param name="draw">false to only restore the enclosing scope - after a paint that failed</param>
        private void CloseOutlineScope(RGraphics g, OutlineScopeState? outer, StructureTagBuilder? builder, bool draw = true)
        {
            if (outer is not { } state) return;

            var outlines = _deferredOutlines!;
            _deferredOutlines = state.Outlines;
            _overflowClips = state.OverflowClips;
            _outlineScopeOwner = state.Owner;

            if (draw) DrawOutlines(g, outlines, builder);
        }

        /// <summary>
        /// Whether <paramref name="fragment"/>'s paint opened the scope now collecting, so its stacking
        /// loop is the one to call <see cref="DrawScopeOutlinesSoFar"/>.
        /// </summary>
        private bool OwnsOutlineScope(BoxFragment fragment) => ReferenceEquals(_outlineScopeOwner, fragment);

        /// <summary>
        /// Draws the outlines this scope has collected so far and empties it, ahead of the scope's
        /// positive <c>z-index</c> layers. Anything collected after this is drawn when the scope closes.
        /// </summary>
        private void DrawScopeOutlinesSoFar(RGraphics g, StructureTagBuilder? builder)
        {
            if (_deferredOutlines is not { Count: > 0 } outlines) return;

            _deferredOutlines = [];
            DrawOutlines(g, outlines, builder);
        }

        /// <summary>
        /// Draws deferred outlines, each under the <c>overflow</c> clips it recorded. Tagged output marks
        /// them as one artifact: an outline is decoration, and most of the structure elements that own
        /// them have already closed their marked content by now.
        /// </summary>
        /// <remarks>
        /// Drawn ahead of a scope's raised layers, the outlines are still inside the scope box's own
        /// paint, and so inside whatever sequence its tagging opened - an author's
        /// <c>-peachpdf-pdf-tag-type: artifact</c>, say. Marked content must not nest, so no artifact
        /// is opened there: inside an artifact the rings already are one, and inside a content element
        /// they join its content, which is where an outline was always tagged before it was deferred.
        /// </remarks>
        private static void DrawOutlines(RGraphics g, List<DeferredOutline> outlines, StructureTagBuilder? builder)
        {
            if (outlines.Count == 0) return;

            using var artifact = builder is { IsInMarkedContent: false } ? builder.OpenArtifact(g) : null;

            foreach (var outline in outlines)
            {
                var pushed = 0;
                foreach (var clip in outline.Clips)
                    pushed += RenderUtils.ClipGraphicsByOverflow(g, clip.Rect, clip.Curve);

                DrawOutline(g, outline.Box, outline.Rects);

                for (var i = 0; i < pushed; i++)
                    g.PopClip();
            }
        }

        /// <summary>
        /// Records the <c>overflow</c> clip <paramref name="fragment"/>'s paint just pushed, so outlines
        /// deferred beneath it can replay it.
        /// </summary>
        /// <returns>whether anything was recorded, for <see cref="PopOverflowClip"/></returns>
        private bool PushOverflowClip(BoxFragment fragment)
        {
            if (fragment.OverflowClip is not { } rect) return false;

            // Every box under one clipping ancestor carries that same ancestor's clip, so a nested run of
            // them would otherwise replay it once per level.
            var step = new OverflowClipStep(rect, fragment.OverflowClipCurve);
            if (_overflowClips.Count > 0 && _overflowClips[^1] == step) return false;

            _overflowClips.Add(step);
            return true;
        }

        private void PopOverflowClip(bool pushed)
        {
            if (pushed) _overflowClips.RemoveAt(_overflowClips.Count - 1);
        }

        /// <summary>
        /// Draws <paramref name="box"/>'s outline now if no scope is collecting, or hands it to the
        /// scope that is.
        /// </summary>
        private void PaintOrDeferOutline(RGraphics g, CssBox box, List<OutlineRect> rects)
        {
            // Every box collects its rectangles, outline or not. Only one that will draw something is
            // worth carrying to its scope - an empty entry would still replay its clips there, and open
            // an artifact in tagged output around nothing.
            if (!OutlineDrawHandler.Paints(g, box)) return;

            if (_deferredOutlines is { } deferred)
                deferred.Add(new DeferredOutline(box, rects, [.. _overflowClips]));
            else
                DrawOutline(g, box, rects);
        }

        /// <summary>Draws one box's outline from the rectangles its paint collected.</summary>
        private static void DrawOutline(RGraphics g, CssBox box, List<OutlineRect> rects)
        {
            // More than one rectangle is a fragmented box, whose outline CSS UI 4 §3.1 asks be drawn
            // as one connected shape rather than closed separately around each fragment - see
            // OutlineDrawHandler.DrawRegionOutline. Every style that paints at all takes that path
            // (see SupportsRegionOutline); anything else - a single rectangle, vertical-decoration
            // geometry, a style that paints nothing - keeps the per-fragment rings below. Either way
            // this stays within the page: these rectangles are one fragmentainer's, so a box
            // broken across pages still gets one shape per page.
            if (rects.Count > 1 &&
                !IsVerticalDecorationGeometry(box) &&
                OutlineDrawHandler.SupportsRegionOutline(box))
            {
                var region = new List<RRect>(rects.Count);
                foreach (var rect in rects) region.Add(rect.Rect);

                OutlineDrawHandler.DrawRegionOutline(g, box, region);
                return;
            }

            foreach (var (rect, hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge) in rects)
            {
                OutlineDrawHandler.DrawOutline(g, box, rect,
                    hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge);
            }
        }
    }
}

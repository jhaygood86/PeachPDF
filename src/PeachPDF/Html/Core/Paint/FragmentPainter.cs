using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Paint.Content;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Paint
{
    /// <summary>
    /// The paint phase. Consumes one <see cref="FragmentainerFragment"/> — one page of the immutable
    /// fragment tree layout produced — and draws it to an <see cref="RGraphics"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>All geometry comes from fragments.</b> A <see cref="CssBox"/> reached through
    /// <see cref="BoxFragment.Box"/> supplies computed style and nothing else; fragment rectangles are
    /// already fragmentainer-local, so there is no page offset to apply anywhere in here.
    /// </para>
    /// <para>
    /// One painter instance paints one page. Per-page state — which fragments have already been drawn
    /// — is the painter's own, so nothing about painting is recorded on the box tree or the container.
    /// Replaced elements (images, inline SVG, <c>&lt;hr&gt;</c>, list markers, repeated table
    /// headers) are painted by an <see cref="IFragmentContentPainter"/> selected from the box's type;
    /// see <see cref="FragmentContentPainters"/>.
    /// </para>
    /// </remarks>
    internal sealed partial class FragmentPainter(HtmlContainerInt container)
    {
        /// <summary>
        /// Tolerance for treating a clip intersection as empty. A rectangle that merely touches the
        /// clip edge - a box relocated to the next page's content top sits exactly flush against the
        /// previous page's clip bottom - is not actually visible, and floating-point rounding across
        /// the several steps such a box's Y goes through can land either side of exact zero.
        /// </summary>
        private const double VisibilityClipEpsilon = 1e-6;

        /// <summary>
        /// Fragments already painted on this page. A fragment can be reached twice in one page walk —
        /// once nested, once hoisted out for stacking-context ordering — and must only paint the first
        /// time. Reference identity is deliberate: two structurally equal fragments (a rowspan cell
        /// shown in each row it spans) are genuinely separate things to paint.
        /// </summary>
        private readonly HashSet<BoxFragment> _painted = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Paints one page: the fragmentainer's whole fragment subtree, clipped to the page's content
        /// window.
        /// </summary>
        internal void Paint(RGraphics g, FragmentainerFragment fragmentainer)
        {
            g.PushClip(container.PageClipOverride ?? container.PageBoxRect);

            PaintFragment(g, fragmentainer.Root);

            g.PopClip();
        }

        /// <summary>
        /// Paints one box fragment — the portion of a box that lives in a single fragmentainer (CSS
        /// Fragmentation Level 3 §2) — establishing the whole-element effects (<c>transform</c>,
        /// <c>clip-path</c>, <c>opacity</c>) around it.
        /// </summary>
        internal void PaintFragment(RGraphics g, BoxFragment fragment)
        {
            var box = fragment.Box;

#if DEBUG
            Console.WriteLine($"paint: {box}");
#endif

            try
            {
                if (box.DerivedStyle.ActualDisplay == Keywords.None || box.Visibility.Value != Visibility.Visible) return;

                // use initial clip to draw blocks with Position = fixed. I.e. ignore page margins
                if (box.Position.Value == PositionMode.Fixed)
                {
                    g.SuspendClipping();
                }

                // A fragment only exists where its box has something in this fragmentainer, so the
                // cheap early-out is now just "is any of it inside the current clip" - the clip being
                // narrower than the page band whenever an ancestor's overflow or a clip-path is active.
                // A fragment that carries only descendants (no decoration rectangles of its own) must
                // always be entered: a hoisted out-of-flow descendant can paint well outside this
                // fragment's own rectangle and is only reached through this call.
                var visible = fragment.Lines.Count == 0 || IsAnyRectVisible(fragment, g.GetClip());

                if (visible)
                {
                    var transformed = box.IsTransformed;

                    if (transformed)
                    {
                        // ActualTransformMatrix is cached treating the box's own top-left as local
                        // (0, 0), so re-anchor the pivot to where the box sits on this page. The whole
                        // (unfragmented) border box is deliberately used rather than this fragment's
                        // own rectangle: per-fragment transform origins are a separate change.
                        g.PushTransform(box.ActualTransformMatrix.RebaseOrigin(fragment.WholeBoxRect.X, fragment.WholeBoxRect.Y));
                    }

                    // clip-path clips the entire element rendering (background, border, content, children).
                    // It is established inside the transform push so the clip and the content it clips are
                    // transformed together (CSS Masking 1: the clip is in the element's local coordinate
                    // system, which any `transform` then maps). The reference box is the border-box (the
                    // default) of the whole box, for the same reason as the transform pivot above.
                    var clipped = false;
                    if (box.ClipPath != Keywords.None && !string.IsNullOrEmpty(box.ClipPath))
                    {
                        if (CssClipPathResolver.TryBuildClipPath(g, box.ClipPath, fragment.WholeBoxRect, box, out var clipGeometry, out _)
                            && clipGeometry is not null)
                        {
                            g.PushClip(clipGeometry);
                            clipped = true;
                        }
                    }

                    if (box.IsOpaque)
                    {
                        PaintTagged(g, fragment);
                    }
                    else
                    {
                        PaintWithOpacity(g, fragment);
                    }

                    if (clipped)
                        g.PopClip();

                    if (transformed)
                        g.PopTransform();
                }

                // Restore clips
                if (box.Position.Value == PositionMode.Fixed)
                {
                    g.ResumeClipping();
                }
            }
            catch (Exception ex)
            {
                if (box.HtmlContainer is { } container)
                    throw container.RenderError(HtmlRenderErrorType.Paint, "Exception in box paint", ex);
            }
        }

        /// <summary>
        /// Whether any of <paramref name="fragment"/>'s own decoration rectangles survives
        /// <paramref name="clip"/>. Checked per rectangle rather than against their union, so a
        /// fragment whose rectangles are far apart on the line cannot be kept alive by empty space
        /// between them.
        /// </summary>
        private static bool IsAnyRectVisible(BoxFragment fragment, RRect clip)
        {
            foreach (var line in fragment.Lines)
            {
                if (IsRectVisible(line.Rect, clip)) return true;
            }

            return false;
        }

        /// <summary>
        /// Paints a fragment (and, via <see cref="PaintTagged"/>, its whole subtree) into an offscreen
        /// tile sized to the current page's visible clip, then composites that tile onto
        /// <paramref name="g"/> as a single flattened result at <c>ActualOpacity</c> - the CSS
        /// <c>opacity</c> property is a group effect (it applies once to the element and everything
        /// painted inside it, not to each descendant's own paint calls independently), and this is what
        /// makes overlapping content within the box composite correctly instead of double-blending
        /// where it overlaps.
        /// </summary>
        /// <remarks>
        /// The tile is sized to the whole current page-visible rect (not a tight bounding box of this
        /// box's own content) so that the subtree can keep painting at its normal page coordinates
        /// unmodified - the box's own possible multiple decoration rectangles (line wraps) and any
        /// overflowing/absolutely-positioned descendants all land correctly inside the tile with no
        /// extra translation math, at the cost of a somewhat larger-than-necessary Form XObject. Any
        /// transform already pushed onto <paramref name="g"/> for this same box (see the caller in
        /// <see cref="PaintFragment"/>) is left active and applies to the tile's placement
        /// automatically, since PDF's own <c>cm</c> operator concatenates - no separate
        /// transform-folding is needed here.
        /// </remarks>
        private void PaintWithOpacity(RGraphics g, BoxFragment fragment)
        {
            var clip = g.GetClip();
            var tileRect = new RRect(0, 0, clip.Right, clip.Bottom);

            var tile = g.CreateTile(tileRect.Width, tileRect.Height);
            if (tile is not { } t)
            {
                // No page/document context to own a Form XObject in (e.g. a measure-only pass) -
                // opacity has no visual effect there anyway, so just paint directly.
                PaintTagged(g, fragment);
                return;
            }

            t.Graphics.PushClip(clip);
            PaintTagged(t.Graphics, fragment);
            t.Graphics.Dispose();

            g.DrawImageWithOpacity(t.Image, tileRect, fragment.Box.ActualOpacity);
        }

        /// <summary>
        /// Paints one fragment's content, wrapped in tagged-PDF structure-tree/marked-content
        /// bookkeeping when tagging is enabled (<c>HtmlContainer.StructureTagBuilder</c> is non-null).
        /// This is the single choke point all tagging flows through, mirroring how
        /// <see cref="PaintFragment"/> already wraps the same content for transform/opacity handling.
        /// When tagging is disabled this adds one null check and otherwise behaves exactly as calling
        /// <see cref="PaintContent"/> directly would.
        /// </summary>
        private void PaintTagged(RGraphics g, BoxFragment fragment)
        {
            // Mirrors PaintBoxContent's own early-out: skip classification/tagging entirely for a
            // fragment that has already been painted on this page.
            if (!_painted.Add(fragment))
                return;

            var box = fragment.Box;
            var builder = box.HtmlContainer?.StructureTagBuilder;
            if (builder == null)
            {
                PaintContent(g, fragment);
                return;
            }

            var classification = StructureTagMapper.Classify(box);
            switch (classification.Kind)
            {
                case StructureTagKind.Artifact:
                    using (builder.OpenArtifact(g))
                        PaintContent(g, fragment);
                    break;

                case StructureTagKind.Grouping when classification.StructureType == Keywords.Li:
                    PaintListItem(g, fragment, builder);
                    break;

                case StructureTagKind.Grouping:
                    using (builder.OpenGroupingElement(box, classification.StructureType!))
                        PaintContent(g, fragment);
                    break;

                case StructureTagKind.Content:
                    using (builder.OpenContentElement(g, box, classification.StructureType!, classification.AltText))
                        PaintContent(g, fragment);
                    break;

                default:
                    PaintContent(g, fragment);
                    break;
            }
        }

        /// <summary>
        /// Paints a tagged &lt;li&gt;'s marker and body as separate sibling structure elements under
        /// "/LI" - "/Lbl" (the marker, via the synthesized <c>::marker</c> child from
        /// <see cref="CssBox.IsMarkerPseudoElement"/>, defaulting to "Lbl" per the UA stylesheet's
        /// <c>li::marker</c> rule, or suppressed if an author set
        /// <c>li::marker { -peachpdf-pdf-tag-type: none }</c>) and "/LBody" (everything else - not
        /// itself a separate CSS signal, inferred as "whatever isn't the marker" per the design
        /// decision this implements). The struct element for "/Lbl" is opened, and its own content
        /// painted, before "/LBody"'s so the two land in that order under "/LI"'s "/K" (correct
        /// reading order) - the marker's own on-page paint position is unaffected by this call-order
        /// swap, since it's driven entirely by pre-computed layout coordinates, not paint order.
        /// </summary>
        private void PaintListItem(RGraphics g, BoxFragment fragment, StructureTagBuilder builder)
        {
            using (builder.OpenGroupingElement(fragment.Box, Keywords.Li))
            {
                var markerFragment = FindMarkerFragment(fragment);
                if (markerFragment is not null)
                {
                    var markerBox = markerFragment.Box;
                    var markerClassification = StructureTagMapper.Classify(markerBox);
                    if (markerClassification.Kind == StructureTagKind.None)
                    {
                        PaintContent(g, markerFragment);
                    }
                    else
                    {
                        using (builder.OpenContentElement(g, markerBox, markerClassification.StructureType ?? Keywords.Lbl))
                            PaintContent(g, markerFragment);
                    }
                }

                using (builder.OpenListItemBodyElement(fragment.Box))
                    PaintBoxContent(g, fragment, paintMarkers: false);
            }
        }

        /// <summary>
        /// This fragment's <c>::marker</c> child fragment, if the marker appears on this page at all.
        /// </summary>
        private static BoxFragment? FindMarkerFragment(BoxFragment fragment)
        {
            foreach (var child in fragment.Children)
            {
                if (child.Box.IsMarkerPseudoElement) return child;
            }

            return null;
        }

        /// <summary>
        /// Paints a fragment's own content, routed to the box type's
        /// <see cref="IFragmentContentPainter"/> if it has one and to the generic box paint
        /// (<see cref="PaintBoxContent"/>) otherwise. This is the type-dispatch seam that
        /// replaced-element subclasses used to provide by overriding a virtual paint method on the box.
        /// </summary>
        internal void PaintContent(RGraphics g, BoxFragment fragment)
        {
            if (FragmentContentPainters.For(fragment.Box) is { } contentPainter)
                contentPainter.Paint(this, g, fragment);
            else
                PaintBoxContent(g, fragment);
        }

        /// <summary>
        /// The generic box paint: this fragment's own decoration rectangles (shadows, background,
        /// borders), its words, its text decoration, then its children in CSS 2.1 Appendix E order.
        /// </summary>
        /// <param name="g">the device to draw to</param>
        /// <param name="fragment">the fragment being painted, the source of all geometry</param>
        /// <param name="paintMarkers">
        /// false only on the tagged-PDF &lt;li&gt; path, which paints the marker itself as a separate
        /// sibling structure element ("/Lbl") ahead of the list item's body ("/LBody").
        /// </param>
        internal void PaintBoxContent(RGraphics g, BoxFragment fragment, bool paintMarkers = true)
        {
            var box = fragment.Box;

            if (box.DerivedStyle.ActualDisplay == Keywords.None ||
                (box.DerivedStyle.ActualDisplay == Keywords.TableCell && box.EmptyCells == Keywords.Hide && box.IsSpaceOrEmpty)) return;

            var clipped = RenderUtils.ClipGraphicsByOverflow(g, fragment.OverflowClip);

            // This fragment's own decoration rectangles - one per line box it spans on this page, or a
            // single whole-border-box rectangle for a block-level box. Already fragmentainer-local.
            var lines = fragment.Lines;
            var clip = g.GetClip();

            for (var i = 0; i < lines.Count; i++)
            {
                var actualRect = lines[i].Rect;

                if (!IsRectVisible(actualRect, clip)) continue;

                // Where this rectangle's decorations resolve, per box-decoration-break (css-break-3 §6.2).
                // Culling above stays on the rectangle itself: the decoration area may be the whole
                // unbroken box, which is far wider than what is visible here.
                var geometry = BoxDecorationGeometry.For(box, lines[i]);

                // Outset (drop) shadows paint BEFORE the background so they sit behind the box
                // (CSS Backgrounds & Borders 3 §5).
                if (BoxDecorationGeometry.HasBoxShadow(box))
                    PaintBoxShadows(g, box, geometry, inset: false);

                // A box whose background was "promoted" to fill the whole page canvas (see
                // HtmlContainerInt.ResolveCanvasBackground / PaintCanvasBackground) already had it
                // painted for every page - skip this box's own normal paint pass so it isn't painted
                // twice.
                if (!box.SuppressOwnBackgroundPaint)
                {
                    // A sliced background is painted over the whole unbroken box, so this clip is what
                    // keeps it out of the space beside this rectangle - which belongs to other content.
                    if (geometry.NeedsClip) g.PushClip(geometry.ClipRect);

                    PaintBackground(g, box, geometry, GetFirstLineStyleForRect(lines[i].Line));

                    if (geometry.NeedsClip) g.PopClip();
                }

                // Inset shadows paint AFTER (over) the background, clipped to the padding box.
                if (BoxDecorationGeometry.HasBoxShadow(box))
                    PaintBoxShadows(g, box, geometry, inset: true);

                // For multi-page tables, draw the outer bottom border at the page-break Y on
                // intermediate pages (instead of at the rectangle's bottom, which is off-page).
                // The page clip already constrains the side-border top to MarginTop.
                var rectForBorders = geometry.DecorationRect;
                if ((box.DerivedStyle.ActualDisplay == Keywords.Table || box.DerivedStyle.ActualDisplay == Keywords.InlineTable)
                    && box.PageBreakBottoms != null && box.HtmlContainer != null)
                {
                    // PageBreakBottoms is keyed by pagination slot, which is exactly what the fragment
                    // records - no re-deriving the slot from a scroll offset.
                    if (box.PageBreakBottoms.TryGetValue(fragment.FragmentainerIndex, out var pageBreakBottom))
                    {
                        var pageBreakBottomVisual = pageBreakBottom - fragment.OriginY;
                        if (pageBreakBottomVisual < rectForBorders.Bottom)
                        {
                            rectForBorders = new RRect(
                                rectForBorders.Left,
                                rectForBorders.Top,
                                rectForBorders.Width,
                                pageBreakBottomVisual - rectForBorders.Top);
                        }
                    }
                }

                if (geometry.NeedsClip) g.PushClip(geometry.ClipRect);

                BordersDrawHandler.DrawBoxBorders(g, box, rectForBorders,
                    geometry.HasLeftEdge, geometry.HasRightEdge, geometry.HasTopEdge, geometry.HasBottomEdge);

                if (geometry.NeedsClip) g.PopClip();
            }

            if (box.ColumnRuleSegments is { Count: > 0 } && box.ActualColumnRuleWidth > 0)
            {
                PaintColumnRules(g, box, fragment.OriginY, clip);
            }

            PaintWords(g, box, fragment);

            for (var i = 0; i < lines.Count; i++)
            {
                var actualRect = lines[i].Rect;

                if (IsRectVisible(actualRect, clip))
                {
                    // Text decoration is drawn over the rectangle itself, never the unbroken box: an
                    // underline belongs to the line it underlines. Only whether to inset it by the box's own
                    // padding and border is a §6.2 question, which is what the edge flags answer.
                    var geometry = BoxDecorationGeometry.For(box, lines[i]);

                    PaintDecoration(g, box, actualRect, geometry.HasLeftEdge, geometry.HasRightEdge,
                        GetFirstLineStyleForRect(lines[i].Line));
                }
            }

            foreach (var layerBoxes in StackingOrder.ByLayers(StackingOrder.Flatten(fragment)))
            {
                // Split paint to handle z-order, per CSS2.1 Appendix E's within-a-stacking-context
                // order: in-flow block-level descendants, then non-positioned floats, then in-flow
                // inline-level descendants (text and inline replaced content), then positioned
                // descendants. Block and inline normal-flow content used to share a single pass here
                // (painted in tree order with no float-relative ordering guarantee at all) - Acid2's own
                // ".eyes" trap (a block, a float, and an inline replaced <object> as siblings, each
                // required to paint in a different layer) depends on inline being its own later pass.
                //
                // A plain (non-inline-itself) box whose entire content is inline - e.g. Acid2's own
                // "#eyes-a" div, a block wrapper around nothing but its resolved inline <object> image -
                // is treated as belonging to the inline pass too, via StackingOrder.ActsAsInline. Its
                // own recursive paint call is what actually paints its inline child (through ITS OWN
                // nested stacking loop), so deferring that whole call to this stacking context's inline
                // pass is what makes the child paint after this context's float pass, matching Appendix
                // E, without needing to hoist the descendant out of its normal DOM position/ancestor at
                // all (unlike the out-of-flow float/absolute/fixed hoisting StackingOrder.Flatten
                // already does - that mechanism moves a box's paint call across ancestor boundaries
                // entirely, which isn't needed or wanted here since "#eyes-a" itself already belongs to
                // this stacking context's own direct children).
                foreach (var p in layerBoxes)
                {
                    if (!StackingOrder.ActsAsInline(p.Box) && p.Box.Position.Value != PositionMode.Absolute && p.Box is { IsFixed: false, IsFloated: false })
                        PaintStackingParticipant(g, p);
                }

                foreach (var p in layerBoxes)
                {
                    if (p.Box.IsFloated)
                        PaintStackingParticipant(g, p);
                }

                foreach (var p in layerBoxes)
                {
                    if (StackingOrder.ActsAsInline(p.Box) && p.Box.Position.Value != PositionMode.Absolute && p.Box is { IsFixed: false, IsFloated: false })
                        PaintStackingParticipant(g, p);
                }

                foreach (var p in layerBoxes)
                {
                    if (p.Box.Position.Value == PositionMode.Absolute)
                        PaintStackingParticipant(g, p);
                }

                foreach (var p in layerBoxes)
                {
                    if (p.Box.IsFixed)
                        PaintStackingParticipant(g, p);
                }
            }

            if (clipped)
                g.PopClip();

            if (paintMarkers)
            {
                var markerFragment = FindMarkerFragment(fragment);
                if (markerFragment != null)
                {
                    PaintContent(g, markerFragment);
                }
            }

            PaintContentImage(g, box, fragment);
        }

        /// <summary>
        /// Paints one stacking-context participant discovered by <see cref="StackingOrder.Flatten"/>.
        /// A hoisted participant (non-empty <see cref="StackingOrder.StackingParticipant.ClipAncestors"/>)
        /// paints via this fragment's own paint loop rather than the ordinary nested parent-to-child
        /// cascade, so its ancestors' own <c>overflow: hidden</c> clipping - normally picked up "for
        /// free" from still being active on the graphics clip stack during natural nested painting - is
        /// never applied on its own. Re-apply it explicitly here instead, scoped to exactly this
        /// participant's own paint call. A no-op for a direct plain child (empty ClipAncestors).
        /// </summary>
        private void PaintStackingParticipant(RGraphics g, StackingOrder.StackingParticipant participant)
        {
            var fragment = participant.Fragment;
            var pushedClips = RenderUtils.PushAncestorOverflowClips(g, participant.ClipAncestors, fragment.OriginY);

            PaintFragment(g, fragment);

            for (var i = 0; i < pushedClips; i++)
                g.PopClip();
        }

        /// <summary>
        /// Whether <paramref name="rect"/> survives <paramref name="clip"/>. A rect merely touching the
        /// clip edge isn't actually visible - see <see cref="VisibilityClipEpsilon"/>.
        /// </summary>
        private static bool IsRectVisible(RRect rect, RRect clip)
        {
            rect.X -= 2;
            rect.Width += 2;
            clip.Intersect(rect);

            return clip.Width > VisibilityClipEpsilon && clip.Height > VisibilityClipEpsilon;
        }

        /// <summary>
        /// Resolves the <c>::first-line</c> style (if any) that applies to a rectangle painted for
        /// <paramref name="lineBox"/> - true exactly when the line's own owner (the block establishing
        /// that inline formatting context - see <see cref="CssLineBox.OwnerBox"/>) has a resolved
        /// first-line style and this is genuinely that owner's first line. Used to make
        /// <see cref="PaintBackground"/>/<see cref="PaintDecoration"/> first-line-aware without either
        /// method needing to know its own relationship to the block establishing its inline formatting
        /// context - the line box already knows.
        /// </summary>
        private static CssBox? GetFirstLineStyleForRect(CssLineBox? lineBox) =>
            lineBox is not null && lineBox.OwnerBox.ResolvedFirstLineStyle is not null && lineBox == lineBox.OwnerBox.LineBoxes.FirstOrDefault()
                ? lineBox.OwnerBox.ResolvedFirstLineStyle
                : null;
    }
}

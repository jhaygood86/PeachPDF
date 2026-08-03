using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Utils;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Paint
{
    /// <summary>
    /// Paint order within one stacking context, per
    /// <see href="https://www.w3.org/TR/CSS21/zindex.html">CSS 2.1 Appendix E</see>: which fragments a
    /// stacking context is responsible for painting, and in which z-index layers.
    /// </summary>
    /// <remarks>
    /// The discovery walk runs over <i>fragments</i>, so a descendant that does not appear on the page
    /// being painted is simply not found; the ordering rules themselves read the originating boxes'
    /// style. The two predicates that decide whether a box participates at all —
    /// <see cref="DomUtils.NeedsStackingHoist"/> and <see cref="DomUtils.IsStackingContextBox"/> — stay
    /// in <see cref="DomUtils"/>, because layout calls them too (see
    /// <c>HtmlContainerInt.ComputeFlowFlags</c>).
    /// </remarks>
    internal static class StackingOrder
    {
        // One box to paint as part of a stacking context's own layer ordering, plus the chain of DOM
        // ancestor boxes (outer to inner, between the claiming stacking context and Box itself) that
        // Box was hoisted past. Empty for a direct plain child - it paints via ordinary nested
        // recursion, so its ancestors' own overflow clipping is already correctly active on the
        // graphics clip stack from their own (still-running) paint calls. Non-empty for a hoisted
        // participant - it paints via the claiming stacking context's own paint loop instead, bypassing
        // those ancestors' paint calls entirely, so their overflow clipping must be reapplied
        // explicitly (see RenderUtils.PushAncestorOverflowClips) around its own paint call.
        internal readonly record struct StackingParticipant(BoxFragment Fragment, IReadOnlyList<CssBox> ClipAncestors)
        {
            /// <summary>The box whose style and stacking role decide where this participant paints.</summary>
            internal CssBox Box => Fragment.Box;
        }

        /// <summary>
        /// The participants of one fragment's own stacking context, discovered over its <i>fragment</i>
        /// children — so a descendant that does not appear on this page is simply not found. The
        /// ordering rules themselves are unchanged and still read the originating boxes.
        /// </summary>
        internal static IEnumerable<StackingParticipant> Flatten(BoxFragment fragment)
        {
            var box = fragment.Box;

            // Plain in-flow, non-stacking-context children always paint here, nested normally - this is
            // what keeps this box's own overflow-clip scope (pushed/popped around the same children
            // loop in FragmentPainter) wrapped around them, and their own further plain descendants are
            // handled the same way, recursively, by their own subsequent paint call.
            foreach (var childFragment in fragment.Children)
            {
                var childBox = childFragment.Box;

                // ::marker boxes (inside or outside position) are always painted via one explicit
                // call from FragmentPainter's own marker handling, not discovered generically here
                // - both so the tagged-PDF path can wrap the marker in its own "/Lbl" structure element
                // separately from the rest of the list item's "/LBody" content, and so an "outside"
                // marker (which must not affect - or be discovered through - the owning list item's own
                // stacking context, per CSS2.1 12.5.1 / CSS Lists Level 3) never gets bubbled up as if
                // it were normal in-flow content. Yielding it here too would double-paint it.
                if (childBox.IsMarkerPseudoElement) continue;

                if (!DomUtils.NeedsStackingHoist(childBox))
                {
                    yield return new StackingParticipant(childFragment, []);
                }
            }

            // The nearest enclosing "local ordering scope" (see IsLocalOrderingScope) is responsible for
            // finding and ordering every out-of-flow / stacking-context-establishing descendant reachable
            // through plain wrapper boxes AND plain floats, at any depth - a box that is neither claims
            // nothing further here; any such descendants of its own are claimed by whichever ancestor
            // above it actually qualifies. This is what fixes three bugs in earlier versions of this
            // method: (1) a box that itself establishes a stacking context (e.g. position:relative;
            // z-index, or now also opacity<1/transform) was never yielded by its own parent at all, so it
            // and its whole subtree never painted; (2) an out-of-flow stacking-context descendant nested a
            // few plain wrapper boxes deep was discovered "naturally" via the ordinary parent-to-child
            // paint cascade before its true ancestor stacking context ever reached its own z-index
            // layer, so it visually painted as if z-index had no effect; (3) a box that is positioned
            // (absolute/relative/fixed/sticky) but establishes no NEW stacking context of its own
            // (z-index:auto) never searched its own subtree either, so its own floated/positioned-without-
            // z-index children (Appendix E's "non-positioned floats" and "positioned descendants with
            // stack level 0") escaped all the way to the nearest TRUE stacking context ancestor instead of
            // being ordered locally against their true DOM siblings - see IsLocalOrderingScope.
            if (!IsLocalOrderingScope(box)) yield break;
            if (!(box.HtmlContainer?.HasStackingHoistCandidates ?? true)) yield break;

            foreach (var participant in SearchForHoistableDescendants(fragment, []))
            {
                yield return participant;
            }
        }

        // A box claims local Appendix-E ordering responsibility for its own PLAIN FLOAT descendants
        // (rather than deferring them to a more distant ancestor) if it is the root, a genuine stacking
        // context, OR merely positioned (absolute/relative/fixed/sticky) regardless of z-index - matching
        // Appendix E step 6's "positioned descendants with stack level 0 [...] painted via the same
        // [7-step] procedure" model, under which every positioned box (not only ones with an explicit
        // z-index) is its own atomic recursive unit for steps 3/4/5 (block/float/inline). This does NOT
        // extend to genuine stacking-context descendants nested inside a merely-positioned (z-index:auto)
        // box - those must keep escaping all the way to the true nearest stacking context, exactly like
        // through a plain non-positioned wrapper, because z-index competition only happens at a REAL
        // stacking context's own level (see the claimFloatsHere parameter on
        // SearchForHoistableDescendants, which encodes this float-vs-stacking-context distinction - a
        // merely-positioned box is a local ordering boundary for floats only, not for z-index).
        //
        // Without the float half of this, a positioned-but-z-index:auto box's own float child (Acid2's
        // own ".eyes" - position:absolute, no z-index - containing float "#eyes-b" alongside block
        // "#eyes-c" and inline "#eyes-a") was hoisted all the way to the true root's own stacking pass
        // instead of being ordered locally against its true DOM siblings, painting relative to the root's
        // entire subtree instead of interleaved correctly within ".eyes" itself.
        private static bool IsLocalOrderingScope(CssBox box) =>
            box.IsRoot || DomUtils.IsStackingContextBox(box) ||
            box.Position is CssConstants.Absolute or CssConstants.Relative or CssConstants.Fixed or CssConstants.Sticky;

        // Tunnels through plain wrapper boxes looking for content that needs to compete at `box`'s own
        // local ordering scope. Two categories of content are hoisted here, with different stopping
        // rules:
        //
        // - A genuine stacking context (IsStackingContextBox) always keeps escaping through anything
        //   that ISN'T ITSELF a stacking context - including a merely-positioned (z-index:auto) box -
        //   because z-index only has meaning relative to the nearest REAL stacking context. Recursion
        //   stops at (but includes) each stacking context found; its own subtree is its own business,
        //   resolved independently once its own paint call later invokes Flatten on itself.
        // - A plain FLOAT only escapes as far as the nearest box that IsLocalOrderingScope (root, a
        //   genuine stacking context, or merely positioned) - once the walk has passed through such a
        //   box, `claimFloatsHere` flips to false for everything beneath it, since that box will find
        //   and locally order its own floats itself (via its own later Flatten call, whose initial bail
        //   check now also accepts merely-positioned boxes - see IsLocalOrderingScope) rather than this
        //   outer search claiming them too, which would both double-paint them and order them relative
        //   to the wrong (too-distant) box's siblings.
        //
        // `ancestorPath` accumulates every DOM ancestor walked through along the way (both plain
        // pass-through wrappers and hoisted-but-not-yet-fully-resolved boxes like a merely-positioned
        // box) - each yielded participant snapshots it as its ClipAncestors, so the caller can re-apply
        // those ancestors' own overflow clipping (which it never picks up naturally, having been hoisted
        // past their own paint calls). Mutating one shared list via add-before-recurse/remove-after is
        // safe here: the whole sequence is drained eagerly and synchronously by Flatten's caller before
        // anything else touches it.
        private static IEnumerable<StackingParticipant> SearchForHoistableDescendants(
            BoxFragment fragment, List<CssBox> ancestorPath, bool claimFloatsHere = true)
        {
            foreach (var childFragment in fragment.Children)
            {
                var childBox = childFragment.Box;

                if (childBox.IsMarkerPseudoElement) continue;

                var isStackingContext = DomUtils.IsStackingContextBox(childBox);
                var isLocalOrderingScope = !isStackingContext && IsLocalOrderingScope(childBox);
                var isPlainFloatToClaim = claimFloatsHere && !isStackingContext && !isLocalOrderingScope && childBox.IsOutOfFlow;

                if (isStackingContext || isLocalOrderingScope || isPlainFloatToClaim)
                {
                    yield return new StackingParticipant(childFragment, ancestorPath.ToArray());
                    if (isStackingContext) continue;
                }

                // Once the walk passes through a merely-positioned (non-stacking-context) box, any
                // further floats beneath it belong to THAT box's own local claim, not this search's -
                // only genuine stacking contexts still need to keep escaping past it.
                var claimBeyond = !isLocalOrderingScope && claimFloatsHere;

                ancestorPath.Add(childBox);
                foreach (var descendant in SearchForHoistableDescendants(childFragment, ancestorPath, claimBeyond))
                {
                    yield return descendant;
                }
                ancestorPath.RemoveAt(ancestorPath.Count - 1);
            }
        }

        /// <summary>
        /// Groups <paramref name="participants"/> into z-index layers, lowest first — the outer loop of
        /// Appendix E's within-a-stacking-context ordering.
        /// </summary>
        internal static IEnumerable<List<StackingParticipant>> ByLayers(IEnumerable<StackingParticipant> participants)
        {
            var boxesByLayer = new Dictionary<int, List<StackingParticipant>>();

            foreach (var participant in participants)
            {
                var zIndex = 0;

                if (participant.Box.ZIndex.Value is { IsValue: true } zIndexValue)
                {
                    zIndex = zIndexValue.Value.GetValueOrDefault();
                }

                if (!boxesByLayer.TryGetValue(zIndex, out var layer))
                {
                    layer = [];
                    boxesByLayer[zIndex] = layer;
                }

                layer.Add(participant);
            }

            return boxesByLayer.OrderBy(x => x.Key).Select(x => x.Value);
        }

        /// <summary>
        /// Whether <paramref name="box"/> belongs in the "inline" paint pass of the block/float/inline
        /// ordering: either it's genuinely inline-level itself, or it's a plain block-level box whose
        /// entire content is inline (an "invisible" wrapper carrying only inline content, own box aside
        /// - e.g. Acid2's own "#eyes-a", a div around nothing but its resolved inline &lt;object&gt;
        /// image). <c>Boxes.Count > 0</c> guards against misclassifying a genuinely empty block box as
        /// inline (<see cref="System.Linq.Enumerable.All{T}"/> on an empty sequence is vacuously true).
        /// </summary>
        internal static bool ActsAsInline(CssBox box) =>
            box.IsInline || (box.Boxes.Count > 0 && box.Boxes.All(b => b.IsInline));
    }
}

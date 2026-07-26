using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// Which break point a break value really states something about, per
    /// <see href="https://www.w3.org/TR/css-break-3/#break-between">CSS Fragmentation Level 3 §3.1</see>:
    /// a <c>break-before</c> on a container's <i>first</i> in-flow child, and a <c>break-after</c> on its
    /// <i>last</i>, are values at the break point before or after the <b>container</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Propagation moves the break, not only its target.</b> <c>CssBox.PerformLayoutPrologue</c> already
    /// resolves a first child's forced break against a predecessor found by climbing the same chain
    /// (<see cref="DomUtils.PrecedingBoxAcrossFirstChildChain"/>), which puts the break in the right place
    /// while leaving the containers the box begins where they were — so each of them spans the boundary and
    /// paints an empty stub of its own chrome on the page being left. Reading the value through the chain
    /// instead lets the outermost such container take the break, and the boxes inside it simply start where
    /// they always did relative to it.
    /// </para>
    /// <para>
    /// <b>Propagation stops before the fragmentation context root</b> (§3.1's "propagation stops before
    /// breaking through the nearest matching fragmentation context"), and before any box whose children a
    /// layout engine places for itself. The first is why a document whose very first element carries a
    /// forced break does not manufacture a blank page in front of itself: the chain reaches the root, finds
    /// nothing before it in the flow, and no break is taken. The second is the #166 engine-independence
    /// boundary — a flex item, a table cell or a multi-column child is positioned by its engine rather than
    /// by block flow, so a break travelling out of one would name a position the engine is about to
    /// overwrite.
    /// </para>
    /// </remarks>
    internal static class BreakPropagation
    {
        /// <summary>
        /// Whether the break point before <paramref name="box"/> <i>is</i> the break point before its
        /// container — so a forced break there is taken by the container, and this box does not take one of
        /// its own.
        /// </summary>
        internal static bool PropagatesBreakBeforeOutward(CssBox box) =>
            CanTravelOutOf(box) && FirstInFlowChild(box.ParentBox!) == box;

        /// <summary>
        /// The break-after counterpart of <see cref="PropagatesBreakBeforeOutward"/>.
        /// </summary>
        internal static bool PropagatesBreakAfterOutward(CssBox box) =>
            CanTravelOutOf(box) && LastInFlowChild(box.ParentBox!) == box;

        /// <summary>
        /// The outermost box whose break point before it is the same one as before <paramref name="box"/> —
        /// <paramref name="box"/> itself when nothing propagates. This is the box that travels when the
        /// break is taken.
        /// </summary>
        internal static CssBox AnchorForBreakBefore(CssBox box)
        {
            var anchor = box;

            while (PropagatesBreakBeforeOutward(anchor))
            {
                anchor = anchor.ParentBox!;
            }

            return anchor;
        }

        /// <summary>
        /// The forced break value that applies at the break point <i>before</i> <paramref name="box"/> —
        /// its own <c>break-before</c> combined with every value propagating outward to it from the chain
        /// of boxes it begins. Null when none of them is forced.
        /// </summary>
        internal static string? ForcedBreakBeforeAt(CssBox box)
        {
            var value = Forced(box.BreakBefore);

            for (var child = FirstInFlowChild(box);
                 child is not null && PropagatesBreakBeforeOutward(child);
                 child = FirstInFlowChild(child))
            {
                value = Combine(value, Forced(child.BreakBefore));
            }

            return value;
        }

        /// <summary>
        /// The break-after counterpart of <see cref="ForcedBreakBeforeAt"/>, read through the chain of boxes
        /// <paramref name="box"/> ends.
        /// </summary>
        internal static string? ForcedBreakAfterAt(CssBox box)
        {
            var value = Forced(box.BreakAfter);

            for (var child = LastInFlowChild(box);
                 child is not null && PropagatesBreakAfterOutward(child);
                 child = LastInFlowChild(child))
            {
                value = Combine(value, Forced(child.BreakAfter));
            }

            return value;
        }

        /// <summary>
        /// Combines two forced break values that apply at one break point, per §3.1's "if multiple forced
        /// break values apply, all of them are honored".
        /// </summary>
        /// <remarks>
        /// A directional value forces a page break as well as naming a side, so it satisfies a plain
        /// <c>page</c> and wins over one outright. Two <i>conflicting</i> directional values cannot both be
        /// honored, and there §3.1 is explicit: the value on the latest element in flow wins — which, for a
        /// value travelling outward, is the deeper one.
        /// </remarks>
        /// <param name="outer">the value already resolved for the enclosing box</param>
        /// <param name="inner">the value propagating outward from the box it begins, later in flow</param>
        private static string? Combine(string? outer, string? inner)
        {
            if (BreakValues.SideOf(inner) is not PageSide.Any) return inner;
            if (BreakValues.SideOf(outer) is not PageSide.Any) return outer;

            return outer ?? inner;
        }

        private static string? Forced(string? value) => BreakValues.IsForcedPageBreak(value) ? value : null;

        /// <summary>
        /// Whether a break value on <paramref name="box"/> may travel out to its parent at all — see the
        /// type's own remarks for the two things that stop it.
        /// </summary>
        private static bool CanTravelOutOf(CssBox box) =>
            IsInFlow(box)
            && box.ParentBox is { ParentBox: not null } parent
            && !MonolithicContent.PaginatesItsOwnContent(parent);

        internal static CssBox? FirstInFlowChild(CssBox box)
        {
            foreach (var child in box.Boxes)
            {
                if (IsInFlow(child)) return child;
            }

            return null;
        }

        internal static CssBox? LastInFlowChild(CssBox box)
        {
            for (var i = box.Boxes.Count - 1; i >= 0; i--)
            {
                if (IsInFlow(box.Boxes[i])) return box.Boxes[i];
            }

            return null;
        }

        /// <summary>
        /// The same set of boxes <see cref="DomUtils.GetPreviousSibling"/> walks with floats excluded, so
        /// "the first in-flow child" and "has no previous in-flow sibling" cannot name different boxes.
        /// </summary>
        private static bool IsInFlow(CssBox box) =>
            box.Display != CssConstants.None
            && box.Position is not (CssConstants.Absolute or CssConstants.Fixed)
            && !box.IsFloated;
    }
}

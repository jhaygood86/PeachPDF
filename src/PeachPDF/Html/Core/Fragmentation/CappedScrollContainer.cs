using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// A scroll container with an auto height and a <c>max-height</c>, which breaks between its lines like a
    /// plain block while its content is <i>under the cap</i> (css-break-3 §2 keeps only a scroll container
    /// with a definite height monolithic) and lets the content past the cap run on unbroken.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cap measures the box's consumed block size, not a document-space distance: a box that breaks
    /// leaves the unused strip at a page foot and the margin of the next page out of it. Content past the
    /// cap is clipped away (or, for <c>auto</c>/<c>scroll</c>, overflows), so it is laid out without taking
    /// a break: a break among it would end the pass with the siblings after the box still to place, on a page
    /// already emitted.
    /// </para>
    /// <para>
    /// Consumed size is read back off the lines already placed rather than kept as state, so a pass that is
    /// laid out again cannot leave a stale count behind. Only lines count: a box whose content holds a table,
    /// flex or grid container (whose own engines take breaks the line hook cannot see) stays whole.
    /// </para>
    /// </remarks>
    internal static class CappedScrollContainer
    {
        /// <summary>The nearest capped scroll container that is <paramref name="box"/> or encloses it.</summary>
        internal static CssBox? ContainerOf(CssBox? box)
        {
            for (var candidate = box; candidate is not null; candidate = candidate.ParentBox)
            {
                // Cheap rejections first: this runs for every word of a fragmenting flow.
                if (!CssValueParser.IsValidLength(candidate.MaxHeight)) continue;
                if (IsCapped(candidate)) return candidate;
            }

            return null;
        }

        /// <summary>Whether <paramref name="box"/> is a capped scroll container that breaks under its cap.</summary>
        internal static bool IsCapped(CssBox box) =>
            CssValueParser.IsValidLength(box.MaxHeight)
            && MonolithicContent.IsScrollContainer(box)
            && !MonolithicContent.IsMonolithic(box)
            && box.WritingMode.Value is not (WritingMode.VerticalRl or WritingMode.VerticalLr)
            && CssLayoutEngine.ResolveMaxHeight(box) is not null;

        /// <summary>
        /// Whether a box with a <c>max-height</c> keeps the unbreakable treatment: a table cell or caption (its own
        /// engine paginates it), or a box holding a table, flex or grid container, whose breaks past the cap
        /// the line hook could not hold back.
        /// </summary>
        internal static bool MustStayWhole(CssBox box) =>
            box.DerivedStyle.ActualDisplay is Keywords.TableCell or Keywords.TableCaption || HoldsAnEngineOfItsOwn(box);

        // A table, flex or grid container inside takes its own break decisions, so a break past the cap could
        // not be held back: the box keeps the unbreakable treatment instead.
        private static bool HoldsAnEngineOfItsOwn(CssBox box)
        {
            foreach (var child in box.Boxes)
            {
                if (child.IsOutOfFlow) continue;
                if (MonolithicContent.PaginatesItsOwnContent(child) || HoldsAnEngineOfItsOwn(child)) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether a flow placed at <paramref name="y"/> inside <paramref name="box"/> (or one of its
        /// descendants) lies past the cap, and so must not take a break.
        /// </summary>
        internal static bool IsPastCap(CssBox box, double y)
        {
            if (ContainerOf(box) is not { } container) return false;

            var cap = CssLayoutEngine.ResolveMaxHeight(container)!.Value;
            var slot = container.HtmlContainer!.SlotStartingAt(y);

            return ConsumedBefore(container, slot) + (y - ContentTopOf(container, slot)) >= cap - 0.01;
        }

        /// <summary>
        /// The document y at which <paramref name="container"/>'s cap is reached, or null when the lines placed
        /// so far do not reach it.
        /// </summary>
        internal static double? CapBottom(CssBox container)
        {
            var html = container.HtmlContainer!;
            var cap = CssLayoutEngine.ResolveMaxHeight(container)!.Value;
            var first = html.SlotStartingAt(container.Location.Y);
            var bottoms = LineBottomsBySlot(container, html, int.MaxValue);

            if (bottoms.Count == 0) return null;

            var last = 0;
            foreach (var slot in bottoms.Keys) last = Math.Max(last, slot);

            var consumed = 0.0;

            for (var slot = first; slot <= last; slot++)
            {
                var top = ContentTopOf(container, slot);
                var extent = ExtentIn(container, html, bottoms, slot, last);

                if (consumed + extent >= cap) return top + (cap - consumed);

                consumed += extent;
            }

            return null;
        }

        private static double ConsumedBefore(CssBox container, int slot)
        {
            var html = container.HtmlContainer!;
            var first = html.SlotStartingAt(container.Location.Y);

            if (slot <= first) return 0;

            var bottoms = LineBottomsBySlot(container, html, slot);
            var consumed = 0.0;

            for (var earlier = first; earlier < slot; earlier++)
            {
                consumed += ExtentIn(container, html, bottoms, earlier, slot - 1);
            }

            return consumed;
        }

        private static double ContentTopOf(CssBox container, int slot) =>
            slot <= container.HtmlContainer!.SlotStartingAt(container.Location.Y)
                ? container.Location.Y
                : container.HtmlContainer.PageTopOf(slot);

        // How much of the slot the box's content fills. A slot between two that hold lines is crossed
        // entirely by a block taller than it, so it counts whole.
        private static double ExtentIn(CssBox container, HtmlContainerInt html, Dictionary<int, double> bottoms, int slot, int last)
        {
            var top = ContentTopOf(container, slot);

            if (bottoms.TryGetValue(slot, out var bottom)) return Math.Max(0, bottom - top);

            return slot < last ? Math.Max(0, html.PageBottomOf(slot) - top) : 0;
        }

        // The lowest line bottom placed in each slot before `before`, over the lines of the box's own in-flow
        // content. A nested scroll container's lines are clipped to its own box, so it is left out.
        private static Dictionary<int, double> LineBottomsBySlot(CssBox container, HtmlContainerInt html, int before)
        {
            var bottoms = new Dictionary<int, double>();

            void Visit(CssBox box)
            {
                foreach (var line in box.LineBoxes)
                {
                    var slot = html.SlotStartingAt(line.LineTop);
                    if (slot >= before) continue;

                    bottoms[slot] = bottoms.TryGetValue(slot, out var known)
                        ? Math.Max(known, line.LineBottom)
                        : line.LineBottom;
                }

                foreach (var child in box.Boxes)
                {
                    if (child.IsOutOfFlow || MonolithicContent.IsScrollContainer(child)) continue;
                    Visit(child);
                }
            }

            Visit(container);
            return bottoms;
        }
    }
}

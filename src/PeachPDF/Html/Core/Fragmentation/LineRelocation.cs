using PeachPDF.Html.Core.Dom;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// One group of boxes that moves together across a fragmentainer boundary — a flex <b>line</b> or a
    /// grid <b>row</b> — together with the two sides of the break point immediately above it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The break point's two sides are given in <b>flow</b> order while the groups themselves are given
    /// in <b>block-axis</b> order, and the two disagree under <c>flex-wrap: wrap-reverse</c>. §3.1
    /// identifies a break point by flow order — the earlier sibling's <c>break-after</c> and the later
    /// one's <c>break-before</c> name the same point — but which line a fragmentainer boundary leaves
    /// above it, and which lines have to follow one that moves, are questions about geometry. So the
    /// group that moves is the one <i>below</i> the boundary whichever of the pair it happens to be.
    /// </para>
    /// <para>
    /// <see cref="Earlier"/> is not simply the previous group's boxes. Which boxes are above a break
    /// point is a question about where they <i>end</i>: a grid item spanning rows 1–2 is not above the
    /// break point before row 2 at all, because that boundary runs through the middle of it. The engine
    /// answers that for itself and hands the result in.
    /// </para>
    /// </remarks>
    /// <param name="Boxes">the boxes that move together, non-empty</param>
    /// <param name="Earlier">
    /// the boxes on the earlier-in-flow side of the break point immediately above this group, whose
    /// <c>break-after</c> governs it, or null where that side does not exist
    /// </param>
    /// <param name="Later">
    /// the boxes on the later-in-flow side of that break point, whose <c>break-before</c> governs it, or
    /// null where that side does not exist. Both null says no break point sits above this group at all.
    /// </param>
    internal readonly record struct LineGroup(
        IReadOnlyList<CssBox> Boxes,
        IReadOnlyList<CssBox>? Earlier,
        IReadOnlyList<CssBox>? Later);

    /// <summary>
    /// Moves flex lines and grid rows across fragmentainer boundaries, honouring the break values declared
    /// at the break points between them, per
    /// <see href="https://www.w3.org/TR/css-break-3/#break-between">CSS Fragmentation Level 3 §3.1</see>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A flex container's break points are between its <b>lines</b> and a grid container's between its
    /// <b>rows</b>, not between the items sharing one — so the two engines ask exactly the same questions
    /// of exactly the same inputs, and asking them in one place is what stops them drifting apart. Each
    /// engine's own work is reducing its layout to a list of <see cref="LineGroup"/>s in block-axis order;
    /// everything after that is here.
    /// </para>
    /// <para>
    /// Displacement <b>accumulates</b>: a line below one that moved to the next fragmentainer follows it
    /// there, and a line needing no relocation of its own still has to keep its place under the one that
    /// did. Moving each line by its own answer instead leaves the lines after the first relocation sitting
    /// on top of it.
    /// </para>
    /// </remarks>
    internal static class LineRelocation
    {
        /// <summary>
        /// Relocates <paramref name="lines"/>, given in block-axis order, and returns the total
        /// displacement in force below the last of them — what the container's own height has to grow by.
        /// </summary>
        /// <param name="container">the layout container, for the page grid</param>
        /// <param name="lines">the container's lines or rows, each holding at least one box</param>
        internal static double Relocate(HtmlContainerInt container, IReadOnlyList<LineGroup> lines)
        {
            // Every reservation this walk makes is re-decided from scratch below, and a pass the driver
            // re-enters runs the walk again over content that has moved. Retracting first is what stops a
            // blank page an earlier attempt asked for outliving the attempt — the same shape
            // CssBox.PlaceBlockChild's prologue has, which cannot cover an engine because it runs once per
            // layout generation rather than once per pass. Keyed on exactly the box DeltaFor keys on, so it
            // can touch no other reservation.
            foreach (var line in lines)
            {
                container.SetBlankSlotReservation(line.Boxes[0], null);
            }

            return new Walk(container, lines).Run();
        }

        /// <summary>
        /// The side required at the break point immediately above <paramref name="group"/>, or null where
        /// no forced break falls there — see <see cref="ForcedBreakBetween"/>.
        /// </summary>
        private static PageSide? ForcedBreakAbove(LineGroup group) =>
            ForcedBreakBetween(group.Earlier, group.Later);

        /// <summary>
        /// The side required at the break point whose earlier-in-flow side is <paramref name="earlier"/>
        /// and whose later-in-flow side is <paramref name="later"/>, or null where no forced break falls
        /// there. Either side may be absent.
        /// </summary>
        /// <remarks>
        /// <para>
        /// §3.1 states this of the break <i>point</i>, not of either box: a forced break occurs there if
        /// the earlier sibling's <c>break-after</c> <b>or</b> the later sibling's <c>break-before</c> has
        /// a forced value, so one side asking is sufficient and neither side can veto the other. Reading
        /// only the later side is what made <c>break-after: page</c> on a flex or grid item inert while
        /// the same intent spelt as <c>break-before</c> on the following line took effect.
        /// </para>
        /// <para>
        /// The <i>value</i> rather than a boolean, because a directional break has a side to honour and
        /// combination is not "whichever was read first". Within one side the most demanding value wins —
        /// a directional value subsumes a plain <c>page</c> — and across the two sides
        /// <see cref="BreakValues.RequiredSide"/> applies §3.1's rule for a pair: two conflicting
        /// directional values resolve to the one on the latest element in flow.
        /// </para>
        /// <para>
        /// Both sides are read one box deep rather than through the chains they begin and end
        /// (<see cref="BreakPropagation"/>): a break travelling out of an item would name a position the
        /// engine is about to overwrite, which is why propagation stops before a box whose children an
        /// engine places for itself.
        /// </para>
        /// </remarks>
        private static PageSide? ForcedBreakBetween(
            IReadOnlyList<CssBox>? earlier, IReadOnlyList<CssBox>? later)
        {
            var laterValue = later is null ? null : MostDemandingForcedValue(later.Select(box => box.BreakBefore));
            var earlierValue = earlier is null ? null : MostDemandingForcedValue(earlier.Select(box => box.BreakAfter));

            return laterValue is null && earlierValue is null
                ? null
                : BreakValues.RequiredSide(laterValue, earlierValue);
        }

        /// <summary>
        /// The forced value one side of a break point speaks with, or null where it declares none.
        /// </summary>
        /// <remarks>
        /// §3.1's combination rule, within one side of the break point: a directional value wins over a
        /// plain <c>page</c>, since the spec asks that all specified breaking requirements be honored and a
        /// directional break satisfies both. Two <i>conflicting</i> directional values cannot both be
        /// honored, and there the value on the latest element in flow wins — so the walk does not stop at
        /// the first directional value it meets. <see cref="BreakValues.RequiredSide"/> applies the same
        /// rule across the two sides.
        /// </remarks>
        private static string? MostDemandingForcedValue(IEnumerable<string?> values)
        {
            string? forced = null;
            string? directional = null;

            foreach (var value in values)
            {
                if (!BreakValues.IsForcedPageBreak(value)) continue;

                if (BreakValues.SideOf(value) is not PageSide.Any) directional = value;

                forced ??= value;
            }

            return directional ?? forced;
        }

        /// <summary>
        /// Whether a break-avoidance value on either side of the break point immediately above
        /// <paramref name="group"/> forbids an unforced break there (§3.1).
        /// </summary>
        private static bool AvoidsBreakAbove(LineGroup group) =>
            (group.Later is not null
             && group.Later.Any(box => BreakValues.AvoidsBreak(box.BreakBefore, FragmentationContext.Page)))
            || (group.Earlier is not null
                && group.Earlier.Any(box => BreakValues.AvoidsBreak(box.BreakAfter, FragmentationContext.Page)));

        /// <summary>
        /// Whether anything in a line may not be cut by a fragmentainer boundary: an item asking not to be
        /// broken, or one <see href="https://www.w3.org/TR/css-break-3/#monolithic">§2</see> says no user
        /// agent may break.
        /// </summary>
        /// <remarks>
        /// An item that neither asks nor forbids is left where it is, and the boundary cuts it — the same
        /// answer ordinary block content gets when it has no line to break at. Moving every straddling
        /// line regardless would paginate a flex container quite differently from how it renders now,
        /// which is a larger behaviour change than the break values themselves ask for.
        /// </remarks>
        private static bool MayNotBeCut(IReadOnlyList<CssBox> boxes) =>
            boxes.Any(box => BreakValues.AvoidsBreak(box.BreakInside, FragmentationContext.Page)
                             || MonolithicContent.IsMonolithic(box));

        /// <summary>
        /// How far the line spanning <paramref name="top"/>..<paramref name="bottom"/> must move down,
        /// or 0 to leave it where it is.
        /// </summary>
        /// <param name="container">the layout container, for the page grid</param>
        /// <param name="owner">
        /// the box a directional break's deliberately-blank page is recorded against — one of the line's
        /// own, so two lines each stepping over a page keep both reservations
        /// </param>
        /// <param name="top">the line's top, already displaced by everything above it that moved</param>
        /// <param name="bottom">the line's bottom, displaced the same way</param>
        /// <param name="forcedBreak">
        /// the side a forced break at the break point before it demands, or null where none falls there —
        /// see <see cref="ForcedBreakAbove"/>
        /// </param>
        /// <param name="mayNotBeCut">something in it asks not to be broken, or §2 says no user agent may</param>
        internal static double DeltaFor(
            HtmlContainerInt container, CssBox owner, double top, double bottom,
            PageSide? forcedBreak, bool mayNotBeCut)
        {
            var slot = container.SlotStartingAt(top);

            // Whether the line's top already sits on this slot's own content top. SlotStartingAt resolves a
            // top edge flush on a boundary to the *later* slot, so such a line does not merely reach this
            // fragmentainer — it *begins* it.
            var flush = top - container.PageTopOf(slot) <= HtmlContainerInt.PageBoundaryEpsilon;

            // §4.4: a forced break must not produce an empty fragmentainer, and a break at a point that
            // already *is* a fragmentainer boundary has already happened. Without this the line is moved a
            // whole further page — measured at exactly one band of filler, where 1pt less and 1pt more both
            // land correctly and only the flush case skips a slot.
            //
            // The *side* is part of what is being asked, so a directional value is satisfied by a flush
            // boundary only where the slot it begins is the side it named: §3.1 asks that the content
            // begin on a page of that side, which an already-taken break on the wrong side has not done.
            if (forcedBreak is { } already && flush && BreakValues.SlotIsOn(slot, already))
            {
                forcedBreak = null;
            }

            var straddles = mayNotBeCut
                            && bottom - HtmlContainerInt.PageBoundaryEpsilon > container.PageBottomOf(slot);

            if (forcedBreak is null && !straddles) return 0;

            // A line taller than a whole band has nowhere better to be, and moving it would ask the same
            // question again on the next fragmentainer, forever.
            if (forcedBreak is null && bottom - top > container.PageBandHeightOf(slot + 1)) return 0;

            var target = slot + 1;

            if (forcedBreak is { } side)
            {
                // §3.1's "one or two page breaks": the content after the break has to *begin* on a page of
                // the requested side. The walk starts at the slot the line would begin unsatisfied — which
                // for a flush line is *this* one, since it begins this fragmentainer rather than merely
                // reaching it, and the slot it vacates is what has to stay blank.
                //
                // Page parity alternates from slot to slot, so at most one page is ever stepped over — and
                // that slot is reserved, because a slot holding no printable content is otherwise dropped
                // (CSS Paged Media 3 §3.2), which would leave `recto` indistinguishable from `page`.
                // Retracted where no step is needed, so a pass that re-decides the same line cannot leave
                // behind a blank page the final layout does not want.
                target = flush ? slot : slot + 1;

                var stepsOver = !BreakValues.SlotIsOn(target, side);

                container.SetBlankSlotReservation(owner, stepsOver ? target : null);

                if (stepsOver) target++;
            }

            var delta = container.PageTopOf(target) - top;

            return delta > 0 ? delta : 0;
        }

        /// <summary>
        /// One pass down a container's lines, carrying the running displacement and where each line has
        /// been left. Held as state rather than threaded through parameters because §3.1 avoidance reaches
        /// <i>back</i> over lines already placed, so the walk is not a fold over one line at a time.
        /// </summary>
        private sealed class Walk(HtmlContainerInt container, IReadOnlyList<LineGroup> lines)
        {
            private readonly double[] _tops = new double[lines.Count];
            private readonly double[] _bottoms = new double[lines.Count];
            private readonly double[] _applied = new double[lines.Count];

            /// <summary>Runs the walk and returns the displacement in force below the last line.</summary>
            internal double Run()
            {
                double shift = 0;

                for (var index = 0; index < lines.Count; index++)
                {
                    var boxes = lines[index].Boxes;

                    var top = double.MaxValue;
                    var bottom = double.MinValue;

                    foreach (var box in boxes)
                    {
                        top = Math.Min(top, box.Location.Y);
                        bottom = Math.Max(bottom, box.ActualBottom);
                    }

                    _tops[index] = top;
                    _bottoms[index] = bottom;

                    var forced = ForcedBreakAbove(lines[index]);
                    var shiftBefore = shift;

                    // Where this line would land with nothing but the displacement above it — the frame the
                    // group's internal spacing is measured in, since the delta below is this line's alone.
                    var naturalTop = top + shiftBefore;

                    shift += DeltaFor(
                        container, boxes[0], naturalTop, bottom + shiftBefore, forced, MayNotBeCut(boxes));

                    Displace(index, shift);

                    // §3.1: a forced break at this break point takes precedence over any avoidance value
                    // on either side of it, so such a pair is never kept together.
                    if (index == 0 || forced is not null) continue;

                    var (head, runDelta) = AvoidanceRun(index, naturalTop, bottom - top);

                    if (head < 0) continue;

                    // The run head takes the break instead of this line, so everything from the head down —
                    // this line included — is displaced by the same amount, which preserves the spacing
                    // inside the group. This line's own delta is superseded rather than added to: it moved
                    // to open the destination fragmentainer, and the head now opens it in its place.
                    for (var member = head; member < index; member++)
                    {
                        Displace(member, _applied[member] + runDelta);
                    }

                    shift = shiftBefore + runDelta;
                    Displace(index, shift);
                }

                return shift;
            }

            /// <summary>
            /// Offsets the line at <paramref name="index"/> so that its total displacement from where the
            /// engine placed it is <paramref name="total"/>, and records where that leaves it.
            /// </summary>
            private void Displace(int index, double total)
            {
                var step = total - _applied[index];

                _applied[index] = total;

                if (step == 0) return;

                foreach (var box in lines[index].Boxes)
                {
                    box.OffsetTop(step);
                }

                _tops[index] += step;
                _bottoms[index] += step;
            }

            /// <summary>
            /// The run of earlier lines that has to travel with the line at <paramref name="index"/>
            /// because an <c>avoid</c> forbids the break the fragmentainer boundary is taking above it,
            /// and how far that run moves. A head of <c>-1</c> means nothing moves — either no break falls
            /// there, none is forbidden, or §4.3 relaxed the constraint away.
            /// </summary>
            /// <remarks>
            /// <para>
            /// This is keep-with-next over a chain of <i>lines</i>. A forced break only ever moves the
            /// later line; avoidance has to move the <b>earlier</b> one, which is why it reaches back over
            /// lines already placed rather than being another argument to <see cref="DeltaFor"/>.
            /// </para>
            /// <para>
            /// Asked <i>after</i> the line has landed, because whether a boundary really falls at the break
            /// point is a question about where it landed: a line the boundary cuts through has not taken a
            /// break at the point above it at all, and there is nothing there to avoid.
            /// </para>
            /// </remarks>
            /// <param name="index">the line the break point being asked about sits above</param>
            /// <param name="naturalTop">
            /// where the line sat before its own delta — the frame the lines above it share, and so the one
            /// the group's internal spacing is measured in
            /// </param>
            /// <param name="extent">the line's own height</param>
            private (int Head, double Delta) AvoidanceRun(int index, double naturalTop, double extent)
            {
                var destination = container.SlotStartingAt(_tops[index]);

                // Does a boundary actually fall at this break point? Bands are contiguous, so the line
                // above ending in an earlier slot than this one begins in *is* the statement that it does.
                if (container.SlotEndingAt(_bottoms[index - 1]) == destination) return (-1, 0);

                if (!AvoidsBreakAbove(lines[index])) return (-1, 0);

                // The chain reaches back while every break point along it forbids a break and none forces
                // one: §3.1's forced values take precedence, so a run never reaches through one.
                var chainStart = index - 1;

                while (chainStart > 0
                       && ForcedBreakAbove(lines[chainStart]) is null
                       && AvoidsBreakAbove(lines[chainStart]))
                {
                    chainStart--;
                }

                var candidates = _tops.AsSpan(chainStart, index - chainStart);

                var head = EarlyBreak.TravellingRunHead(
                    candidates,
                    naturalTop,
                    container.PageTopOf(container.SlotEndingAt(_bottoms[index - 1])),
                    extent,
                    container.PageBandHeightOf(destination));

                return head < candidates.Length
                    ? (chainStart + head, container.PageTopOf(destination) - _tops[chainStart + head])
                    : (-1, 0);
            }
        }
    }
}

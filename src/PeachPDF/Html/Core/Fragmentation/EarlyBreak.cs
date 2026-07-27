using System.Collections.Generic;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// Why layout decided to break earlier than the content itself required, per
    /// <see href="https://www.w3.org/TR/css-break-3/#possible-breaks">CSS Fragmentation Level 3
    /// §4.1–§4.3</see>.
    /// </summary>
    /// <remarks>
    /// Recorded rather than merely acted on because §4.3's "best possible break point" is a question
    /// about <i>why</i> a candidate exists, not only about where it falls: progressive relaxation has
    /// to know which constraint to give up first, and a bare coordinate says nothing about that.
    /// </remarks>
    internal enum EarlyBreakReason
    {
        /// <summary>
        /// A run of preceding siblings chained to the breaking box by <c>break-after: avoid</c> /
        /// <c>break-before: avoid</c> would otherwise be stranded on the page its content just left
        /// (<see href="https://www.w3.org/TR/css-break-3/#break-between">§3.1</see>).
        /// </summary>
        KeepWithNext,

        /// <summary>The box asked not to be broken: <c>break-inside: avoid</c> or <c>avoid-page</c> (§3.2).</summary>
        AvoidBreakInside,

        /// <summary>
        /// The box may not be broken by any user agent — a replaced element or a scroll container
        /// (<see href="https://www.w3.org/TR/css-break-3/#monolithic">§2</see>).
        /// </summary>
        Monolithic,

        /// <summary>Too few lines would fall on one side of the break to satisfy <c>orphans</c>/<c>widows</c> (§5.4).</summary>
        OrphansWidows
    }

    /// <summary>
    /// A break decision taken at the point it is discovered: <see cref="BeforeBox"/> is to begin at
    /// <see cref="Top"/>, in pagination slot <see cref="Slot"/>, rather than where ordinary flow put it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The alternative — and what each of these sites used to do — is to lay the box out where it does
    /// not fit and then translate the result. That is cheaper to write and wrong in a way that shows: a
    /// box which had already begun flowing its text across the boundary gets shifted <i>uniformly</i>,
    /// while the lines past the boundary had been laid out against the next band's top. The shift
    /// carries that gap into the box as interior blank space and leaves its height inflated by the same
    /// amount. Stating the decision and laying the box out once, at its final position, removes the gap
    /// rather than moving it around.
    /// </para>
    /// <para>
    /// <see cref="BeforeBox"/> is <b>not</b> always the box that discovered the decision. A
    /// keep-with-next run pull is discovered by the box that does not fit, but falls before the
    /// <i>head of its preceding run</i> — which is the whole reason that case needs a different vehicle
    /// from the others: a box can re-run itself, but only its parent can re-run a sibling placed before
    /// it.
    /// </para>
    /// <para>
    /// <see cref="Top"/> is computed once, by the site that discovers the decision, and travels with
    /// it. Re-deriving it where it is applied would ask the same "does this fit?" question against the
    /// same geometry, reach the same answer, and break again forever — the lesson
    /// <see cref="BlockBreakToken.ResumeTopOverride"/> already records for the margin-truncation path.
    /// </para>
    /// </remarks>
    /// <param name="BeforeBox">the box the break falls before, which begins the next fragmentainer</param>
    /// <param name="Subject">
    /// the box that travels — the one that discovered the decision, or, where the break point before it is
    /// really its container's
    /// (<see href="https://www.w3.org/TR/css-break-3/#break-between">§3.1</see> propagation), the outermost
    /// container it begins. Carrying it out on the container is what stops that container from being left
    /// spanning the boundary while its content moves off it.
    /// </param>
    /// <param name="Top">the document Y <paramref name="BeforeBox"/> is to begin at</param>
    /// <param name="Slot">the pagination slot <paramref name="Top"/> falls in</param>
    /// <param name="Reason">what made the break necessary</param>
    internal sealed record EarlyBreak(CssBox BeforeBox, CssBox Subject, double Top, int Slot, EarlyBreakReason Reason)
    {
        /// <summary>
        /// The run of preceding siblings that travels with the break, empty when the break falls before
        /// <see cref="BeforeBox"/> alone.
        /// </summary>
        internal IReadOnlyList<CssBox> KeepWithNextRun { get; private init; } = [];

        /// <summary>
        /// Which of §4.3's tiers this decision settled at — how much of what was asked for it could keep.
        /// See <see cref="BreakRelaxation"/> for the ladder.
        /// </summary>
        internal BreakRelaxation Relaxation { get; private init; } = BreakRelaxation.None;

        /// <summary>
        /// Works out where the break falls, given that <paramref name="box"/> itself needs to start at
        /// <paramref name="targetTop"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The answer is <paramref name="box"/> unless a run of preceding siblings is chained to it by
        /// <c>avoid</c> values, in which case the break falls before the <i>head</i> of that run — a
        /// heading must not be left at the bottom of the page whose content just moved off it.
        /// </para>
        /// <para>
        /// Two guards decide whether the run really comes along, and both are §5.3 relaxation rather
        /// than bookkeeping: the run has to start on the page being left (otherwise "pull it along"
        /// names content in a fragmentainer this decision has no business touching), and the run plus
        /// this box's own content has to fit the destination band. An <c>avoid</c> that cannot be
        /// satisfied is relaxed and the box moves alone, exactly as if it had never been written.
        /// </para>
        /// </remarks>
        /// <remarks>
        /// <para>
        /// The run is collected at <paramref name="box"/>'s <b>propagation anchor</b> rather than at the box
        /// itself (<see cref="BreakPropagation.AnchorForBreakBefore"/>). Where the box is a container's first
        /// in-flow child it has no preceding sibling of its own, but §3.1's break point before it is the one
        /// before that container — so the heading to keep with it is the container's sibling, and it is the
        /// container that travels. Without this the heading is stranded on the page the content just left,
        /// and the structurally equivalent document with the paragraph as the heading's own next sibling
        /// behaves differently.
        /// </para>
        /// <para>
        /// With <b>no</b> run to collect, the anchor still travels — that is the whole of §3.1 propagation
        /// for the §4.3 movers, and without it <c>&lt;div class="card"&gt;&lt;table&gt;…&lt;/table&gt;&lt;/div&gt;</c>
        /// relocates the table and leaves the card spanning the boundary, painting an empty copy of its own
        /// background, border and padding on the fragmentainer the table just left. It travels only where it
        /// <i>fits</i> the destination, and that guard is not bookkeeping: <c>TryRestartAt</c> has no fit test
        /// of its own, so a box laid out again where it still does not fit breaks again from its new top,
        /// opens another pass, and is re-asked — the runaway <see cref="CssBox.CanBeLaidOutAgain"/> exists to
        /// prevent, measured at 100,000 pages. The anchor is mid-layout when this is asked, so what is
        /// measured is the same extent the run case measures: from the anchor's own top down to the bottom of
        /// the box that broke, since the anchor's own <c>ActualBottom</c> is not settled yet.
        /// </para>
        /// </remarks>
        /// <param name="box">the box that discovered it does not fit</param>
        /// <param name="targetTop">the document Y <paramref name="box"/> would move to on its own</param>
        /// <param name="reason">what made the break necessary</param>
        internal static EarlyBreak Discover(CssBox box, double targetTop, EarlyBreakReason reason)
        {
            var container = box.HtmlContainer!;
            var slot = container.SlotStartingAt(targetTop);
            var alone = new EarlyBreak(box, box, targetTop, slot, reason)
            {
                Relaxation = BreakRelaxation.RunDropped
            };

            var subject = BreakPropagation.AnchorForBreakBefore(box);

            // The run has to start on the page being left. Otherwise "pull it along" names content in
            // a fragmentainer this decision has no business touching — expressed, as it has been since
            // this guard was written, as the run's top lying below that page's own content top.
            var ownPageTop = container.PageTopOf(container.PageIndexOf(subject.Location.Y));

            // "Fits on one page" is asked of the destination band, which per-page @page margins can
            // size differently from the one being left. The extent measured is from the subject's own top
            // down to the bottom of the box that broke: where the two differ the subject is still mid-layout,
            // so its own ActualBottom is not settled yet.
            var destinationBand = container.PageBandHeightOf(container.PageIndexOf(targetTop));
            var subjectExtent = box.ActualBottom - subject.Location.Y;

            // The anchor travels whether or not a run is chained to it — §3.1's break point before this box
            // is the container's, so leaving the container behind is a relaxation, not the normal case.
            var withTheContainer = ReferenceEquals(subject, box) || !CanTravelTo(subject, subjectExtent, destinationBand)
                ? alone with { Relaxation = ReferenceEquals(subject, box) ? BreakRelaxation.None : BreakRelaxation.ContainerLeftBehind }
                : alone with { BeforeBox = subject, Subject = subject, Relaxation = BreakRelaxation.None };

            var (run, _, trimmed) = TravellingRun(
                subject, subject.Location.Y, ownPageTop, subjectExtent, destinationBand);

            if (run.Count == 0) return withTheContainer;

            // The retained run's own top lands where this box would have gone alone; the box follows at
            // its original distance below it, so the spacing inside the group is preserved.
            return alone with
            {
                BeforeBox = run[0],
                Subject = subject,
                KeepWithNextRun = run,
                Reason = EarlyBreakReason.KeepWithNext,
                Relaxation = trimmed ? BreakRelaxation.RunTrimmed : BreakRelaxation.None
            };
        }

        /// <summary>
        /// The most of <paramref name="subject"/>'s preceding keep-with-next run that can travel with it
        /// when it moves to the next fragmentainer, and how far above <paramref name="subjectTop"/> the
        /// retained run's head sits. Empty when none of it can, which is §4.3's "give the constraint up".
        /// </summary>
        /// <remarks>
        /// <para>
        /// §4.3 relaxation, one tier at a time (see <see cref="BreakRelaxation"/>): a run that does not
        /// fit as a whole is trimmed from its <i>front</i> rather than dropped, because the members
        /// nearest the breaking box are the ones the chain is about — a subheading immediately above a
        /// table matters more than the heading above that.
        /// </para>
        /// <para>
        /// Separate from <see cref="Discover"/> because the two whole-table pre-checks in
        /// <c>CssLayoutEngineTable.LayoutCells</c> ask the same question at a point where no decision can
        /// be stated: they run before the rows are placed, so the subject has no settled extent of its own
        /// and nothing to be laid out again. They pass an <i>estimate</i> as
        /// <paramref name="subjectExtent"/> and carry the move out themselves. Sharing the ladder is what
        /// stops those guards being a second, drifting copy of these.
        /// </para>
        /// </remarks>
        /// <param name="subject">the box that moves, and whose preceding siblings the chain is read from</param>
        /// <param name="subjectTop">the coordinate <paramref name="subject"/> would move away from</param>
        /// <param name="ownPageTop">the content top of the fragmentainer being left</param>
        /// <param name="subjectExtent">how much room the subject itself still needs in the destination</param>
        /// <param name="destinationBand">the destination fragmentainer's content height</param>
        internal static (List<CssBox> Run, double ExtraAbove, bool Trimmed) TravellingRun(
            CssBox subject, double subjectTop, double ownPageTop, double subjectExtent, double destinationBand)
        {
            var run = DomUtils.GetPrecedingKeepWithNextRun(subject, FragmentationContext.Page);
            var tops = new double[run.Count];

            for (var i = 0; i < run.Count; i++) tops[i] = run[i].Location.Y;

            var head = TravellingRunHead(tops, subjectTop, ownPageTop, subjectExtent, destinationBand);

            return head < run.Count
                ? (run.GetRange(head, run.Count - head), subjectTop - run[head].Location.Y, head > 0)
                : ([], 0, false);
        }

        /// <summary>
        /// Which member of a keep-with-next run the retained run starts at — §4.3's ladder, one tier at a
        /// time. <paramref name="memberTops"/>.Count means none of it can travel, which is the rung where
        /// the constraint is given up altogether.
        /// </summary>
        /// <remarks>
        /// The ladder itself is arithmetic over coordinates, and deliberately says nothing about what a
        /// run <i>member</i> is: <see cref="TravellingRun"/> reads a chain of preceding siblings, while
        /// <see cref="LineRelocation"/> reads a chain of flex lines or grid rows, which have no sibling
        /// relationship at all. Both ask the same two questions in the same order, and stating them once
        /// is what stops the second copy relaxing differently from the first.
        /// </remarks>
        /// <param name="memberTops">the candidate members' tops, in document order (topmost first)</param>
        /// <param name="subjectTop">the coordinate the subject would move away from</param>
        /// <param name="ownPageTop">the content top of the fragmentainer being left</param>
        /// <param name="subjectExtent">how much room the subject itself still needs in the destination</param>
        /// <param name="destinationBand">the destination fragmentainer's content height</param>
        internal static int TravellingRunHead(
            IReadOnlyList<double> memberTops, double subjectTop, double ownPageTop,
            double subjectExtent, double destinationBand)
        {
            for (var head = 0; head < memberTops.Count; head++)
            {
                var extraAbove = subjectTop - memberTops[head];

                // A member at or below the subject cannot be "above" it, and neither can anything after it
                // in the run — so there is nothing left to trim towards.
                if (extraAbove <= 0) break;

                if (extraAbove >= subjectTop - ownPageTop) continue;
                if (extraAbove + subjectExtent > destinationBand) continue;

                return head;
            }

            return memberTops.Count;
        }

        /// <summary>
        /// Whether the propagation anchor can take the break itself rather than being left spanning the
        /// boundary while its content moves off it.
        /// </summary>
        /// <remarks>
        /// It has to fit the destination — see <see cref="Discover"/>'s own remarks for why that is the
        /// guard the whole redirect hangs on — and it has to be a box that <i>places itself</i>, since the
        /// target reaches it through <c>PlaceBlockBox</c> and any other box would ignore it and never move.
        /// </remarks>
        private static bool CanTravelTo(CssBox subject, double subjectExtent, double destinationBand) =>
            subject.PlacesItselfAsBlockBox && subjectExtent <= destinationBand;

        /// <summary>
        /// Whether <paramref name="token"/>, produced by <paramref name="container"/>, records a break before
        /// that container's own first in-flow child — a break point §3.1 says is the container's, so the
        /// container travels with it rather than being left spanning the boundary.
        /// </summary>
        internal static bool NamesAPropagatingBreakBefore(BreakToken token, CssBox container)
        {
            if (token is not BlockBreakToken { IsBreakBefore: true, ChildToken: null } block
                || !ReferenceEquals(block.Box, container))
            {
                return false;
            }

            return BreakPropagation.FirstInFlowChild(container) is { } first
                   && BreakPropagation.PropagatesBreakBeforeOutward(first)
                   && container.Boxes.IndexOf(first) == block.ResumeChildIndex;
        }
    }
}

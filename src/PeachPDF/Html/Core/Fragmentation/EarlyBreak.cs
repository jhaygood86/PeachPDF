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
    /// <param name="Top">the document Y <paramref name="BeforeBox"/> is to begin at</param>
    /// <param name="Slot">the pagination slot <paramref name="Top"/> falls in</param>
    /// <param name="Reason">what made the break necessary</param>
    internal sealed record EarlyBreak(CssBox BeforeBox, double Top, int Slot, EarlyBreakReason Reason)
    {
        /// <summary>
        /// The run of preceding siblings that travels with the break, empty when the break falls before
        /// <see cref="BeforeBox"/> alone.
        /// </summary>
        internal IReadOnlyList<CssBox> KeepWithNextRun { get; private init; } = [];

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
        /// <param name="box">the box that discovered it does not fit</param>
        /// <param name="targetTop">the document Y <paramref name="box"/> would move to on its own</param>
        /// <param name="reason">what made the break necessary</param>
        internal static EarlyBreak Discover(CssBox box, double targetTop, EarlyBreakReason reason)
        {
            var container = box.HtmlContainer!;
            var slot = container.PageIndexOf(targetTop + HtmlContainerInt.PageBoundaryEpsilon);
            var alone = new EarlyBreak(box, targetTop, slot, reason);

            var run = DomUtils.GetPrecedingKeepWithNextRun(box);

            if (run.Count == 0) return alone;

            var runTop = run[0].Location.Y;
            var extraAbove = box.Location.Y - runTop;

            if (extraAbove <= 0) return alone;

            // The run has to start on the page being left. Otherwise "pull it along" names content in
            // a fragmentainer this decision has no business touching — expressed, as it has been since
            // this guard was written, as the run's top lying below that page's own content top.
            var ownPageTop = container.PageTopOf(container.PageIndexOf(box.Location.Y));

            if (extraAbove >= box.Location.Y - ownPageTop) return alone;

            // "Fits on one page" is asked of the destination band, which per-page @page margins can
            // size differently from the one being left.
            var destinationBand = container.PageBandHeightOf(container.PageIndexOf(targetTop));

            if (extraAbove + box.ActualBottom - box.Location.Y > destinationBand) return alone;

            // The run's own top lands where this box would have gone alone; the box follows at its
            // original distance below the run, so the spacing inside the group is preserved.
            return alone with { BeforeBox = run[0], KeepWithNextRun = run, Reason = EarlyBreakReason.KeepWithNext };
        }
    }
}

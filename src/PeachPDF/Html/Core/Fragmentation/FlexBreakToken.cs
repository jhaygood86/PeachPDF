using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// One item of a flex line that did not finish its own content in this fragmentainer, with the
    /// break token its content produced — the flex analogue of <c>UnfinishedTableCell</c>.
    /// </summary>
    internal sealed record UnfinishedFlexItem(CssBox Item, BreakToken Token);

    /// <summary>
    /// A flex container's items are laid out for real (<see cref="Dom.CssLayoutEngineFlex"/>'s commit
    /// pass, run once every item sits at its final position) and the walk stopped inside one line
    /// because one or more of its items did not finish their own content in this fragmentainer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Row-direction items sharing one line are <see href="https://www.w3.org/TR/css-break-3/#parallel-flows">
    /// §2.1 parallel flows</see> in exactly the sense css-tables-3 §6.1 already gives a table row's cells —
    /// the spec's own example of a parallel flow is "the contents of each flex item in a flex layout row" —
    /// so the per-line shape of this token mirrors <see cref="TableBreakToken"/> field-for-field rather than
    /// inventing a new shape.
    /// </para>
    /// <para>
    /// <b>Row-direction only.</b> A <c>flex-direction: column</c>/<c>column-reverse</c> line's items are a
    /// <i>sequential</i> flow, not this shape's "parallel flows" — see
    /// <c>.claude/accepted-gaps/flex-column-container-has-no-break-points-between-items.md</c> and
    /// <c>FlexColumnBreakToken</c>, its own analogue closer to ordinary block-child continuation.
    /// </para>
    /// <para>
    /// The commit pass walks <i>every</i> line in one pass, committing as many whole lines as fit the
    /// current fragmentainer, so this token carries <see cref="ResumeLineIndex"/> and the full
    /// <see cref="Lines"/> grouping — the way <c>TableBreakToken</c> carries <c>ResumeRowIndex</c> for
    /// the same reason, and the grid engine's own <c>GridBreakToken</c> mirrors for its rows.
    /// <see cref="Lines"/> travels on the token rather than being recomputed on resume because
    /// <see cref="Dom.CssLayoutEngineFlex.Layout"/>'s resume short-circuit returns before line
    /// collection/sizing run again — a resumed pass has no local line list to rebuild it from.
    /// </para>
    /// </remarks>
    /// <param name="Box">the flex container to resume</param>
    /// <param name="ResumeSlotIndex">
    /// the pagination slot to resume in, taken from the items that actually stopped — they were all laid
    /// out into the same fragmentainer within one pass, so they agree.
    /// </param>
    /// <param name="ResumeLineIndex">
    /// the index into <see cref="Lines"/> the walk stopped in — already in block-axis (down-the-page)
    /// order, not necessarily the container's own source order (see <c>wrap-reverse</c>).
    /// </param>
    /// <param name="Lines">every line's items, in block-axis order, fixed once line collection has run</param>
    /// <param name="UnfinishedItems">the stopping line's items that stopped, each with its own content's break token</param>
    /// <param name="FinishedItems">
    /// the stopping line's items that <b>finished</b> here, which a resumed pass must not re-enter —
    /// lines before <see cref="ResumeLineIndex"/> are implicitly fully finished and are never revisited.
    /// </param>
    /// <param name="PlacementOrigin">
    /// <see cref="Dom.CssBox.Location"/> of the flex container itself at the moment every not-yet-committed
    /// item's own <c>Location</c> was last known correct — see <c>GridBreakToken.PlacementOrigin</c>'s own
    /// remarks and <c>CssLayoutEngineFlex.ResumeCommitPass</c> for why a resumed pass needs this.
    /// </param>
    internal sealed record FlexBreakToken(
        CssBox Box,
        int ResumeSlotIndex,
        int ResumeLineIndex,
        IReadOnlyList<IReadOnlyList<CssBox>> Lines,
        IReadOnlyList<UnfinishedFlexItem> UnfinishedItems,
        IReadOnlyList<CssBox> FinishedItems,
        RPoint PlacementOrigin) : BreakToken(Box, ResumeSlotIndex)
    {
        /// <inheritdoc />
        internal override IReadOnlyList<BreakToken> FanOutContinuations =>
            UnfinishedItems.Select(item => item.Token).ToList();

        /// <summary>
        /// Compared by <b>contents</b>, for the same reason <see cref="TableBreakToken"/> states it: the
        /// driver's no-progress backstop is an equality test between consecutive passes' records, and a
        /// record's compiler-generated equality compares these collections by <i>reference</i> — every pass
        /// builds fresh ones, so a flex container that made no progress would never compare equal to itself
        /// and would spin to the pass cap instead of falling back to the relaxation ladder's last rung.
        /// <see cref="Lines"/> is structural data fixed once by line collection and never varies
        /// independently for a given <c>(Box, ResumeLineIndex, UnfinishedItems, FinishedItems)</c> tuple, so
        /// it does not need to participate. <see cref="PlacementOrigin"/> is excluded for the opposite
        /// reason: content too tall for any single fragmentainer walks through many of them while genuinely
        /// making zero progress, so two tokens naming the same stuck item in different fragmentainers must
        /// still compare equal for the backstop to catch that walk rather than spin on it.
        /// </summary>
        public bool Equals(FlexBreakToken? other) =>
            other is not null
            && ReferenceEquals(Box, other.Box)
            && ResumeSlotIndex == other.ResumeSlotIndex
            && ResumeLineIndex == other.ResumeLineIndex
            && UnfinishedItems.SequenceEqual(other.UnfinishedItems)
            && FinishedItems.SequenceEqual(other.FinishedItems);

        public override int GetHashCode() =>
            HashCode.Combine(Box, ResumeSlotIndex, ResumeLineIndex, UnfinishedItems.Count, FinishedItems.Count);
    }
}

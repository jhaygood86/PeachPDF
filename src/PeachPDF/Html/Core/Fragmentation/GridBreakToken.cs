using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// One item of a grid row that did not finish its own content in this fragmentainer, with the
    /// break token its content produced — the grid analogue of <c>UnfinishedFlexItem</c>.
    /// </summary>
    internal sealed record UnfinishedGridItem(CssBox Item, BreakToken Token);

    /// <summary>
    /// A grid container's rows are laid out for real (<see cref="Dom.CssLayoutEngineGrid"/>'s commit
    /// pass, run once every item sits at its final position) and the walk stopped inside one row
    /// because one or more of its items did not finish their own content in this fragmentainer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A row's items sharing one block-axis range are <see href="https://www.w3.org/TR/css-break-3/#parallel-flows">
    /// §2.1 parallel flows</see> in exactly the sense a flex row's items are, so the per-row shape of
    /// this token mirrors <see cref="FlexBreakToken"/> field-for-field. Unlike <see cref="FlexBreakToken"/>
    /// (permanently scoped to a single line), a grid container's commit pass walks <i>every</i> row in
    /// one pass, committing as many whole rows as fit the current fragmentainer — so this token also
    /// carries <see cref="ResumeRowIndex"/> and the full <see cref="Rows"/> grouping, the way
    /// <c>TableBreakToken</c> carries <c>ResumeRowIndex</c> for the same reason.
    /// </para>
    /// <para>
    /// <see cref="Rows"/> and <see cref="SubgridContexts"/> travel on the token rather than being
    /// recomputed on resume because <c>CssLayoutEngineGrid.Layout</c>'s resume short-circuit returns
    /// before placement/track-sizing run again — a resumed pass has no local <c>placements</c> or
    /// <c>Track[]</c> arrays to rebuild them from.
    /// </para>
    /// </remarks>
    /// <param name="Box">the grid container to resume</param>
    /// <param name="ResumeSlotIndex">
    /// the pagination slot to resume in, taken from the items that actually stopped — they were all
    /// laid out into the same fragmentainer within one pass, so they agree.
    /// </param>
    /// <param name="ResumeRowIndex">the index into <see cref="Rows"/> the walk stopped in</param>
    /// <param name="Rows">every row's items, in block-axis order, fixed once placement has run</param>
    /// <param name="UnfinishedItems">the stopping row's items that stopped, each with its own content's break token</param>
    /// <param name="FinishedItems">
    /// the stopping row's items that <b>finished</b> here, which a resumed pass must not re-enter —
    /// rows before <see cref="ResumeRowIndex"/> are implicitly fully finished and are never revisited.
    /// </param>
    /// <param name="SubgridContexts">
    /// each subgrid item's adopted track geometry, captured once from the fresh pass's <c>columns</c>/
    /// <c>rows</c> arrays so a resumed pass can re-thread it without recomputing track sizing.
    /// </param>
    /// <param name="PlacementOrigin">
    /// <see cref="Dom.CssBox.Location"/> of the grid container itself at the moment every not-yet-committed
    /// item's own <c>Location</c> was last known correct. <c>CssBox.ResumeInTheNextFragmentainer</c> moves
    /// only the container when a resumed pass lands in a new fragmentainer (a new multicolumn column, most
    /// concretely) — "only this box moves, not its subtree" is exactly right for a box whose descendants are
    /// already fully placed, but this container's remaining rows are not, so nothing gives them the same
    /// correction. Comparing the container's current <c>Location</c> against this origin is what lets a
    /// resumed pass detect that correction is needed and by how much — see
    /// <c>CssLayoutEngineGrid.ResumeCommitPass</c>.
    /// </param>
    internal sealed record GridBreakToken(
        CssBox Box,
        int ResumeSlotIndex,
        int ResumeRowIndex,
        IReadOnlyList<IReadOnlyList<CssBox>> Rows,
        IReadOnlyList<UnfinishedGridItem> UnfinishedItems,
        IReadOnlyList<CssBox> FinishedItems,
        IReadOnlyDictionary<CssBox, GridSubgridContext> SubgridContexts,
        RPoint PlacementOrigin) : BreakToken(Box, ResumeSlotIndex)
    {
        /// <summary>
        /// Compared by <b>contents</b>, for the same reason <see cref="FlexBreakToken"/> states it: the
        /// driver's no-progress backstop is an equality test between consecutive passes' records, and a
        /// record's compiler-generated equality compares collections by <i>reference</i> — every pass
        /// builds fresh ones. <see cref="Rows"/>/<see cref="SubgridContexts"/> are structural data fixed
        /// once by placement and never vary independently for a given <c>(Box, ResumeRowIndex,
        /// UnfinishedItems, FinishedItems)</c> tuple, so they don't need to participate here — two tokens
        /// with the same box, row and item sets made the same amount of progress regardless of whether
        /// their (identical, re-derived-once) structural data happens to be the same object.
        /// <see cref="PlacementOrigin"/> is deliberately excluded too, for the opposite reason: content
        /// too tall for any single fragmentainer walks through many of them while genuinely making zero
        /// progress, so two tokens naming the same stuck item in different fragmentainers must still
        /// compare equal for the backstop to catch that walk rather than spin on it.
        /// </summary>
        public bool Equals(GridBreakToken? other) =>
            other is not null
            && ReferenceEquals(Box, other.Box)
            && ResumeSlotIndex == other.ResumeSlotIndex
            && ResumeRowIndex == other.ResumeRowIndex
            && UnfinishedItems.SequenceEqual(other.UnfinishedItems)
            && FinishedItems.SequenceEqual(other.FinishedItems);

        public override int GetHashCode() =>
            HashCode.Combine(Box, ResumeSlotIndex, ResumeRowIndex, UnfinishedItems.Count, FinishedItems.Count);
    }
}

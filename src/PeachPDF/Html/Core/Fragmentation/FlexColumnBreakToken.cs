using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// One <c>flex-direction: column</c>/<c>column-reverse</c> line's own resumption state: which item
    /// (by index into its line's block-axis-ordered item list) the sequential walk stopped at, and that
    /// item's own content break token.
    /// </summary>
    internal sealed record ColumnLineCursor(int LineIndex, int ResumeItemIndex, BreakToken ItemToken);

    /// <summary>
    /// A column-direction flex container's lines are laid out for real (<see cref="Dom.CssLayoutEngineFlex"/>'s
    /// column commit pass) and one or more lines stopped mid-sequence because an item's own content did
    /// not finish in this fragmentainer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A column-direction line's items are a <i>sequential</i> flow along the block axis — not the
    /// row/row-reverse "parallel flows" shape <see cref="FlexBreakToken"/> models — so this token is
    /// shaped like <c>BlockBreakToken</c> (index into a list, plus that entry's own break token) rather
    /// than <see cref="FlexBreakToken"/>'s parallel <c>UnfinishedItems</c>/<c>FinishedItems</c> lists.
    /// Because a <c>flex-wrap</c> column container can have several lines running <b>side by side</b>
    /// (each its own independent sequential run, sharing no block-axis range with the others), the shape
    /// is two-level: <see cref="UnfinishedLines"/> holds one <see cref="ColumnLineCursor"/> per line still
    /// mid-sequence, committed in parallel with each other — a stalled line does not block a different
    /// line's sequence from continuing or finishing on its own.
    /// </para>
    /// <para>
    /// Scoped, for now, to item <b>content</b> fragmentation only: a forced <c>break-before</c>/
    /// <c>break-after</c>/<c>break-inside: avoid</c> between two items in a line is not yet honored (see
    /// <c>.claude/accepted-gaps/flex-column-container-has-no-break-points-between-items.md</c>, issue
    /// #455) — every line's items are walked and committed unconditionally, stopping only where an
    /// item's own content genuinely does not fit.
    /// </para>
    /// </remarks>
    /// <param name="Box">the flex container to resume</param>
    /// <param name="ResumeSlotIndex">
    /// the pagination slot to resume in, taken from the lines that actually stopped — they were all laid
    /// out into the same fragmentainer within one pass, so they agree.
    /// </param>
    /// <param name="Lines">
    /// every line's items, already in block-axis (top-to-bottom) order — reversed from source order for
    /// <c>column-reverse</c>, since <c>FlexItem</c> collection does not reorder <c>FlexLine.Items</c>
    /// itself (only <see cref="Dom.CssLayoutEngineFlex.AssignLocations"/>'s main-axis math reads
    /// <c>_isReverse</c>).
    /// </param>
    /// <param name="UnfinishedLines">one cursor per line still mid-sequence</param>
    /// <param name="FinishedLineIndexes">
    /// indexes into <see cref="Lines"/> of lines that finished every item — a resumed pass must not
    /// re-enter these.
    /// </param>
    /// <param name="PlacementOrigin">
    /// <see cref="Dom.CssBox.Location"/> of the flex container itself at the moment every not-yet-committed
    /// item's own <c>Location</c> was last known correct — see <c>GridBreakToken.PlacementOrigin</c>'s own
    /// remarks and <c>CssLayoutEngineFlex.ResumeColumnCommitPass</c> for why a resumed pass needs this.
    /// </param>
    internal sealed record FlexColumnBreakToken(
        CssBox Box,
        int ResumeSlotIndex,
        IReadOnlyList<IReadOnlyList<CssBox>> Lines,
        IReadOnlyList<ColumnLineCursor> UnfinishedLines,
        IReadOnlyList<int> FinishedLineIndexes,
        RPoint PlacementOrigin) : BreakToken(Box, ResumeSlotIndex)
    {
        /// <summary>
        /// Compared by <b>contents</b>, for the same reason <see cref="FlexBreakToken"/> states it — see
        /// its own remarks for why <see cref="Lines"/>/<see cref="PlacementOrigin"/> are excluded.
        /// </summary>
        public bool Equals(FlexColumnBreakToken? other) =>
            other is not null
            && ReferenceEquals(Box, other.Box)
            && ResumeSlotIndex == other.ResumeSlotIndex
            && UnfinishedLines.SequenceEqual(other.UnfinishedLines)
            && FinishedLineIndexes.SequenceEqual(other.FinishedLineIndexes);

        public override int GetHashCode() =>
            HashCode.Combine(Box, ResumeSlotIndex, UnfinishedLines.Count, FinishedLineIndexes.Count);
    }
}

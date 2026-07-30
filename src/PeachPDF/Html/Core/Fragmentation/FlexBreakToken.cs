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
    /// pass, run once every item sits at its final position) and one or more did not finish their own
    /// content in this fragmentainer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Row-direction items sharing one line are <see href="https://www.w3.org/TR/css-break-3/#parallel-flows">
    /// §2.1 parallel flows</see> in exactly the sense css-tables-3 §6.1 already gives a table row's cells —
    /// the spec's own example of a parallel flow is "the contents of each flex item in a flex layout row" —
    /// so this token mirrors <see cref="TableBreakToken"/> field-for-field rather than inventing a new shape.
    /// </para>
    /// <para>
    /// <b>Scoped to a single flex line.</b> A <c>flex-wrap</c> container whose second line would itself need
    /// resumption is out of scope for now — see
    /// <c>.claude/accepted-gaps/flex-multiline-item-content-fragmentation.md</c> — so this token carries no
    /// line index. Every item of the (one) line is either in <see cref="UnfinishedItems"/> or
    /// <see cref="FinishedItems"/>; there is no "not yet entered" state to represent.
    /// </para>
    /// </remarks>
    /// <param name="Box">the flex container to resume</param>
    /// <param name="ResumeSlotIndex">
    /// the pagination slot to resume in, taken from the items that actually stopped — they were all laid
    /// out into the same fragmentainer within one pass, so they agree.
    /// </param>
    /// <param name="UnfinishedItems">the line's items that stopped, each with its own content's break token</param>
    /// <param name="FinishedItems">
    /// the line's items that <b>finished</b> here, which a resumed pass must not re-enter — both this and
    /// "never entered" are absent from <paramref name="UnfinishedItems"/>, and only a finished item already
    /// has content committed to an earlier fragmentainer.
    /// </param>
    internal sealed record FlexBreakToken(
        CssBox Box,
        int ResumeSlotIndex,
        IReadOnlyList<UnfinishedFlexItem> UnfinishedItems,
        IReadOnlyList<CssBox> FinishedItems) : BreakToken(Box, ResumeSlotIndex)
    {
        /// <summary>
        /// Compared by <b>contents</b>, for the same reason <see cref="TableBreakToken"/> states it: the
        /// driver's no-progress backstop is an equality test between consecutive passes' records, and a
        /// record's compiler-generated equality compares these collections by <i>reference</i> — every pass
        /// builds fresh ones, so a flex container that made no progress would never compare equal to itself
        /// and would spin to the pass cap instead of falling back to the relaxation ladder's last rung.
        /// </summary>
        public bool Equals(FlexBreakToken? other) =>
            other is not null
            && ReferenceEquals(Box, other.Box)
            && ResumeSlotIndex == other.ResumeSlotIndex
            && UnfinishedItems.SequenceEqual(other.UnfinishedItems)
            && FinishedItems.SequenceEqual(other.FinishedItems);

        public override int GetHashCode() =>
            HashCode.Combine(Box, ResumeSlotIndex, UnfinishedItems.Count, FinishedItems.Count);
    }
}

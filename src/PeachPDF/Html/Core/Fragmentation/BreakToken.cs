using PeachPDF.Html.Core.Dom;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// A resumption record: where layout stopped in one fragmentainer, so the next one can pick up
    /// from exactly that point (<see href="https://www.w3.org/TR/css-break-3/#breaking-controls">CSS
    /// Fragmentation Level 3 §2/§4.4</see>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tokens form a <b>chain</b>, one link per ancestor between the fragmentation-context root and the
    /// box that actually stopped: each link names a box and where inside it to resume, and points at the
    /// deeper link for its own child. The driver hands the chain back to the root, which walks it down,
    /// so every ancestor on the path re-enters mid-flight while boxes off the path are untouched.
    /// </para>
    /// <para>
    /// A token records <i>where</i> to resume, never any geometry: the box tree still holds the
    /// coordinates, and the page grid (<see cref="HtmlContainerInt.PageTopOf"/> and friends) still
    /// defines where each fragmentainer sits.
    /// </para>
    /// </remarks>
    /// <param name="Box">the box this link of the chain resumes into</param>
    /// <param name="ResumeSlotIndex">
    /// the pagination slot to resume in. Derived from where the break actually fell, never from
    /// "the pass after this one": a box can be placed far down the document — past a tall spacer, or by
    /// an engine that positions its own children — so the fragmentainer it overflows is not in general
    /// the one after the fragmentainer the pass nominally started in.
    /// </param>
    internal abstract record BreakToken(CssBox Box, int ResumeSlotIndex)
    {
        /// <summary>
        /// This token's per-child continuations, for a token naming more than one — the
        /// <see href="https://www.w3.org/TR/css-break-3/#parallel-flows">§2.1 parallel-flows</see> shape
        /// <see cref="TableBreakToken"/>/<see cref="FlexBreakToken"/>/<see cref="GridBreakToken"/>/
        /// <see cref="FlexColumnBreakToken"/> all share, each overriding this to expose its own
        /// per-cell/per-item tokens. Empty for every other kind, whose one child (if any) is
        /// <see cref="BlockBreakToken.ChildToken"/> instead — <see cref="FragmentEmitter.RecordChain"/> is
        /// this member's one reader, and walks both shapes the same way.
        /// </summary>
        internal virtual IReadOnlyList<BreakToken> FanOutContinuations => [];
    }

    /// <summary>
    /// A block container stopped part-way through its in-flow children.
    /// </summary>
    /// <param name="Box">the block container to resume</param>
    /// <param name="ResumeSlotIndex">the pagination slot the resumed pass fills</param>
    /// <param name="ResumeChildIndex">the index into <see cref="CssBox.Boxes"/> to resume the child loop at</param>
    /// <param name="ChildToken">
    /// how to resume that child, or null when the child has not been entered at all
    /// (<see cref="IsBreakBefore"/>).
    /// </param>
    /// <param name="IsBreakBefore">
    /// whether the break falls <i>before</i> the child rather than inside it. This is the distinction
    /// §4.4 turns on: a break before a box means the box was never entered, so it has no geometry in the
    /// earlier fragmentainer and therefore produces no fragment there — as opposed to a box that was
    /// partially laid out and continues. A break-before child runs its full prologue on resume; a
    /// partially laid-out one must not.
    /// </param>
    /// <param name="ResumeTopOverride">
    /// the document Y to place a break-before child at, when it is not simply the next fragmentainer's
    /// band top. Set by the margin-truncation and keep-with-next paths, which have already computed an
    /// adjusted target and must not have it re-derived.
    /// </param>
    /// <param name="EscapesNestedFragmentainer">
    /// whether this break is not the enclosing nested fragmentainer's to satisfy — a forced <i>page</i>
    /// break raised while a column was being filled
    /// (<see href="https://www.w3.org/TR/css-break-3/#break-between">§3.1</see>). No column of a container
    /// is on another page, so an engine driving fragmentainers of its own reads this as "stop opening
    /// them" and hands the record up unchanged rather than restating it in its own terms. The record has
    /// to say this because the page vehicle cannot: a forced page break is realized by <i>placement</i>,
    /// and placement cannot escape a container whose engine decides which fragmentainer content lands in.
    /// </param>
    internal sealed record BlockBreakToken(
        CssBox Box,
        int ResumeSlotIndex,
        int ResumeChildIndex,
        BreakToken? ChildToken,
        bool IsBreakBefore,
        double? ResumeTopOverride,
        bool EscapesNestedFragmentainer = false) : BreakToken(Box, ResumeSlotIndex);

    /// <summary>
    /// A block container's inline flow stopped part-way through its content.
    /// </summary>
    /// <remarks>
    /// <see cref="ResumePath"/> is a path rather than a single index because
    /// <c>CssLayoutEngine.FlowBox</c> walks the inline box tree recursively: resuming means descending
    /// the same path again and fast-forwarding to the word that did not fit, rather than replaying the
    /// walk from the top (which is not idempotent — it measures words and mutates the word list for
    /// hyphenation as it goes).
    /// </remarks>
    /// <param name="Box">the block container whose inline flow stopped</param>
    /// <param name="ResumeSlotIndex">the pagination slot the resumed pass fills</param>
    /// <param name="ResumePath">child indices from <paramref name="Box"/> down to the inline box owning the word</param>
    /// <param name="ResumeWordIndex">the index into that box's <see cref="CssBox.Words"/> to resume at</param>
    /// <param name="CompletedLineCount">
    /// how many line boxes the container had already produced when the break was taken. Everything below
    /// this index has been emitted into an earlier fragmentainer and must not be re-aligned, re-bubbled
    /// or re-measured by the resumed pass.
    /// </param>
    /// <param name="LinesKeptHere">
    /// how many line boxes <i>this</i> fragmentainer kept — <see cref="CompletedLineCount"/> minus what the
    /// pass began with. This is the quantity <c>orphans</c> is defined over
    /// (<see href="https://www.w3.org/TR/css-break-3/#widows-orphans">§5.4</see>: line boxes left in a
    /// fragment before the break), which the cumulative count cannot answer for any fragment but the first.
    /// </param>
    /// <param name="FollowsForcedBreak">
    /// <see cref="Dom.CssLineBox.FollowsForcedBreak"/> of the line-in-progress the break discarded (a line
    /// box is monolithic, css-break-3 §4.1, so the whole of it - not just the word that didn't fit - moves
    /// to the next fragmentainer, and <c>CreateLineBoxes</c> rebuilds it there as a fresh seed line). Carried
    /// across the boundary so <c>text-indent: each-line</c> (CSS Text 3 §3) still recognizes a resumed line
    /// that follows a forced break in the source, rather than only a line born mid-fragmentainer.
    /// </param>
    internal sealed record InlineBreakToken(
        CssBox Box,
        int ResumeSlotIndex,
        IReadOnlyList<int> ResumePath,
        int ResumeWordIndex,
        int CompletedLineCount,
        int LinesKeptHere = 0,
        bool FollowsForcedBreak = false) : BreakToken(Box, ResumeSlotIndex)
    {
        /// <summary>
        /// Compared by <b>contents</b>, because the driver's no-progress backstop is an equality test —
        /// the same reason <see cref="TableBreakToken"/> states it, and the same hazard.
        /// </summary>
        /// <remarks>
        /// <c>HtmlContainerInt.LayoutDocument</c> ends a run whose passes keep arriving at a record it has
        /// already been handed; the compiler's own equality for a record compares
        /// <see cref="ResumePath"/> — an <see cref="IReadOnlyList{T}"/> — by <i>reference</i>. Two passes
        /// that stopped at the same word would then compare unequal the moment the path is built rather
        /// than shared, so a flow that made no progress would spin to the pass cap and silently truncate
        /// instead of falling back to §4.3's last-resort relayout. Today's single construction site passes
        /// an empty collection expression, which the compiler serves from a cached singleton, so the
        /// defect is latent rather than live — and that is precisely the kind of accident a value type's
        /// equality must not depend on.
        /// </remarks>
        public bool Equals(InlineBreakToken? other) =>
            other is not null
            && ReferenceEquals(Box, other.Box)
            && ResumeSlotIndex == other.ResumeSlotIndex
            && ResumeWordIndex == other.ResumeWordIndex
            && CompletedLineCount == other.CompletedLineCount
            && LinesKeptHere == other.LinesKeptHere
            && FollowsForcedBreak == other.FollowsForcedBreak
            && ResumePath.SequenceEqual(other.ResumePath);

        public override int GetHashCode() =>
            HashCode.Combine(Box, ResumeSlotIndex, ResumeWordIndex, CompletedLineCount, LinesKeptHere,
                FollowsForcedBreak, ResumePath.Count);
    }
}

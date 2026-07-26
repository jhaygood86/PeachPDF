using PeachPDF.Html.Core.Dom;
using System.Collections.Generic;

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
    internal abstract record BreakToken(CssBox Box, int ResumeSlotIndex);

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
    internal sealed record BlockBreakToken(
        CssBox Box,
        int ResumeSlotIndex,
        int ResumeChildIndex,
        BreakToken? ChildToken,
        bool IsBreakBefore,
        double? ResumeTopOverride) : BreakToken(Box, ResumeSlotIndex);

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
    internal sealed record InlineBreakToken(
        CssBox Box,
        int ResumeSlotIndex,
        IReadOnlyList<int> ResumePath,
        int ResumeWordIndex,
        int CompletedLineCount,
        int LinesKeptHere = 0) : BreakToken(Box, ResumeSlotIndex);
}

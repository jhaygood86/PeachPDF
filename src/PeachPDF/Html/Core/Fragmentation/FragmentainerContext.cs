using PeachPDF.Html.Core.Dom;
using System;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// The fragmentainer one layout pass is targeting — the input half of the fragmentation model, as
    /// against the immutable <see cref="Fragments.BoxFragment"/>s that are its output.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a cursor over the existing page grid, not a new coordinate system.</b> Layout runs in
    /// one continuous document space in which fragmentainer <c>k</c> already occupies
    /// <c>[<see cref="HtmlContainerInt.PageTopOf"/>(k), <see cref="HtmlContainerInt.PageBottomOf"/>(k))</c>;
    /// this type just names which one a pass is filling and collects the resumption record for the next.
    /// </para>
    /// <para>
    /// One instance per pass. The driver
    /// (<c>HtmlContainerInt.LayoutDocument</c>) creates it, runs layout into it, reads
    /// <see cref="OutgoingToken"/>, and — if there is one — opens the next fragmentainer and re-enters.
    /// </para>
    /// </remarks>
    internal sealed class FragmentainerContext
    {
        private bool _fragmenting;

        internal FragmentainerContext(
            HtmlContainerInt container,
            CssBox contextRoot,
            int slotIndex,
            int generation,
            BreakToken? incomingToken)
        {
            ArgumentNullException.ThrowIfNull(container);
            ArgumentNullException.ThrowIfNull(contextRoot);

            Container = container;
            ContextRoot = contextRoot;
            SlotIndex = slotIndex;
            Generation = generation;
            IncomingToken = incomingToken;

            // An unpaginated/measurement pass uses the double.MaxValue page-height sentinel; there is no
            // grid to break against, so nothing may fragment. Same guard every existing page-grid caller
            // already applies before touching PageIndexOf.
            _fragmenting = container.PageSize.Height > 0
                           && container.PageSize.Height < double.MaxValue - 1;
        }

        internal HtmlContainerInt Container { get; }

        /// <summary>
        /// The box that owns this fragmentation context. A field rather than the document root so a
        /// nested context (multi-column columns are fragmentainers too, per §2) can be introduced
        /// without reshaping this type.
        /// </summary>
        internal CssBox ContextRoot { get; }

        /// <summary>The pagination slot this pass is filling.</summary>
        internal int SlotIndex { get; }

        /// <summary>
        /// Which <c>LayoutDocument</c> invocation this context belongs to. A box carries the generation
        /// it last laid out in, so state left over from an earlier pass of the unrestricted-width double
        /// layout or the per-page-width reflow loop is recognised as stale rather than resumed into.
        /// </summary>
        internal int Generation { get; }

        internal double BandTop => Container.PageTopOf(SlotIndex);

        internal double BandBottom => Container.PageBottomOf(SlotIndex);

        internal double BandHeight => Container.PageBandHeightOf(SlotIndex);

        /// <summary>
        /// Where a resumed pass starts flowing: this fragmentainer's own content edge, per
        /// <see href="https://www.w3.org/TR/css-break-3/#fragmentainer">§2</see>.
        /// </summary>
        /// <remarks>
        /// The retired <c>CssRect.BreakPage</c> relocated to <c>NextPageTopOf(Top) + 1</c> instead, so
        /// every continuation line used to sit one layout unit below the content edge. That nudge only
        /// existed to keep a relocated word off the exact boundary value, which is a question the page
        /// grid's own epsilons already answer.
        /// </remarks>
        internal double ResumeContentTop => BandTop;

        /// <summary>How this pass resumes, or null when it starts the document from the top.</summary>
        internal BreakToken? IncomingToken { get; }

        /// <summary>Where this pass stopped, or null when the document finished inside this fragmentainer.</summary>
        internal BreakToken? OutgoingToken { get; private set; }

        /// <summary>
        /// Whether break decisions are live. False for a measurement pass at a provisional position and
        /// inside monolithic content, where a break decision would be made against coordinates the box
        /// does not actually end up at.
        /// </summary>
        internal bool IsFragmenting => _fragmenting && !Container.SuppressWordPageBreaks;

        /// <summary>
        /// Whether this pass placed anything. The driver refuses to open another fragmentainer after a
        /// pass that made no progress, so a box that can neither fit nor advance cannot loop forever.
        /// </summary>
        internal bool MadeProgress { get; private set; }

        /// <summary>
        /// Suppresses breaking for the duration of a monolithic subtree — content the spec does not
        /// allow to be split (<see href="https://www.w3.org/TR/css-break-3/#monolithic">§2</see>), which
        /// here also covers the layout engines that paginate their own content.
        /// </summary>
        internal MonolithicScope PushMonolithic() => new(this);

        internal void RecordBreak(BreakToken token)
        {
            ArgumentNullException.ThrowIfNull(token);
            OutgoingToken = token;
        }

        internal void NoteProgress() => MadeProgress = true;

        /// <summary>
        /// Restores the enclosing fragmenting state on disposal, so nested monolithic subtrees compose.
        /// </summary>
        internal readonly ref struct MonolithicScope
        {
            private readonly FragmentainerContext _context;
            private readonly bool _previous;

            internal MonolithicScope(FragmentainerContext context)
            {
                _context = context;
                _previous = context._fragmenting;
                context._fragmenting = false;
            }

            internal void Dispose() => _context._fragmenting = _previous;
        }
    }
}

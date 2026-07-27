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
        private readonly bool _fragmenting;

        private readonly (double Top, double Bottom)? _ownBand;

        internal FragmentainerContext(
            HtmlContainerInt container,
            CssBox contextRoot,
            int slotIndex,
            (double Top, double Bottom)? ownBand = null,
            bool inheritsSuppression = false,
            bool suppressed = false)
        {
            ArgumentNullException.ThrowIfNull(container);
            ArgumentNullException.ThrowIfNull(contextRoot);

            Container = container;
            ContextRoot = contextRoot;
            SlotIndex = slotIndex;
            _ownBand = ownBand;

            // An unpaginated/measurement pass uses the double.MaxValue page-height sentinel; there is no
            // grid to break against, so nothing may fragment. Same guard every existing page-grid caller
            // already applies before touching PageIndexOf.
            // A nested context inherits whether breaking is live at all. Establishing one inside a
            // suppressed scope - a multi-column container inside a flex, grid or table container, or
            // inside another engine's measurement pass - must not re-enable breaking there: the engine
            // enclosing it would not read the resumption record, so the content that record names is
            // simply dropped. Measured at five items lost from a twelve-item container.
            // `suppressed` is the driver's own last-resort relaxation: a real pass, with a real slot the
            // emitter reads, that must not break again. A *measurement* pass is not this - it has no
            // fragmentainer at all (HtmlContainerInt.DetachFragmentainer).
            _fragmenting = !suppressed
                           && container.HasRealPageGrid
                           && (!inheritsSuppression || container.IsFragmenting);
        }

        internal HtmlContainerInt Container { get; }

        /// <summary>
        /// The box that owns this fragmentation context. A field rather than the document root so a
        /// nested context (multi-column columns are fragmentainers too, per §2) can be introduced
        /// without reshaping this type.
        /// </summary>
        internal CssBox ContextRoot { get; }

        /// <summary>
        /// The pagination slot this pass is filling <i>now</i> — a cursor, not the slot the pass opened
        /// with, because <see cref="StepOverTo"/> moves it on as the pass steps over one.
        /// </summary>
        internal int SlotIndex { get; private set; }

        /// <summary>
        /// Moves the cursor onto <paramref name="slot"/>, which a forced break has just stepped this pass
        /// over to without ending it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>A pass does not always fill one fragmentainer.</b> A forced break is realized by
        /// <i>placement</i> — the box is put at the content top of the slot the break names and the pass
        /// carries on from there — so a pass that opened on slot <c>k</c> can be flowing content into
        /// <c>k + n</c> with no resumption record in between. The emitter has always known this
        /// (<c>FragmentEmitter.EmitPass</c> takes a <c>throughSlot</c> for exactly this reason); without
        /// this the context did not, and every question about "the fragmentainer being filled" — its band,
        /// whether anything precedes a box inside it — answered about a fragmentainer the pass had left.
        /// </para>
        /// <para>
        /// Monotonic, because a pass fills fragmentainers in document order and never goes back to an
        /// earlier one; a pass that has to reconsider an earlier fragmentainer is re-entered from the
        /// driver with a context of its own (<c>HtmlContainerInt.TryRewindForRunPull</c>).
        /// </para>
        /// <para>
        /// A no-op for a nested fragmentainer. A multi-column column's band is not a slot of the page grid
        /// at all — <see cref="SlotIndex"/> there names only the page the column sits on, which a break
        /// stepping over pages inside the column would have to escape the column to reach
        /// (<c>CssBox.PlaceBlockChild</c>'s escaping-break arm).
        /// </para>
        /// </remarks>
        internal void StepOverTo(int slot)
        {
            if (_ownBand is not null || slot <= SlotIndex) return;

            SlotIndex = slot;
        }

        /// <summary>
        /// Whether this context names a fragmentainer of its own rather than a page — a multi-column
        /// column, per <see href="https://www.w3.org/TR/css-break-3/#fragmentainer">§2</see>'s "a column
        /// in multi-column layout, or a page in paged media".
        /// </summary>
        /// <remarks>
        /// A column band is a <i>sub-band</i> of the page band it lives in, so the page grid can no
        /// longer answer where this fragmentainer ends — which is the one thing a break decision needs
        /// to know. Everything else about the page grid stays true and stays in use: the column is still
        /// placed in one continuous document space, and <see cref="SlotIndex"/> still names the page it
        /// sits on, so a break that escapes the column resumes against the ordinary grid.
        /// </remarks>
        internal bool HasOwnBand => _ownBand is not null;

        /// <summary>
        /// This fragmentainer's block-axis extent, in the one continuous document space layout runs in —
        /// the value form of <see cref="BandTop"/>/<see cref="BandBottom"/>, so
        /// <see cref="HtmlContainerInt.FallsPast"/> can be asked of it directly.
        /// </summary>
        internal PageBand Band => new(BandTop, BandBottom);

        internal double BandTop => _ownBand?.Top ?? Container.PageTopOf(SlotIndex);

        internal double BandBottom => _ownBand?.Bottom ?? Container.PageBottomOf(SlotIndex);

        internal double BandHeight => _ownBand is { } band
            ? band.Bottom - band.Top
            : Container.PageBandHeightOf(SlotIndex);

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

        /// <summary>Where this pass stopped, or null when the document finished inside this fragmentainer.</summary>
        internal BreakToken? OutgoingToken { get; private set; }

        /// <summary>
        /// Whether a break may be taken here — that is, whether layout may stop and record a
        /// <see cref="BreakToken"/> rather than placing content in this fragmentainer regardless. False
        /// for an unpaginated pass, inside monolithic content, and during a measurement pass at a
        /// provisional position, where a break decision would be made against coordinates the box does
        /// not end up at.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is deliberately <b>not</b> the gate on the legacy per-word relocation in
        /// <c>CssLayoutEngine.FlowBox</c>, which reads
        /// <see cref="HtmlContainerInt.SuppressWordPageBreaks"/> directly. The two answer different
        /// questions: a multi-column container is monolithic to the driver (its engine fragments its own
        /// children) while its content still paginates word by word. Folding the word path into this one
        /// is what makes inline flow genuinely resumable, and is a separate step.
        /// </para>
        /// <para>
        /// This property used to <i>read</i> that flag as well, which welded the two questions together at
        /// the definition site: breaking could never be enabled for a flex or grid item without also
        /// enabling the legacy word relocation, so neither engine could be made fragmentable a step at a
        /// time. The flex and grid measurement re-layouts now enter a monolithic scope of their own
        /// alongside setting the flag, which is where that suppression belongs and which leaves this
        /// answering only its own question.
        /// </para>
        /// </remarks>
        internal bool IsFragmenting => _fragmenting;

        internal void RecordBreak(BreakToken token)
        {
            ArgumentNullException.ThrowIfNull(token);
            OutgoingToken = token;
        }
    }
}

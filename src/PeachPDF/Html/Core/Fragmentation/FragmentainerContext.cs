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
        /// Moves the cursor onto <paramref name="slot"/>, which a forced break — or one of the several
        /// other things that can put content past the fragmentainer being filled — has just stepped this
        /// pass over to without ending it.
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
        /// <b>Not only a forced break.</b> An unforced §5.2 flush placement, a word or block child too tall
        /// for any fragmentainer, a table row-loop band jump, and a flex/grid line relocation can each put
        /// flow past the band being filled with no break recorded for the crossing either — every one of
        /// them calls this too, for the same reason: whatever this pass places next has to see a truthful
        /// answer to "which fragmentainer am I filling", not a stale one
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/435">#435</see>). Whichever call
        /// stepped it, this is the one thing every one of the two §6.2 reservations below
        /// (<see cref="ResumeContentInset"/>, <see cref="BandEndInsetOf"/>) needs to stay true: a
        /// reservation is keyed to the band it was made in, and a step means this pass has moved on from
        /// that band by placement rather than by a break record, so nothing drawn into or reserved for the
        /// bands it left still applies here.
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
        /// The room a repeated group has claimed at the top of one fragmentainer, and <b>which</b>
        /// fragmentainer it claimed it in — see <see cref="ReserveResumeContent"/> for why the slot is
        /// part of the answer.
        /// </summary>
        private (int Slot, double Amount)? _resumeReservation;

        /// <summary>
        /// How far below <see cref="BandTop"/> a resumed flow in the subtree currently being laid out
        /// starts, because something this fragmentainer repeats has already claimed that much of it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Zero for everything except a table repeating a <c>&lt;thead&gt;</c>. A repeated header is laid
        /// out at the content edge of every fragmentainer the table covers, and
        /// <see href="https://www.w3.org/TR/css-tables-3/#repeated-headers">css-tables-3 §6.2</see>
        /// requires room to be left for it. The row cursor reserves that room by advancing, which places
        /// the rows this pass lays out <i>and</i> the cells it enters fresh — but not a cell whose own flow
        /// continues from an earlier fragmentainer. That flow asks this type where its fragmentainer's
        /// content begins, and the answer for it is below the header rather than at the band's edge;
        /// without this the header is drawn over the first lines of the continuation it is supposed to sit
        /// above.
        /// </para>
        /// <para>
        /// Zero again once the pass has left the fragmentainer the reservation was made in.
        /// </para>
        /// </remarks>
        internal double ResumeContentInset =>
            _resumeReservation is { } reservation && reservation.Slot == SlotIndex ? reservation.Amount : 0;

        /// <summary>
        /// Records that <paramref name="amount"/> at the top of the fragmentainer being filled is spoken
        /// for, and returns the reservation that was in force so the caller can put it back.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A pair rather than a scope for the same reason
        /// <see cref="HtmlContainerInt.DetachFragmentainer"/> is one: every call site is an <c>async</c>
        /// method. Restore in a <c>finally</c>.
        /// </para>
        /// <para>
        /// <b>The slot is part of the reservation, not incidental to it.</b> <see cref="SlotIndex"/> is a
        /// cursor — <see cref="StepOverTo"/> moves it on when a forced break is realized by placement — so
        /// a reservation made for the fragmentainer a repeated header was drawn in must stop applying once
        /// the pass has stepped past it. Without the slot, a forced page break inside a resumed cell left
        /// the header's room reserved on the page the break opened, where no header is drawn: measured as
        /// a 13pt blank strip at the top of that page.
        /// </para>
        /// <para>
        /// The amount <b>composes</b> with any reservation still in force for this same fragmentainer, and
        /// starts from zero against one made for another.
        /// </para>
        /// </remarks>
        /// <param name="amount">how much room the caller has just taken at this fragmentainer's content edge</param>
        internal (int Slot, double Amount)? ReserveResumeContent(double amount)
        {
            var previous = _resumeReservation;

            _resumeReservation = (SlotIndex, ResumeContentInset + amount);

            return previous;
        }

        /// <summary>Puts back the reservation <see cref="ReserveResumeContent"/> returned.</summary>
        /// <param name="reservation">what that call handed back</param>
        internal void RestoreResumeContent((int Slot, double Amount)? reservation) =>
            _resumeReservation = reservation;

        /// <summary>
        /// The room a repeated group has claimed at the <b>end</b> of every fragmentainer from
        /// <c>FromSlot</c> on, which fragmentainer that is, and which one the pass was filling when the
        /// claim was made — see <see cref="ReserveBandEnd"/> for why one of those slots is a floor and the
        /// other a ceiling.
        /// </summary>
        private (int FromSlot, int MadeAtSlot, double Amount)? _bandEndReservation;

        /// <summary>
        /// How far above fragmentainer <paramref name="slot"/>'s band bottom content in the subtree being
        /// laid out has to stop, because something drawn at that fragmentainer's foot has already claimed
        /// the space.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Zero for everything except a table repeating a <c>&lt;tfoot&gt;</c>. That footer is drawn at the
        /// foot of every fragmentainer the table covers, and
        /// <see href="https://www.w3.org/TR/css-tables-3/#repeated-headers">css-tables-3 §6.2</see>'s
        /// "the same applies for footer rows and the table bottom border" requires room to be left for it.
        /// The row cursor leaves that room for whole rows (<c>CssLayoutEngineTable</c>'s
        /// <c>availableHeight</c> subtracts the footer's height), and reaches nothing inside a cell whose
        /// own flow runs down toward the band bottom; without this the footer is drawn over the last lines
        /// of the continuation it is supposed to close.
        /// </para>
        /// <para>
        /// The mirror of <see cref="ResumeContentInset"/>, at the other end of the band — and deliberately
        /// not its exact reflection, see <see cref="ReserveBandEnd"/>.
        /// </para>
        /// </remarks>
        /// <param name="slot">the fragmentainer being asked about, which the caller already knows</param>
        internal double BandEndInsetOf(int slot) =>
            _bandEndReservation is { } reservation
            && slot >= reservation.FromSlot
            && SlotIndex <= reservation.MadeAtSlot
                ? reservation.Amount
                : 0;

        /// <summary>
        /// Records that <paramref name="amount"/> at the end of fragmentainer <paramref name="fromSlot"/>
        /// and of every fragmentainer after it is spoken for, and returns the reservation that was in force
        /// so the caller can put it back.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A pair rather than a scope for the same reason <see cref="ReserveResumeContent"/> is one: every
        /// call site is an <c>async</c> method. Restore in a <c>finally</c>.
        /// </para>
        /// <para>
        /// <b>Three things differ from <see cref="ReserveResumeContent"/>, and a reader who assumes
        /// symmetry will be wrong about each.</b>
        /// </para>
        /// <para>
        /// <b>The slot is a parameter rather than <see cref="SlotIndex"/>.</b> That property is a cursor
        /// <see cref="StepOverTo"/> may already have moved past the fragmentainer the caller is filling —
        /// a table's row loop tracks its own band (<c>TableRowCursor.BandReached</c>) and knows which one
        /// it is placing a row in, so it says so rather than being guessed at.
        /// </para>
        /// <para>
        /// <b>The claim runs forward from that slot, where the header's applies to its own slot alone.</b>
        /// A repeated <i>header</i> is drawn by the code that reserves for it, into exactly one
        /// fragmentainer. A repeated <i>footer</i> is drawn at the foot of every fragmentainer a pass
        /// <i>fills or leaves</i>, so a claim made in band <c>k</c> is equally true of <c>k+1</c> — and it
        /// has to be, because inline flow reaches the next band without recording a break at all, under
        /// the boundary tolerance <c>CssRect.WouldStraddleFragmentainer</c> relies on. Stopping at <c>k</c>
        /// would drop the reservation exactly where it is needed.
        /// </para>
        /// <para>
        /// <b>It still stops once the pass has stepped over the fragmentainer it was made in</b>, which is
        /// the rule <see cref="ReserveResumeContent"/> states and the one that is <i>not</i> an asymmetry.
        /// <see cref="StepOverTo"/> means a forced break was realized by placement rather than by ending
        /// the pass, and no repeated footer is drawn on the band such a break opens — the per-row block
        /// only writes one at a break between two rows, and the closing footer belongs under the table's
        /// last row. Room held there is room held for a footer that is not drawn: measured on a
        /// <c>&lt;tfoot&gt;</c> table whose cell carries a <c>break-before: page</c> as one page of seven
        /// with no footer on it whose content still stopped level with the six that had one.
        /// </para>
        /// <para>
        /// <b>Composition keys to the new <paramref name="fromSlot"/>.</b> An inner table reserving in a
        /// later band composes with an outer one still in force, and restores to the outer's own key. The
        /// bands below that inner <c>fromSlot</c> read zero while it is in force, which no call site can
        /// reach today — a nested table's row band is never above its enclosing row's — and which is
        /// deliberately left unstated rather than modelled as a stack.
        /// </para>
        /// </remarks>
        /// <param name="fromSlot">the first fragmentainer this claim applies to</param>
        /// <param name="amount">how much room the caller has taken at that fragmentainer's foot</param>
        internal (int FromSlot, int MadeAtSlot, double Amount)? ReserveBandEnd(int fromSlot, double amount)
        {
            var previous = _bandEndReservation;

            _bandEndReservation = (fromSlot, SlotIndex, BandEndInsetOf(fromSlot) + amount);

            return previous;
        }

        /// <summary>Puts back the reservation <see cref="ReserveBandEnd"/> returned.</summary>
        /// <param name="reservation">what that call handed back</param>
        internal void RestoreBandEnd((int FromSlot, int MadeAtSlot, double Amount)? reservation) =>
            _bandEndReservation = reservation;

        /// <summary>
        /// Where a resumed pass starts flowing: this fragmentainer's own content edge, per
        /// <see href="https://www.w3.org/TR/css-break-3/#fragmentainer">§2</see>, below anything
        /// <see cref="ResumeContentInset"/> says this fragmentainer repeats above it.
        /// </summary>
        /// <remarks>
        /// The retired <c>CssRect.BreakPage</c> relocated to <c>NextPageTopOf(Top) + 1</c> instead, so
        /// every continuation line used to sit one layout unit below the content edge. That nudge only
        /// existed to keep a relocated word off the exact boundary value, which is a question the page
        /// grid's own epsilons already answer.
        /// </remarks>
        internal double ResumeContentTop => BandTop + ResumeContentInset;

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

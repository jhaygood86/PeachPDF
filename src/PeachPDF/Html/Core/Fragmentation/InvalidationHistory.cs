using System.Collections.Generic;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// Every fragmentainer-reopening event <see cref="FragmentEmitter.InvalidateFrom"/> has recorded so
    /// far, so a box's "produced nothing" observation can be checked against only the reopenings that
    /// could actually have affected it, rather than every reopening retiring every observation in the
    /// document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A single counter bumped on every reopening (this type's predecessor) is sound but not scoped: it
    /// answers "has anything at all been reopened since this observation was made", when the question
    /// that matters is "has anything at <i>or after</i> the slot this observation is about been
    /// reopened". On a document whose <c>widows</c>/<c>orphans</c> rewind fires often — short entries in
    /// <c>columns: 2</c>, as in the css4.pub Icelandic Dictionary reference document — reopenings are
    /// frequent enough (roughly one per page) that the single counter retires essentially every
    /// observation in the whole ~255,000-box tree before any of them are ever read, which is what made
    /// fragment-emission pruning (added for issue #581 / PR #584) fire on well under 1% of slots despite
    /// being otherwise correct.
    /// </para>
    /// <para>
    /// <b>Recording is append-only and reopening slots only ever grow forward</b> (a reopening's own
    /// <c>fromSlot</c> is bounded by the highest slot emitted so far,
    /// <see cref="FragmentEmitter.InvalidateFrom"/>'s own early return), so a suffix-minimum over the
    /// sequence of <c>fromSlot</c> values answers exactly the scoped question: an observation made after
    /// N reopenings, naming a given slot, remains trustworthy iff no reopening since then started at or
    /// before that slot — see <see cref="StillSafe"/>.
    /// </para>
    /// </remarks>
    internal sealed class InvalidationHistory
    {
        private readonly List<int> _fromSlots = [];

        /// <summary>
        /// <c>_suffixMin[i]</c> is the minimum <c>fromSlot</c> among reopenings <c>i, i+1, ..., Count-1</c>
        /// — maintained incrementally as reopenings are recorded rather than recomputed per query, since
        /// a query happens for every box <see cref="FragmentEmitter.BuildDraft"/> considers skipping.
        /// </summary>
        private readonly List<int> _suffixMin = [];

        /// <summary>How many reopening events have been recorded so far this layout.</summary>
        internal int Count => _fromSlots.Count;

        /// <summary>
        /// Records that layout is about to redo everything from <paramref name="fromSlot"/> on.
        /// </summary>
        /// <remarks>
        /// The backward walk is a standard incremental-suffix-minimum maintenance: each earlier slot is
        /// only ever overwritten while the newly-appended value is smaller than what it already holds,
        /// and once an earlier slot's minimum is already that small or smaller, everything before it is
        /// too (a suffix's minimum can only rise as the suffix shrinks) — so the walk stops there rather
        /// than reaching the start on every call.
        /// </remarks>
        internal void Record(int fromSlot)
        {
            _fromSlots.Add(fromSlot);
            _suffixMin.Add(fromSlot);

            for (var i = _suffixMin.Count - 2; i >= 0; i--)
            {
                if (_suffixMin[i] <= fromSlot) break;

                _suffixMin[i] = fromSlot;
            }
        }

        /// <summary>
        /// Whether an observation recorded after <paramref name="recordedAt"/> reopenings, stating a box
        /// produced nothing from <paramref name="recordedSlot"/> on, is still trustworthy now.
        /// </summary>
        internal bool StillSafe(int recordedAt, int recordedSlot) =>
            recordedAt >= Count || recordedSlot < _suffixMin[recordedAt];
    }
}

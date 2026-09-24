# The column-scoped footnote loop terminates by construction once a state repeats (#1270)

**The issue called it a rare two-cycle. Measured, it is the ordinary case for dense column notes.** A fixed-height,
`column-fill: auto` two-column container with a `float-reference: column` footnote on every third line stopped on
`maxFootnotePasses` (six) for **30 of 30** swept layouts; the same document with page-scoped notes settled every
time, and one call every eighth line settled every time. So the loop's cap was not a backstop for a corner case, it
was how those documents ended, in whichever state parity left them.

**Mechanism:** reserving room at the foot of column 1 shortens it, which can push the paragraph holding the call into
column 2; the reservation moves there and column 1 is long again, and the paragraph is pulled back. Tracing the
resolved state per pass on the `FootnoteColumnScopeIntegrationTests` document showed a real cycle of three states,
not a drift.

**The guard** (`HtmlContainerInt.GuardFootnoteConvergence`, called from `ResolveFootnotesForThisAttempt`): keep the
states the run visits; the first time one repeats that is *not the previous pass's* (that would be a settled loop),
latch a floor per column key at the largest value that column had anywhere in the cycle, and from then on hold each
column's reservation at `max(actual, floor)`, raising floors every pass. The floors only grow and take values from
a finite set, so the loop ends without reasoning about the column balancer. Nothing changes before the first repeated
state, so a document that settles on its own is laid out exactly as before, and the six-pass cap stays as a backstop.
Measured after: the dense sweep settles in at most four passes.

**Two alternatives tried and rejected, both by running the existing column-scope tests:**
- *Union every key over the cycle* (slot-level too): a cycle can include a state with a **page-level** area for a
  column note, because the note is resolved against the page before `ColumnFragmentainers` exist to route it.
  Holding that reservation leaves `FootnoteAreaHeightsBySlot` non-empty for a document whose notes are all column-
  scoped. Only column reservations take part in the feedback edge, so only they are held.
- *Pin the single largest state of the cycle*: no state in a cycle is a fixed point, so the layout produced with the
  pinned seeds resolved to a different set of areas (one column area where the test document has two).

**Cost, not hidden:** a column that lost its call keeps a blank reserved strip at its foot. No note area overlaps
content and no area is attached to a page its call is not on (`bodies == calls` is asserted across the sweep); what is
not asserted is that the strip is the *smallest* that ends the cycle.

`HtmlContainerInt.FootnoteResolvePasses`/`FootnoteLoopSettled` are internal diagnostics added to measure this; the
tests read them.

Evidence: `FootnoteColumnConvergenceIntegrationTests` (dense sweep settles for every height and note length, page-scoped
unchanged, sparse notes settle in fewer than six passes); the whole net8.0 suite green.

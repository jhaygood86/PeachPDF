# A stamp read across layout invocations needs a generation check *and* a scoped-invalidation check — neither alone is enough

_CSS Fragmentation Level 3. Tracker: [#384](https://github.com/jhaygood86/PeachPDF/issues/384)._

A `CssBox` field that stamps "which pass/slot/attempt placed me, as of writing" (`_placedByPass`,
`_emittedNothingAtSlot`, and any future sibling) is read by code that may run long after the stamp was
taken — a different pass discovering it needs to relocate a box the stamp names, a later layout
invocation re-deciding the same box. Two independent things can make the stamp stale, and one check does
not cover the other:

- **A whole new layout invocation** (`HtmlContainerInt.LayoutDocument` running again — `ShrinkToFit`, the
  per-page reflow loop) rebuilds the structure the stamp indexes into (`_passEntries`, the fragment tree)
  from nothing. An index recorded against the old structure can land back in-range in the new one by pure
  coincidence and silently name the wrong thing. Guard: compare `HtmlContainer.LayoutGeneration` at
  stamp time against its current value (`_emittedNothingGeneration`, `_placedByPassGeneration`).
- **A retraction within the same invocation** (`TruncatePassEntries`, `FragmentEmitter.InvalidateFrom`)
  discards a suffix of the structure and lets it grow back differently. Guard: an
  `Fragmentation.InvalidationHistory`, scoped by the same index/slot the stamp names, answers "was
  anything at or after my own index invalidated since I was stamped" — see its own doc comment for why a
  single unscoped counter over-invalidates (measured on the Icelandic Dictionary reference document).

Missing the generation check: a stamp from a finished invocation reads as valid in a fresh one whose
`InvalidationHistory` has just been cleared to empty, because `StillSafe`'s own `recordedAt >= Count`
early return treats "nothing has been recorded yet against the new, empty history" as trivially safe —
exactly backwards for a value that was never recorded against *this* history at all.

Missing the scoped-invalidation check (using a bare bump instead): correct within one invocation, but an
invalidation event anywhere in the document retires every stamp in the document, degrading every rewind
decision but the one that just fired back to whatever fallback exists for "no valid stamp" — silently
reintroducing the exact defect the stamp was added to fix, for every box the fix wasn't specifically
being tested against.

Add both, mirroring `EmittedNothingAtOrBefore`'s existing three-part check
(`_emittedNothingGeneration == LayoutGeneration && _emittedNothingAtSlot >= 0 && history.StillSafe(...)`)
rather than inventing a new shape.

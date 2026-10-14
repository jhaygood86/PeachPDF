# A keep-with-next heading in a multi-column container was claimed by two fragmentainers (#1719)

**Cause (found by tracing `Location` writes, not by reading).** When a paragraph chained to a `break-after: avoid`
heading was pushed past a column's foot, `CssBox.ResolveBlockChildOffset` ran its *page-grid* keep-with-next pull: it
translated the heading (`OffsetTop`) to the next slot's top. In a column that is wrong twice over - every column of a
container shares one band, so there is no lower coordinate - and the column loop then broke before the *paragraph*, so
the heading stayed in the column's recorded capture (held at the stale spot) while its live geometry now sat at the next
page's top, where the emitter's live read claimed it again.

**Fix.** The translation is skipped when the current fragmentainer is a column (`HasOwnBand`), and
`ColumnBreakFallsBefore` states the break before the run head instead. That function's fit test read the asking
child's `Location`/`ActualBottom`, which for a child that asked for a break before it was placed are leftovers of an
earlier attempt (here 800pt below the heading), so it never accepted the run; a child with `RequestedBreakBeforeTop`
now skips the fit test and takes the earliest run member after the column's start (`RunHeadAfter`) - the next column
is empty at the same band top. A second, symmetrical branch in the `RequestedBreakBeforeTop` handler was tried and
never reached by the suite, so it was not kept.

**Not done.** A heading at the foot of a page's *last* column whose paragraph starts the next page is still left
there (keep-with-next is not applied when the carry leaves the container); it is claimed once, so no duplication.
Tracked in #1744.

**Evidence.** `EveryWordOfALongTwoColumnDocument_IsClaimedByAFragment` now asserts every word is claimed exactly once;
full net8.0 suite 17164 passed.

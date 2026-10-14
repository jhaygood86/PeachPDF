# A keep-with-next heading at the foot of a multi-column page's last column now travels with its paragraph (#1744)

**Cause (found by tracing which `PendingBreakToken` site fired, not by reading).** The paragraph chained to the
`break-after: avoid` heading kept fewer lines than `orphans` in the last column, so the orphans arm of
`CssBox.FillFragmentainerWithBlockChildren` raised a break before the *paragraph*. That arm's keep-with-next pull
(`EarlyBreak.Discover` + `TryRestartAt`) is page-grid only (`HasOwnBand: false`), so in a column the run was never
given - and the heading stayed in the column while the paragraph moved on. The column-to-column case worked only
because the other column arms go through `ColumnBreakFallsBefore`.

**Fix.** In a column, the orphans arm states the break before the earliest run member after the column's start
(`RunHeadAfter`) with no target; the columns engine restates it for the next column
(`ResumeInTheNextColumn`) or the next page (`RetargetToTheNextPage`). No fit test: the child kept too few lines to
measure, and the destination begins empty (same reasoning as the `RequestedBreakBeforeTop` branch of
`ColumnBreakFallsBefore`).

**Trap.** A `CssBox.Location` of a paragraph split across columns is its *last* fragment's, so a test asserting
"heading precedes paragraph" by `Location.Y` fails after a correct fix; assert on the heading alone.

**Not done.** The propagating-break-before arm (just above it) has the same page-grid-only gate, but no fixture
reached it, so it was not changed.

**Evidence.** `AKeepWithNextHeading_AtTheFootOfTheLastColumn_TravelsWithItsParagraphToTheNextPage` (with a
no-chain control); Multicol filter 219 passed.

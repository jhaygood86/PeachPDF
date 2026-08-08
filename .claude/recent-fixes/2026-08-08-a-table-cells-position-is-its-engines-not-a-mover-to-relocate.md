# A table cell's position is its engine's, not a mover's to relocate

_Landed 2026-08-08._

**A `break-before: page` on a block inside a `<td>` was silently dropped, or walked one page
further than it asked for, once its row straddled a band boundary**
([issue #512](https://github.com/jhaygood86/PeachPDF/issues/512)). Two independent defects
compounded into the numbers the issue reported (an 8-row, 50pt-row table on a 260pt band: row 4's
cell measured `rowTop=166.0 maxBottom=310.0`, 144pt for 40pt of real content).

**Root cause A, the deeper one.** `CssLayoutEngineTable` lays out every cell via
`await cell.PerformLayout(g)` without ever setting `CssBox.PositionAssignedByEngine = true` — unlike
the flex/grid engines, which already do this for their own items
(`Fragmentation/ItemContentCommit.cs`'s `CommitLayout`). Every `<td>`/`<th>` gets `overflow: hidden`
from the UA stylesheet (`CssDefaults.cs`), which makes `MonolithicContent.IsScrollContainer` — and so
`IsMonolithic` — true for essentially every cell. `CssBox.PerformLayoutEpilogue`'s §4.3 "monolithic"
mover, built for ordinary block-flow content that can be moved by re-deriving its own position, then
decided a straddling cell should be laid out again — but `CssBox.ResolveBlockChildOffset` already
special-cases `ActualDisplay == TableCell` to never reposition one (the table engine owns that), so
the retry cannot honour the relocation it decided; it can only repeat the cell's own content layout at
the same top. On that second, silent pass, the forced-break child's one-shot retake latch
(`CssBox.PlacedByForcedBreak`) was already spent by the first pass, so the break did not fire again
and the child fell back to flowing normally. Fixed by wrapping the cell's layout call with
`cell.PositionAssignedByEngine = true` / `finally { cell.PositionAssignedByEngine = false; }`,
mirroring `ItemContentCommit.CommitLayout`'s own pattern — the same #166 engine-independence boundary
`BreakPropagation.CanTravelOutOf` already draws for a break value travelling *out* of a cell, now also
drawn for the epilogue movers that would otherwise try to move the cell itself.

**Root cause B, layered on top.** Even with A fixed, a cell's forced break lands its content at the
real destination band's top the first time — the row is laid out at its true row-top coordinate, not
a provisional one a later translation corrects, so nothing about that placement is wrong. But
`CssLayoutEngineTable`'s straddle correction (`StraddleCorrectionAppliesTo`) read the resulting
`cursor.MaxBottom - rowTop` as ordinary row content that overflowed its band, and retracted and
re-placed the whole row on the next one. The retraction's `PassRewind.RollBackTo(null, row.Boxes)`
resets every descendant's forced-break retake latch, so the same break fires *again* relative to the
row's new position and walks the content one fragmentainer further than the value asked for. Fixed by
a fifth decline on `StraddleCorrectionAppliesTo`: `RowHoldsAnInternalForcedBreak` walks the row's
cells for any descendant with `PlacedByForcedBreak == true` and, if found, leaves the row's
already-correct geometry alone rather than retrying it.

**Both were measured independently load-bearing** by reverting each in isolation against the new
test: reverting A alone dropped the break (content landed on the first page instead of the second);
reverting B alone walked it one page too far (third page instead of second).

Tests: `TableRowCursorBandTests.ARowsInternalForcedBreak_IsNotWalkedFurtherByTheStraddleCorrection`.
Full `net8.0` suite: 8316 passing, 9 skipped (pre-existing, platform-specific), 0 failed. 100% diff
coverage on the changed lines. `dotnet build PeachPDF.slnx -t:Rebuild`: zero warnings.

**Not investigated further:** whether `PositionAssignedByEngine = true` on every cell (not only ones
with an internal forced break) also changes `break-inside: avoid`/widows-orphans behaviour for a
`<td>` that carries those properties directly rather than relying on the row loop's own stopping
mechanism. This mirrors flex/grid's already-shipped treatment of their own items and is believed
correct (a cell's position is never block flow's to begin with, so these movers' whole premise — "lay
this box out again somewhere else" — never applied to one), but no fixture isolates that scenario
specifically.

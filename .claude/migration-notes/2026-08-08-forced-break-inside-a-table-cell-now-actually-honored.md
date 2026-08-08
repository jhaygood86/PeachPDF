# A forced break inside a table cell is now actually honored

**Landed:** 2026-08-08 — A table cell's position is its engine's, not a mover's to relocate (issue #512)
**Doc section:** docs/html-css-support.md § [Fragmentation](../../docs/html-css-support.md) ("a break value on a box *inside* a cell is likewise the table's row grid to answer") — the doc text already described the intended behavior; the layout code did not match it.
**Verified against v0.9.8:** `git show v0.9.8:src/PeachPDF/Html/Core/Dom/CssLayoutEngineTable.cs` has neither `PositionAssignedByEngine` nor `RowHoldsAnInternalForcedBreak` — the defect predates the tag.

A `break-before`/`break-after: page` (or a directional value) on a block inside a `<td>`/`<th>` could
silently fail to move that content to the next page at all once the row it was in crossed a page-band
boundary — the content simply flowed on as if the break value were not there. Depending on exact
geometry, the row could instead land the broken content one page further than the break value asked
for. Both were caused by the table engine re-running a straddling cell's own layout a second time
internally (see the recent-fixes entry for the full mechanism), which silently spent the break's
one-shot retake latch. A document relying on a forced break inside a table cell to open a new page at
a specific point should now see it land exactly where the break value names, with no missing or extra
page.

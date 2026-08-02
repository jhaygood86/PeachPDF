# A spanning cell's box is closed per band, not stretched across them

_CSS Fragmentation Level 3 / css-tables-3 §6.1. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

A `rowspan` cell belongs to the row that **opens** the span, but its bottom is written by the row that
**ends** it — `CssLayoutEngineTable.LayoutBodyRow`, reaching it either as a `CssSpacingBox`'s
`ExtendedBox` or through `TableRowCursor.RowSpannedBoxes[rowIndex]`. Where those two rows are in
different bands, giving the cell the ending row's bottom makes one box that spans a page boundary, and
its borders and background are then drawn straight through the page edge.

[css-tables-3 §6.1](https://www.w3.org/TR/css-tables-3/#breaking-rules) names both halves, and it is
worth quoting because the terms are the spec's own: a row is preserved unfragmented only *"if the cells
spanning the row do not span any subsequent row"* — so the row that **ends** a span is moved whole, while
a row in the **middle** of one is *freely fragmentable* and *"user agents must attribute all the remaining
height in the fragmentainer to the cells of that row"*. Either way the cell continues rather than
travelling, and *"top borders must not be repainted in continuation fragments"* — which is what
`TheSpanningCellsFragments_OwnOnlyTheBlockEdgesTheBreakDidNotMake` pins.

**`CloseSpanningCell` is the one place that may write a spanning cell's bottom, and it asks the bands,
not the caller.** Three different mechanisms put a break between two rows a span covers — the straddle
correction moving the ending row, the row loop's `EstimateRowHeight` prediction breaking before a row in
the *middle* of a span, and a forced `break-before` declared on one — and all three were measured
producing the stretched box. A fix keyed to any one of them fixes a third of the defect. Ask
`SlotStartingAt(cell.Location.Y)` against the row's own band.

Three rules the close depends on, each of which cost something to find:

- **It closes at the table's own slice bottom for that band (`PageBreakBottoms[cellSlot]`), not at the
  band's foot.** `FragmentPainter` clips the table's bottom border to the same record, so a cell closed
  lower is a tint drawn past the table's own edge — visible in the first rasterization of
  `paged_media_table_rowspan_break` as a strip hanging below the last row on the page.
- **The gate is `CurrentFragmentainer is { HasOwnBand: false }`, not `is not { HasOwnBand: true }`.**
  The two differ on **null**, which is a measurement pass — a flex or grid item's layout runs behind a
  detached fragmentainer at a provisional position it is about to be translated away from, with the
  emitter still live. The `is not` form treats that as "the page grid answers", and a close decided there
  states continuation geometry at coordinates nothing ends up at and no later run sweeps.
- **The table's slice on the band follows the cell where the cell reaches lowest.** `MaxBottom` never
  counts a spanning cell before the row that ends it, so a tall cell opened by a *short* row closes below
  the `PageBreakBottoms` entry its own table wrote, and the bottom border clipped to that entry would be
  drawn across the cell. Raise the record rather than closing the cell at it — see the next rule for why
  the close cannot give way.
- **It may never close above the cell's own content, but closing *at* a band the content already
  occupies is safe — `FragmentEmitter.ShellIn` is consulted only for a band its real per-pass walk found
  nothing in at all.** A rowspan cell whose own content needs more than one band stops and resumes
  exactly like any other box, through the table's ordinary per-cell continuation
  (`TableRowCursor.UnfinishedCells`/`Continuation`), so its real fragments already exist in every band it
  actually occupies by the time `CloseSpanningCell` runs for its ending row — stating a continuation
  shell over one of those bands too is discarded outright rather than merely redundant, never a risk of
  displacing the real content ([issue #521](https://github.com/jhaygood86/PeachPDF/issues/521)).
- **Where the content reaches into the very band the row ending the span lands in, there is no later,
  empty band left to state a shell over, and the close cannot use the `cellSlot`-band arithmetic above at
  all.** `CloseSpanningCell` asks `SlotEndingAt(contentBottom)` against `slot` for exactly this: when the
  content's own band is `>= slot`, the close is `Math.Max(rowMaxBottom, contentBottom)`, not
  `Math.Max(PageBottomOf(cellSlot), ...)` — closing at `cellSlot`'s own band-foot in this shape closed the
  box *above* the row's remaining span (measured: the rowspan's other rows in that same band lost their
  tint and border entirely, rendering `paged_media_table_rowspan_break`'s own Q4 fixture). `PageBreakBottoms[slot]`
  has to be created here, not merely raised like the `cellSlot` entry below — nothing else has necessarily
  written a slice-bottom for the band the row loop is still filling, unlike a band it has already broken
  away from.
- **Each cell is closed exactly once per row.** `boxesToVerticallyAlign` is
  `row.Boxes ∪ boxesThatEndOnRow`, so a spanning cell arrives twice, and
  `CssLayoutEngine.ApplyCellVerticalAlignment` **offsets a subtree rather than assigning a position** —
  applying it twice under a `<td>`'s used `vertical-align: middle` adds a further quarter of the leftover
  room every time (measured: content 24.75pt low on a 99pt leftover).

**Anything a row writes to a cell it does not own must be recorded** (`TableRowCursor.RecordForeignWrite`),
because `Retract` restores this cursor's totals and `PassRewind.RollBackTo(null, row.Boxes)` resets the
row's own boxes, and the spanning cell is neither. Without the record the straddle correction cannot move
such a row at all, which is exactly why it used to decline to
([#511](https://github.com/jhaygood86/PeachPDF/issues/511)).

**Do not use `CssSpacingBox` presence to decide whether a span crosses a row.** `InsertEmptyBoxes` walks
only the later row's *existing* cells, so a spacer that belongs after the last one is never inserted:
measured, a `rowspan` cell in the **last column** produces no spacer anywhere in the tree
([#522](https://github.com/jhaygood86/PeachPDF/issues/522)). `RowSpannedBoxes` is the reliable
answer. (A spacer that *is* created is also reachable twice, since `CssBox`'s `ParentBox` setter appends
it to the table while `InsertEmptyBoxes` inserts it into the row.) The spacer is still load-bearing for a
separate reason unrelated to closing or detecting a span: `GetCellRealColumnIndex` counts a later row's
own real cells by summing `colspan` across `row.Boxes`, so without a placeholder occupying the spanned
column, every real cell after it in that row would be miscounted by one column, corrupting column-width
distribution and cell positioning for the whole row. That role stays regardless of #522.

**`boxesThatEndOnRow` (`TableRowCursor.RowSpannedBoxes[rowIndex]`) must be asked before
`ResumedFromAnEarlierPass`/`stoppedCells`, not after.** `ResumedFromAnEarlierPass` matches by reference
against a carried record seeded when *this pass* first resumed the cell at the row that **opened** its
span — several rows before the one now ending it — and that record is never cleared once consumed.
Asking it first for a cell that also appears in `boxesThatEndOnRow` reads that stale match and `continue`s
past it, so `CloseSpanningCell` is never entered at all for a cell whose own content took more than one
resumption pass to finish: measured as the exact shape of
[issue #521](https://github.com/jhaygood86/PeachPDF/issues/521) once the `contentSlot` fix above was in
place — the close-at-band-boundary logic was correct, but the call it depends on silently never ran.

**A spanning cell's own content overflowing into the row that ends its span must raise `rowMaxBottom`
for every cell in that row, not just the spanning cell.** Unpaginated table layout already grows every
row a tall rowspan cell spans (ordinary flow), but paginated layout's `rowMaxBottom` for that row comes
from `TableRowCursor.MaxBottom`, which deliberately excludes spanning cells (needed so the straddle
correction can still move the row — see `RecordForeignWrite` above). Left alone, the row's *other* cells
close at the smaller `MaxBottom` value while the spanning cell itself closes lower (at its own
`contentBottom`), so their bottom borders no longer line up — visible as a sibling `<td>`'s border cutting
across the still-open rowspan cell rather than meeting its edge. `LayoutBodyRow` asks
`SpanningCellBandGeometry` for every box in `boxesThatEndOnRow` *before* the row's ordinary
vertical-alignment loop runs, and raises `rowMaxBottom` to `contentBottom` wherever the cell's own content
band (`contentSlot`) reaches at least as far as the row's band (`slot`) — so every cell this row aligns,
spanning or not, sees the same final value.

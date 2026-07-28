# A spanning cell's box is closed per band, not stretched across them

_CSS Fragmentation Level 3 / css-tables-3 §6.1. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

A `rowspan` cell belongs to the row that **opens** the span, but its bottom is written by the row that
**ends** it — `CssLayoutEngineTable.LayoutBodyRow`, reaching it either as a `CssSpacingBox`'s
`ExtendedBox` or through `TableRowCursor.RowSpannedBoxes[rowIndex]`. Where those two rows are in
different bands, giving the cell the ending row's bottom makes one box that spans a page boundary, and
its borders and background are then drawn straight through the page edge.

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
- **It may never close above the cell's own content.** Only the *box* is fragmented; what lies below the
  close is then inside no fragment at all. Measured as ~100 unclaimed words in
  `TableCellBreakTokenTests.APaginatingTable_DropsNoWord`, which is the suite's word census and the
  thing that catches it. A cell whose content overflows its band keeps the stretched box — that case
  wants the flow-level continuation §6.1 asks for, which is not the box close's to invent
  ([its gap file](../accepted-gaps/table-a-rowspan-cells-own-content-is-not-continued-past-its-band.md)).
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
it to the table while `InsertEmptyBoxes` inserts it into the row.)

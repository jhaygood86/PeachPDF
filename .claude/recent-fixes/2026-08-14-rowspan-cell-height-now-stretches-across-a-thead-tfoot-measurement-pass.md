# A rowspan cell in a detached thead/tfoot now stretches to cover every row it spans, closing issue #742

[Issue #742](https://github.com/jhaygood86/PeachPDF/issues/742), found while verifying
[#740](https://github.com/jhaygood86/PeachPDF/issues/740)'s own fix: a `rowspan` cell inside a multi-row
`<thead>`/`<tfoot>` never got its height stretched to cover every row it spans - it kept only its own
natural (single-row) content height, even when the rows it spans were visibly taller. Measured directly
off the laid-out box tree, not assumed.

## Root cause

`CssLayoutEngineTable.LayoutBodyRow`'s ordinary vertical-alignment loop only stretches a `rowSpan == 1`
cell to the row's own max bottom; a `rowSpan > 1` cell is instead supposed to be closed later, on the row
its span ends on, via `CloseSpanningCell` (reached through a `CssSpacingBox` placeholder's `EndRow`, or
through `TableRowCursor.RowSpannedBoxes`, keyed by `GetEffectiveEndRowIndex(rowIndex, rowSpan)`). Both
routes depend on `TableRowCursor.RowIndex` incrementing per row so `rowIndex` matches the row a rowspan
actually ends on. During a detached header's/footer's own measurement pass
(`CssLayoutEngineTable.DetachAndMeasureRepeatedRowGroups`, via `TableRowCursor.ForRowGroupMeasurement`),
`RowIndex` is pinned at `-1` for every row of the group instead - by design, since a row-group
measurement's own row numbering and rowspan bookkeeping are never the body's. With `rowIndex` always
`-1`, that machinery never engaged for a header/footer rowspan, and the cell kept whatever height its own
initial content layout gave it.

## The fix

Deliberately does **not** give header/footer row measurement a real per-row `TableRowCursor.RowIndex` to
make the existing machinery engage. That machinery's other half is `CloseSpanningCell`, whose own
bookkeeping (straddle correction, fragmentainer band geometry, `TableRowCursor.RecordForeignWrite`/
`BeginRow`/`Retract`) is a pagination concept a row-group's own one-shot, never-resumed measurement pass
has no analogue for - `CloseSpanningCell`'s own comments note plainly that "the row-group measurement
cursors... place rows without ever calling `BeginRow`". Reaching it from a header/footer context risked
exercising fragmentainer-dependent code paths never designed or tested for that context, for no reason.

Instead, `DetachAndMeasureRepeatedRowGroups`'s header/footer loops keep this bookkeeping themselves, in a
`Dictionary<int, List<CssBox>>` scoped to one loop and keyed by a row-group-local row index with no
meaning outside it - mirroring, one row at a time, what `InsertEmptyBoxes`'s `CssSpacingBox` placeholder
does for an ordinary body row:

- `RegisterRowSpanCellsEndingRow` - as each row is placed, registers every `rowSpan > 1` cell against the
  row-group-local row its span ends on.
- `GrowForClosingRowSpanCells` - before that ending row's own bottom is finalized, grows it to fit any
  cell ending there, using that cell's own natural (pre-stretch) `ActualBottom` from the row that opened
  it. Without this half, a first attempt (a simple post-pass run once after the whole loop, stretching
  each cell to whatever the group's already-settled row heights added up to) left the group's own total
  height too short whenever the spanning cell's own content was *taller* than the rows it spans combined
  - confirmed by a second, deliberately adversarial repro (a rowspan cell with much longer text than its
    sibling rows) after the first fix already had a passing test: the header's own bottom stayed short and
    the table body started overlapping the header's own tallest content. Growing the row *before* it's
    finalized (the same order `LayoutBodyRow`'s own `sb.EndRow == rowIndex` fold-back uses for a body row)
    is what a post-hoc-only stretch of the cell alone cannot fix, since by the time all rows are already
    laid out it is too late to grow one without shifting everything after it.
- `CloseRowSpanCellsEndingOnRow` - once the row's bottom is finalized (now correctly grown if needed),
  stretches every cell ending there to it and reruns `CssLayoutEngine.ApplyCellVerticalAlignment` - the
  same idiom `LayoutBodyRow`'s own loop uses for a `rowSpan == 1` cell.
- `CloseOverflowingRowSpanCells` - a `rowSpan` declared past the group's own remaining rows (e.g.
  `rowspan="99"` in a two-row `<thead>`) registers against a row index the loop never reaches, so the
  three methods above never ran for it; clamped to the group's own actual last row once the loop finishes,
  without growing it further (there is no later row left in the group to push down to make room).

No `TableRowCursor` state is read or written anywhere in this.

A first version of `RegisterRowSpanCellsEndingRow` computed `endRow` as plain `rowIndex + rowSpan - 1` -
review (not inspection alone; confirmed with a purpose-built repro) found this wrong whenever the header/
footer group has a `visibility: collapse` row in it: `rowIndex` is the *filtered* per-iteration counter
(collapsed rows are skipped before it ever advances), but a `rowSpan`'s own value counts rows in the
group's raw source order, collapsed ones included (CSS 2.1 §17.6.1) - the identical mismatch issue #665
already fixed for `_bodyRows` via `GetEffectiveEndRowIndex`/`_bodyRowOriginalIndices`, just unfixed here.
`GetEffectiveEndRowIndex` is now a shared static method parameterized over an original-indices list and a
row count, rather than reaching for `_bodyRowOriginalIndices`/`_bodyRows` directly; the existing
`_bodyRowOriginalIndices`-based instance overload is now a one-line wrapper over it. A new
`ComputeRowGroupOriginalIndices(CssBox? groupBox)` builds the equivalent mapping for a header's/footer's
own rows (computed once, up front, before either loop runs - the mapping needs to know every row's
collapsed/not status in advance, which the previous per-row-as-you-go registration couldn't, since a
rowspan's own remapping can depend on rows the loop hasn't reached yet), and `RegisterRowSpanCellsEndingRow`
now calls the shared `GetEffectiveEndRowIndex` overload against it instead of the naive sum.

## A second, pre-existing bug this surfaced

Testing the overflowing-rowspan case (`rowspan="99"` in a two-row header) crashed with
`IndexOutOfRangeException` from `CollapsedBorderModel.Horizontal` - confirmed pre-existing (reproduces
identically without any of this fix's own changes, from #736's own `TableGrid.Build`, not something this
change introduced). `TableGrid.Build`'s `CellSpan` recorded a cell's `LastRow` straight from
`ComputeColumnPlacements`'s own placement, which for a header/footer row's rowspan is unclamped raw
arithmetic (`CssLayoutEngineTable.GetLastRowInGrid`) - so a `CellSpan.LastRow` could name a row past the
grid's own last one, and `CollapsedBorderModel` indexes its own line-width arrays with exactly that value,
with no bounds check of its own. Fixed alongside this one, in the same file already under review: `Build`
now clamps `LastRow` to the grid's own `rowCount - 1` when recording a `CellSpan` (the slot-filling loop
just below it was already correctly bounded by `rr < rowCount` - only the recorded span itself was not).

## Evidence

New regression tests: `TableLayout_RowspanInThead_CellStretchesToCoverEveryRowItSpans` (the core fix),
`TableLayout_RowspanCellTallerThanItsRows_GrowsTheHeaderToFitInstead` (the row-growth half, the adversarial
case above), `TableLayout_RowspanExceedingTheadsOwnRowCount_DoesNotThrow` (the `TableGrid.Build` crash
fix), and `TableLayout_RowspanAcrossACollapsedTheadRow_ClosesOnTheCorrectRow` (the collapsed-row remapping
fix). Each verified against its actual pre-fix failure (a wrong height, an overlapping body row, a thrown
exception, or closing on the wrong row, respectively), not merely by passing once the fix landed. Full
suite green (8833 tests, net8.0), zero warnings on `dotnet build -t:Rebuild`, 100% diff coverage
(`diff-cover` against `main`).

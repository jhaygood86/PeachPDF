# A rowspan cell in a detached `<thead>`/`<tfoot>` is never stretched to cover the rows it spans

A `rowspan` cell inside a multi-row `<thead>`/`<tfoot>` never gets its height stretched to cover every
row it spans - it keeps only its own natural (single-row) content height, even when the rows it spans are
visibly taller. Found while fixing [#740](https://github.com/jhaygood86/PeachPDF/issues/740) (a
rowspan-created gap in a header/footer row mis-positioning the *following* cell): that fix corrects the
following cell's X position and width, but the rowspan cell's own height is a separate, still-open defect
through a different part of the layout pipeline.

Concrete case (measured directly off the laid-out box tree):
`<thead><tr><th rowspan="2">A</th><th style="height:60px">B</th></tr><tr><th style="height:60px">D</th></tr></thead>`
- `B` and `D` each get their declared height (45pt, `1px = 0.75pt`), so the header's two rows together are
90pt tall. `A`, spanning both rows, gets 15pt - its own single line of text, not stretched to the
combined 90pt the two rows it spans actually take up.

`CssLayoutEngineTable.LayoutBodyRow`'s vertical-alignment pass only stretches a cell to `rowMaxBottom`
when `GetRowSpan(cell) == 1`; a `rowSpan > 1` cell is instead supposed to be closed later, on the row its
span ends on, via `CloseSpanningCell` - reached either through a `CssSpacingBox` placeholder
(`sb.EndRow == rowIndex`) or through `TableRowCursor.RowSpannedBoxes` (keyed by the row the span ends on,
`GetEffectiveEndRowIndex(rowIndex, rowSpan)`). Both paths depend on `TableRowCursor.RowIndex`
incrementing per row so `rowIndex` matches the row a rowspan actually ends on. During a detached
header's/footer's own measurement pass (`CssLayoutEngineTable.DetachAndMeasureRepeatedRowGroups`, via
`TableRowCursor.ForRowGroupMeasurement`), `RowIndex` is pinned at `-1` for every row of the group instead
- by design, since its rows are not body rows and neither its row numbering nor its rowspan bookkeeping is
theirs (`ForRowGroupMeasurement`'s own doc comment). With `rowIndex` always `-1`, the
`RowSpannedBoxes`/`CloseSpanningCell` machinery that depends on it never engages correctly for a
header/footer rowspan, so the cell is simply never closed and keeps whatever height its own initial
content layout gave it.

This is a genuine layout defect (a rowspan cell's box is the wrong size), not a deliberate scope line -
it's tracked as [#742](https://github.com/jhaygood86/PeachPDF/issues/742).

**Deliberately out of scope** of both #736's and #740's fixes: closing this needs giving header/footer row
measurement its own real, per-row-incrementing row index (distinct from `_bodyRows`' numbering, which
`RowIndex`'s `-1` sentinel exists specifically to avoid colliding with), so
`GetEffectiveEndRowIndex`/`RowSpannedBoxes`/`CloseSpanningCell` can engage the same way they already do
for body rows. That touches the pagination-sensitive `TableRowCursor` design directly, unlike #740's fix
(which works entirely through `GetCellRealColumnIndex` and stays clear of `TableRowCursor`) - warranting
its own focused change and test pass.

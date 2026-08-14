# GetCellRealColumnIndex and LayoutBodyRow's column cursor are rowspan-occupancy-aware too, closing issue #740

[Issue #740](https://github.com/jhaygood86/PeachPDF/issues/740), found while verifying
[#736](https://github.com/jhaygood86/PeachPDF/issues/736): `CssLayoutEngineTable.GetCellRealColumnIndex`
had the exact same Boxes-list-summing bug #736 fixed in `TableGrid.Build`, but #736's fix didn't touch it -
`TableGrid.Build` made its own column computation self-sufficient rather than routing through
`GetCellRealColumnIndex`. `GetCellRealColumnIndex` is called directly by `CssLayoutEngineTable.LayoutBodyRow`
(reused for header/footer row layout) to compute a cell's rendered width, and `LayoutBodyRow`'s `currentX`
cursor only advances past a rowspan gap when a `CssSpacingBox` placeholder occupies it in `row.Boxes` -
which never happens for a header/footer row (`InsertEmptyBoxes` only ever pads `_bodyRows`). So a cell
after a rowspan gap in a detached `<thead>`/`<tfoot>` row rendered at the wrong X position and width - not
just a border-resolution gap, an actually mis-rendered cell.

## The fix

`TableGrid.Build`'s placement algorithm (the rowspan-occupancy tracking pass #736 added) is now shared
rather than private to that method - extracted into `TableGrid.ComputeColumnPlacements`, returning a
`Dictionary<CssBox, CellPlacement>` plus the resulting column count. `CssLayoutEngineTable` computes this
once per layout pass (`ComputeColumnPlacements()`, called right after `AssignBoxKinds` populates
`_allRows`/`_bodyRows`/`_headerBox`/`_footerBox`, and before `InsertEmptyBoxes`'s own first call to
`GetCellRealColumnIndex`) and caches it in `_columnPlacements` - unconditionally, unlike the
collapsed-border-only `_grid`, since cell positioning matters for a `separate` table too.
`GetCellRealColumnIndex` becomes a simple cached lookup (a `CssSpacingBox` shares its `ExtendedBox`'s
placement, since it stands in for exactly that cell), replacing all 7 of its call sites' dependence on the
row's own `Boxes` order.

That alone only fixed the cell's *width* (`GetCellWidth(columnIndex, cell)` now gets the right column) -
`LayoutBodyRow`'s `currentX` still advanced sequentially, one `Boxes`-list entry at a time, so a cell
positioned after an un-padded gap still landed at the wrong X. Fixed by tracking a second cursor,
`expectedColumn` (the grid column the row loop expects to be at, separate from the existing
Boxes-list-entry-counting `currentColumn`), and walking `currentX` forward past any gap between it and a
cell's real `columnIndex` before placing that cell - the same width-plus-trailing-spacing formula a real
cell in that column would have advanced by, applied one skipped column at a time.

## What was deliberately not done

The rowspan cell's own height still isn't stretched to cover every row it spans in a detached header/footer
- confirmed by direct measurement, not assumed - a separate, deeper defect through `TableRowCursor.RowIndex`
being pinned at `-1` for the whole of a header/footer measurement pass (so the existing
`RowSpannedBoxes`/`CloseSpanningCell` height-closing machinery, keyed by row index, never engages for a
header/footer rowspan). This fix stays entirely within `GetCellRealColumnIndex`/`LayoutBodyRow`'s column
cursor and never touches `TableRowCursor`, deliberately - tracked separately as
[#742](https://github.com/jhaygood86/PeachPDF/issues/742).

## Evidence

New regression test (`CssLayoutEngineTableTests.TableLayout_RowspanInThead_PositionsLaterRowCellInItsOwnColumn`)
measures the laid-out box tree directly (via the header proxy's `SourceBox`, since a `<thead>` is detached
from the ordinary box tree) and asserts the previously-mispositioned cell now shares its column's exact
X/width with the other real cells in that column. Full suite green (8827 tests, net8.0), zero warnings on
`dotnet build -t:Rebuild`.

# A rowspan-created gap in a detached `<thead>`/`<tfoot>` row also mis-positions the following cell's own layout

`CssLayoutEngineTable.GetCellRealColumnIndex` sums the `colspan` of preceding cells within a row's own
`Boxes` list to find where a cell starts. For an ordinary body row this is correct because
`CssLayoutEngineTable.InsertEmptyBoxes` pads a rowspan's gap with a `CssSpacingBox` placeholder before
layout runs. A detached `<thead>`/`<tfoot>` row-group's own rows are never passed through
`InsertEmptyBoxes`, so a `rowspan` cell that starts in an earlier row of the *same* header/footer group
and reaches into a later row leaves the same kind of gap [#736](https://github.com/jhaygood86/PeachPDF/issues/736)
fixed for `TableGrid.Build`'s collapsed-border grid occupancy - except `GetCellRealColumnIndex` is also
called directly by `CssLayoutEngineTable.LayoutBodyRow` (reused for header/footer row layout) to compute
a cell's rendered width (`GetCellWidth(columnIndex, cell)`), and `LayoutBodyRow`'s `currentX` cursor only
advances past a rowspan gap when a `CssSpacingBox` placeholder occupies it in `row.Boxes` - which never
happens for a header/footer row.

Concrete case (measured directly off the laid-out box tree): `<thead><tr><th rowspan="2">A</th><th>B</th></tr><tr><th>D</th></tr></thead>`
in a `width:400px` table - `A`/`x` (column 0) land at `X=20, Right=170.5`; `B`/`y` (column 1) land at
`X=170.5, Right=320`. `D`, which should render under column 1 alongside `B`/`y`, instead lands at
`X=20, Right=170.5` - column 0's exact position and width, overlapping/underneath `A` instead of `B`.
After #736's fix the collapsed border for `D` correctly resolves at column 1's boundary, so the border
renders one column to the right of where `D`'s own text content renders.

This is a genuine layout defect (a cell renders under the wrong column, at the wrong width), not a
deliberate scope line - it's tracked as
[#740](https://github.com/jhaygood86/PeachPDF/issues/740), found while verifying #736's fix didn't also
need to cover cell positioning (it doesn't touch `GetCellRealColumnIndex` at all, only `TableGrid.Build`'s
own independent column computation).

**Deliberately out of scope** of #736's fix: `TableGrid.Build`'s own fix made its column computation
self-sufficient (rowspan-occupancy-aware) rather than trusting `GetCellRealColumnIndex` - an option that
doesn't carry over here, since `LayoutBodyRow` needs an actual column index up front for every cell it
lays out, not grid topology it can query after the fact. The more direct fix - extending
`InsertEmptyBoxes` to also pad a detached header's/footer's own rows with `CssSpacingBox` placeholders,
fixing `GetCellRealColumnIndex` at its root for every consumer at once - needs two things sorted out
first: `CssSpacingBox.EndRow` is read elsewhere in `LayoutBodyRow` (`sb.EndRow == rowIndex`) to close a
rowspan cell and fold its bottom into row-height tracking, but `TableRowCursor.RowIndex` is pinned at
`-1` for every row of a header/footer group during its measurement pass (`ForRowGroupMeasurement`) rather
than incremented per row the way body-row layout does, so the existing `EndRow`-comparison logic would
need its own header/footer-aware row numbering first; and any padding pass has to operate on the same
`visibility:collapse`-filtered, group-local row list layout already uses
(`_headerBox.Boxes.Where(r => !IsRowCollapsed(r))`), mirroring how `_bodyRows`/`_bodyRowOriginalIndices`
keep that distinction for body rows - warranting its own focused change and test pass.

# A rowspan cell inside a multi-row `<thead>`/`<tfoot>` can be dropped from the collapsed-border grid

`TableGrid.Build`'s column-index computation (`CssLayoutEngineTable.GetCellRealColumnIndex`) sums the
`colspan` of preceding cells within a row's own `Boxes` list to find where a cell starts. For an ordinary
body row this is correct because `CssLayoutEngineTable.InsertEmptyBoxes` pads a rowspan's gap with a
`CssSpacingBox` placeholder before this runs. A detached `<thead>`/`<tfoot>` row-group's own rows are never
passed through `InsertEmptyBoxes` (only `_bodyRows` is), so a `rowspan` cell that starts in an earlier row
of the *same* header/footer group and reaches into a later row leaves a genuine gap with no placeholder -
the later row's own remaining cells then compute the wrong starting column, and `TableGrid.Build`'s
`slots[...] ??= cell` first-writer-wins placement silently drops the later cell from the grid entirely for
the column(s) it should have occupied (the earlier row's rowspan fill runs first and already claims that
slot).

Concrete case: a `<thead>` with `<tr><th rowspan="2">A</th><th>B</th></tr><tr><th>D</th></tr>` - `D` is
row 1's only real cell, computed at column 0 (nothing precedes it in that row's own `Boxes`) instead of
column 1 (column 0 is occupied by `A`'s rowspan). `TableGrid.CellAt(1, 1)` returns `null` instead of `D`,
so any border-conflict resolution reading that slot - both the whole-table `CollapsedBorderModel.Resolve`
for the header's own internal grid line, and `CollapsedBorderModel.ResolveRepeatedGroupBoundary` for a
repeated header's boundary to the body - silently drops `D`'s own border as a candidate for that column.

This is a genuine CSS 2.1 [§17.6.2](https://www.w3.org/TR/CSS21/tables.html#border-conflict-resolution)
deviation (a declared border is dropped as a resolution candidate outright, not merely outranked), not a
deliberate scope line - it's tracked as
[#736](https://github.com/jhaygood86/PeachPDF/issues/736), found while adding
`CollapsedBorderModel.ResolveRepeatedGroupBoundary` (the repeated-`<thead>`/`<tfoot>`-boundary phase of
issue #735's fix) and testing it against a multi-row header. `ResolveRepeatedGroupBoundary` itself was
fixed in the same change to read cell occupancy via `TableGrid.CellAt` (rowspan/colspan-aware) rather than
a hand-rolled, `Boxes`-list-scanning helper - which is a real improvement (the hand-rolled version would
have *also* misattributed a rowspan cell it could see at all, on top of this deeper gap it can't see
around) - but that fix cannot correct occupancy the grid itself never recorded.

**Deliberately out of scope** of issue #735's fix: closing this needs either extending
`CssLayoutEngineTable.InsertEmptyBoxes` (or an equivalent pass) to also pad a detached header's/footer's
own rows with `CssSpacingBox` placeholders before `BuildTableGrid` runs, or giving `TableGrid.Build` a
column-index callback that's aware of rowspan occupancy from earlier rows in the same row-group rather
than only counting `Boxes`-list predecessors - a change to shared grid-construction code every other
`TableGrid` consumer (column-width distribution, the main §17.6.2 resolution, `visibility: collapse`
handling) also depends on, warranting its own focused change and test pass rather than folding it into
an already large border-collapse PR.

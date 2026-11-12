# A table cell with `columns` painted none of its content (#2342)

`CssLayoutEngineColumns.Layout` resolved the container width with `GetBoxWidth`, which measures against the containing block. A cell's containing block is its row, which has no width of its own, so the width came out as nothing: every column was zero wide and the words and child backgrounds collapsed. The table algorithm has already written the cell's right edge before the cell's content is laid out, so for a cell placed by the table engine (`PositionAssignedByEngine`, `table-cell`) the engine now derives the content width from `ActualRight - Location.X` minus its border and padding.

Found by running it: the two blocks of the repro landed 10pt apart (the gap, with a zero-width column). Pinned by `MulticolTableCellTests`.

Not done: the float-in-a-cell gap (`a-multicol-container-with-a-float-in-a-table-cell-is-not-columnized.md`) is unchanged.

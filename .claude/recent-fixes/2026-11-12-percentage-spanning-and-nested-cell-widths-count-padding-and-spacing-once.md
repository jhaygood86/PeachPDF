# Percentage, spanning and nested declared cell widths count padding and spacing once

Follow-up to [the default cell padding](2026-11-12-table-cells-have-a-default-padding-and-a-nested-table-keeps-its-spacing.md). The default `padding: 1px` made three older mis-sizings visible; each had been matching Chrome only because the cells had no padding. They were reported on the review of that change (the review's fourth case, a float holding a table with a CSS width, did not reproduce; see below).

**What was wrong, and the one-line fix for each**

1. `CalculateColumnWidths` added `CellInlineSizeExcludes` (padding plus border) to every declared cell width. For a length that is right (content-box). A percentage is a share of the grid that already includes the cell's padding in Chrome: `width:50%; padding:20px` beside an unsized cell is 225/225 of 450, and 30% is 135/315. Fix: skip the addition for a percentage.
2. `SpreadSpannedWidth` compared a spanning cell's need to the sum of its columns only. The cell also fills the `border-spacing` between those columns, so it was widened by one spacing too many. Fix: `required - total - GetInteriorSpacing(col, colSpan)`. Old main matched Chrome here by accident: no padding, plus one spacing too many, equals the padding Chrome has.
3. `GetMinMaxSumWords` called `UpdateMinWidth` with a child's declared width for every child, so a `td width=80` in a nested table made the whole nested table at least 80px + padding + spacing wide, and the outer `td width=80` grew. A cell's declared width is its column's preferred width, not a floor (Chrome keeps the outer cell at its declared 80px plus padding). Fix: skip the floor for a table cell. A block child's definite width is still a floor.

**Found by running it.** The review's figures were the symptoms; what moved each to Chrome's number to 0.01pt was reading Chrome's border-box shares and the nested outer cell (61.5pt, 82px) directly. A wide nested cell (a 120px box) still widens the outer cell (96pt in both), which is what a "never a floor" change would have broken.

**Not done.**
- The review's case 3 (a float holding a table with `width:350px` and `border-spacing:5px`, a `colspan=2 width=80` row, then a `width:90px` cell with five words) did not reproduce: the table is 262.5pt (350px) in Chrome and here, floated, in a floated div, in a cell, with `width` as an attribute or style. What does differ there is the column split of the second row (Chrome 69/182pt, here 174/77pt), which is the same on the build before the padding change, so it is not part of this.
- The nested table in the `width=80` cell is 59.5pt here against Chrome's 60 (the cell that holds it is exact); the half point is not understood and was not chased.

**Evidence.** Each of the three rules fails its tests when reverted (engine `TableCellWidthRulesTests`, PDF-level `TableDefaultPaddingPdfTests`). Random nested tables against Chrome: no case worse than the build before.

# `visibility: collapse` table layout: two residual gaps

Tracking issue: [#665](https://github.com/jhaygood86/PeachPDF/issues/665) (follow-up to
[#639](https://github.com/jhaygood86/PeachPDF/issues/639), which implemented CSS 2.1
[§17.6.1](https://www.w3.org/TR/CSS21/tables.html#dynamic-effects) table row/row-group/column/
column-group `visibility: collapse` layout in `CssLayoutEngineTable`).

Two narrower cases were deliberately left out of scope of that change:

1. **A `rowspan`/`colspan` cell spanning across a collapsed row/column may size or align
   incorrectly.** Collapsed rows are filtered out of `_bodyRows` before `InsertEmptyBoxes` inserts
   its rowspan placeholder boxes, and that method indexes later rows by their position in
   `_bodyRows` — a list a collapsed row no longer appears in. A rowspan cell that opens before a
   collapsed row and extends into or past it can have its placeholder land in the wrong row once the
   collapsed row's position is removed from the list, misaligning that column's grid.

2. **A collapsed column's own cell content can still influence column widths before it is zeroed.**
   `CollapseColumnWidths` runs last, after `DetermineMissingColumnWidths`/`EnforceMaximumSize`/
   `EnforceMinimumSize` have already sized every column — including a soon-to-collapse one — from
   its own content. Those content-based passes (`GetColumnsMinMaxWidthByContent`,
   `GetColumnMinWidths`) don't know a column is about to collapse, so a collapsed column's own
   (invisible) cell content can still push its pre-collapse width up and, through
   `EnforceMinimumSize`'s colspan-neighbor adjustment, narrow the width ultimately given to the
   *next* column.

**Deliberately out of scope.** Both are edge cases layered on top of an already-substantial layout
change; the common cases (a collapsed row/column with no rowspan/colspan crossing it) lay out
correctly and are covered by `TableVisibilityCollapseIntegrationTests`. Fixing either means teaching
`GetColumnsMinMaxWidthByContent`/`GetColumnMinWidths`/`InsertEmptyBoxes` about collapsed rows/columns
directly, rather than filtering only at the row-loop/final-width level as the current implementation
does.

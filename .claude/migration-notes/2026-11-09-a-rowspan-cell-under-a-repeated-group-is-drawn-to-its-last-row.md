# A rowspan cell under a repeated group is drawn down to its last row, once

Before: a `rowspan` cell spanning a row taller than a page, in a table with a repeated `<thead>` or `<tfoot>`, could lose
part of itself on a continuation page - its fill, side and bottom borders stopped at the end of one row's band, short of the
rows beside it - a page in the middle of a long span could lose the cell entirely, and the cell could be drawn over the
repeated header or footer. Now the cell's background and borders are drawn beside every row of its span on every page it
crosses, never under a repeated group, and its text once, where its own row puts it. Elsewhere a spanning cell is built as
before; the one other difference is that a page emitted while a spanning cell's height was still unsettled is emitted again
once the span closes, so it shows the cell at its final height.

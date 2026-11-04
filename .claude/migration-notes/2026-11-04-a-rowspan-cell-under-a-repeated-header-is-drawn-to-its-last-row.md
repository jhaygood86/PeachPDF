# A rowspan cell under a repeated header is drawn down to its last row

Before: a `rowspan` cell next to a row taller than a page, in a table with a repeated `<thead>` or `<tfoot>`, could lose part
of itself on a continuation page - its fill, side and bottom borders stopped at the end of its first row's band, short of the
rows beside it - and a page in the middle of a long span could lose the cell entirely. Now each part of the cell that a row's
band shows is drawn, so the cell reaches the bottom of its last row on every page it crosses. The cell is still drawn once
wherever its copies would draw the same part in the same place.

With row backgrounds painted only under the cells that start in their row, a spanning cell keeps its own fill and text under a
striped row (see the table track background note).

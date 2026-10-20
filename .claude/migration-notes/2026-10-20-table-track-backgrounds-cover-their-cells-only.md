# Row, group and column backgrounds cover only their own cells

Before (v0.9.23 included): a background on a `<tr>`, `<thead>`/`<tbody>`/`<tfoot>`, `<col>` or `<colgroup>` filled that
element's whole rectangle, in document order. It showed in the `border-spacing` gaps between cells and in slots no cell
filled, and it was painted over any cell spanning into it from an earlier row or column, so a transparent `rowspan` cell
showed a later row's colour in its lower part, and a `colspan` cell took the colours of every column it covered. Now, as CSS
2.1 §17.5.1 requires, each one's background covers exactly the cells that originate in it, beneath every cell: a cell spanning
several rows shows the background of the row it starts in over its whole height, a `colspan` cell that of the column it starts
in, and the gaps and empty slots show the table's own background. Background images and gradients are still positioned
against the row's, group's or column's own rectangle, so a gradient on a row runs across all of its cells.

Because each cell now paints those backgrounds, they follow the cell: a cell with `visibility: hidden` no longer shows its
row's or column's background, and a cell's own `opacity`, `transform` or `filter` applies to them too. A track's own
`border-radius` and `background-clip` do not shape them.

`empty-cells: hide` now applies to the separated borders model only, as CSS 2.1 §17.6.1.1 specifies: before, it also hid an
empty cell of a `border-collapse: collapse` table, whose background (and now its row's and column's) is painted again.

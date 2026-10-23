# Row, group and column backgrounds cover only their own cells

Before (v0.9.23 included): a background on a `<tr>`, `<thead>`/`<tbody>`/`<tfoot>`, `<col>` or `<colgroup>` filled that
element's whole rectangle, in document order. It showed in the `border-spacing` gaps between cells and in slots no cell
filled, and it was painted over any cell spanning into it from an earlier row or column, so a transparent `rowspan` cell
showed a later row's colour in its lower part, and a `colspan` cell took the colours of every column it covered. Now, as CSS
2.1 §17.5.1 requires, each one's background covers exactly the cells that originate in it, beneath every cell: a cell spanning
several rows shows the background of the row it starts in over its whole height, a `colspan` cell that of the column it starts
in, and the gaps and empty slots show the table's own background. Background images and gradients are still positioned
against the row's, group's or column's own rectangle, so a gradient on a row runs across all of its cells.

Each row, group and column still paints its own background, in the same order as before, so `opacity`, `filter` and
`transform` apply to it as they did: a row's to the row's background and its cells, a cell's to the cell alone. Two narrow
cases paint a track's background together with the cell instead, under the cell's own effects: a `rowspan` cell continuing
onto a page its starting row does not reach, and a column with `visibility: hidden` (see below); a track layered above one
painted that way is painted with the cell too, to keep the layer order. A track's own `border-radius` and `background-clip`
do not shape the cell areas it paints.

Other changes a document author can see, each following the specification:

- **`empty-cells: hide` in the separated borders model:** a hidden empty cell shows the table's background, not its row's or
  column's (CSS 2.1 §17.6.1.1: "letting the table background show through"). `main` and Chrome keep the row's and column's
  background there, so a striped table with hidden empty cells now has holes in its stripes.
- **`empty-cells` in the collapsed model:** it no longer hides anything. CSS 2.1 §17.6.1.1 defines it for the separated model
  only; before, it also hid an empty cell of a `border-collapse: collapse` table, whose background (and its row's and column's)
  is painted again.
- **A cell with `visibility: hidden`** shows the table's background (css-tables-3 §5.3.2 treats hidden empty cells "as if
  `visibility: hidden` was specified on them, letting the table background show through"); before, its row's background showed
  there. A hidden row or row group paints no background, as before.
- **`visibility: hidden` on a `<col>` or `<colgroup>`** still paints its background under its cells, as `main` and Chrome do:
  CSS 2.1 §17.3 gives a column's `visibility` a meaning for `collapse` only.
- **`border-spacing` with spans:** with a wide spacing and spanning cells, Chrome draws a thin strip of the track's background
  across the gap; PeachPDF leaves the gap to the table, as §17.5.1 says.

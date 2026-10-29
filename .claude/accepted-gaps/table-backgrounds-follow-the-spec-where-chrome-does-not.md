# Table track backgrounds follow the spec where Chrome does not

Since table track backgrounds cover only the cells that originate in them
([#1894](https://github.com/jhaygood86/PeachPDF/issues/1894), [#1895](https://github.com/jhaygood86/PeachPDF/pull/1895)),
PeachPDF and Chrome differ in a few places. Each one is PeachPDF following CSS 2.1 / css-tables-3 and Chrome not, so none is a
spec deviation to track; they are recorded so a future comparison against Chrome does not "fix" them back.

- **`empty-cells: hide` in the separated model.** A hidden empty cell shows the table's background. CSS 2.1 §17.6.1.1 and
  css-tables-3 §5.3.2 both say it is not rendered at all, "letting the table background show through". Chrome (156) and
  `main` show the row's and column's backgrounds there, so a striped table with hidden empty cells has holes in its stripes
  in PeachPDF.
- **Spanning cells with a wide `border-spacing`.** Chrome draws a thin strip of a row's or column's background across the gap
  beside a spanning cell (384 systematic cases in the #1895 review: 12 mismatches, all `border-spacing: 6px` with spans).
  §17.5.1 gives the spacing to the table's background.

## Not a Chrome difference, but close to one

Where a track has no fragment over a cell on the page - a `rowspan` cell continuing onto a page its starting row does not
reach - or is a `visibility: hidden` column (which paints nothing itself, while CSS 2.1 §17.3 gives a column's `visibility` a
meaning for `collapse` only), the cell paints that track's background beneath its own, together with every track layered
above it, so the cell's own `opacity`, `filter` or `transform` applies to them. Everywhere else the track paints it, outside
the cell's effects, as Chrome does. Neither spec says which effects apply to a track's background (css-tables-3 §5.3.2 has the
cell draw it; CSS 2.1 §17.5.1 puts it on a layer beneath the cell), so this is not tracked as a deviation either. Closing it
would mean giving those rows and columns a paint slot on pages where they have no box of their own.

# Table layout fixes from the CSS tables tests

**Before / now**, each a change a document author can see:

- **Row and row-group borders.** A `border` on a `tr`, `thead`, `tbody`, `tfoot`, `col` or `colgroup` of a table with
  `border-collapse: separate` (the default) used to be drawn around that row or group. It is no longer drawn: CSS 2.1 §17.6
  says these elements have no borders of their own in the separated model, and browsers ignore them. (In
  `border-collapse: collapse` tables they still take part in the collapsed borders, as before.)
- **`th` alignment.** A `th` used to be centered always. It is now centered only when its parent's `text-align` is the
  initial value, so a `th` in a row or table with `text-align: right` (or `end`, ...) is aligned like its parent, as in a
  browser. `text-align: inherit` on any element is now applied; it used to be dropped.
- **`max-height` on a table** used to cut the table short of its rows. It is now ignored, as for a table cell.
- **Empty tables** (no cells) used to ignore their own `width` and `height` and collapse to their borders. They now take the
  size the author gave them. An absolutely positioned table's percentage `width` now resolves against its positioned
  containing block rather than its parent.
- **A table with only `thead`/`tfoot` rows** and a `height` larger than its rows now gives the extra height to those rows; it
  used to leave it empty below them.
- **`rowspan="0"`** used to be read as one row. It now spans to the last row of the row group.
- **`<table rules>`**: any value other than `none` now switches the table to the collapsed border model, and
  `rules="groups"` draws a line after each `thead` and `tbody`.

See [Tables](../../docs/html-css-support.md#tables).

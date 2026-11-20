# A table cell's percentage height gets its share of a definite-height table

**Before:** in a table with a definite height, a row holding a cell with `height: 100%` / `50%` was sized like any
other row: the surplus was split in proportion to natural row heights, so the percentage was ignored.

**Now:** per css-tables-3, rows with percentage-height cells are served their share of the table's height first,
capped so the other rows keep their content height; the rest is spread over the remaining rows. A 300px table of
three 50px rows with a `height: 100%` middle cell is now 50 / 200 / 50.

See [Tables](../../docs/html-css-support.md#tables).

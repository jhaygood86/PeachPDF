# A `colspan` cell no longer widens a column the span already fits into

An automatic-layout table sized its columns by dividing a spanning cell's width by its span and
forcing every column it covers to at least that share. CSS 2.1 §17.5.2.2 asks for the opposite
reading: a spanning cell constrains the columns it spans *together* — "increase the minimum widths
of the columns it spans so that together, they are at least as wide as the cell" — so it should not
move them at all when their sum already accommodates it.

The old rule dragged a narrow column up to the span's average whatever its own content needed, and
the surplus then pushed every later column boundary out. A `colspan="2"` cell straddling a
one-character column and a long one moved boundaries that a browser leaves alone.

Such a span now leaves its columns where they are. A span genuinely wider than its columns still
widens them, sharing the shortfall in proportion to what each column already measures.

**What a document author may notice.** Column boundaries in an automatic-layout table containing
`colspan` cells can shift, and in general shift toward what a browser produces. Measured against
Chrome 152 on a three-column table with a spanning row that fits inside its columns' sum, boundaries
in points with the page margin subtracted:

| | second boundary | third boundary |
| --- | --- | --- |
| Chrome 152 | ~7.0 | 197.3 |
| after this change | 7.2 | 195.1 |
| before | 23.3 | 211.2 |

See [Tables](../../docs/html-css-support.md#tables) in `docs/html-css-support.md`.

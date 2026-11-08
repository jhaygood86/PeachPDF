# Table fixes that change rendered output

- A table cell that centres or bottom-aligns its content measures the content from the last in-flow block's margin edge: a `display: none` child with a height, a relative offset of the last child and a negative bottom margin no longer move the content out of its place.
- `max-height` is ignored on a table cell (it used to cap the cell and push centred content above it).
- A table placed left of the page by a negative margin or offset lays its cells out inside its box instead of from x=0.
- A `rowspan` that reaches past the last row of its row group is clamped to the rows that exist; the cell used to be drawn above the page.
- A block-level table after a left float is placed beside the float when it fits, instead of on top of it.
- A table that is a direct child of `<center>` or of an `align="center"` element is centred as a box.
- In a document with no acceptable DOCTYPE (quirks mode), a `<table>` start tag no longer closes an open `<p>`.

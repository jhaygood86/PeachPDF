# In vertical text, `float: left` is the top and `float: right` the bottom, and `clear` works

**Before:** in a `writing-mode: vertical-rl` or `vertical-lr` box, `float: left`/`right` were placed by the physical
left/right rules of horizontal flow: a `float: right` went to the physical right (the block-start side of a
`vertical-rl` box) at an arbitrary height, and `clear` on a block child did nothing.

**Now:** `float: left` and `float: right` (and `clear`, `inline-start`/`inline-end`) are line-relative, as [CSS Writing Modes
4 §7.5](https://www.w3.org/TR/css-writing-modes-4/#text-align) defines them. In `vertical-rl` and `vertical-lr`, whatever
the `direction`, a `float: left` sits at the physical **top** and a `float: right` at the physical **bottom**, at the
block-axis position where it appears. Text in the columns beside a top float starts below it, and beside a bottom float
stops above it. `clear` moves a block past the block-end edge of the floats it clears. A document written for a browser
with vertical text and floats now lays out as it does there; one that relied on the old physical placement moves.

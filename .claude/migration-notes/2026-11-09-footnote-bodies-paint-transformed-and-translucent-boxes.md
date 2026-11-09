# Transformed or translucent boxes inside a footnote body are painted

An `inline-block` or `<img>` with a `transform`, an `opacity` below 1, or a `z-index` on a positioned box inside a
`float: footnote` body used to leave a gap where it should be (its space was reserved but nothing was drawn). It is now
painted, in stacking order within the footnote body.

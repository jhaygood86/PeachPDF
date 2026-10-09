# A transformed inline-level box is painted, and pivoted, where it sits

Before (`v0.9.20` and `main`): an `inline-block` box, or an `<img>` with `display: inline-block`, that has a
`transform` (or an `<img>` with a `transform`) and sits in a line was pivoted around the top-left corner of the page. Depending on the transform it
was drawn off the page (so it was missing, while its space stayed reserved in the line) or displaced up and to
the left. The same applied to a `clip-path` on such a box (resolved against the page origin, so little or nothing
showed), to `perspective()` (warped around the origin) and to the extent of a `filter` or `opacity` group, and
to the same boxes inside a running element or a footnote. An identity transform was not affected.

Now: the box is transformed, clipped and filtered around its own border box in the line, as a block-level box is.
A page whose transformed inline-blocks were silently invisible will now show them.

Also corrected: the pivot of such a box. `transform-origin` percentages and `translate(%)` now resolve against the
box's real border box, so a content-sized inline-block (a text badge) and an `<img>` that keeps the default
`display: inline` rotate and scale about their centre, as in browsers; before, they resolved against a width of
zero, or against the padding and border only.

Visible side effect: a `transform` on a plain non-replaced inline box (`<span>`, `<a>`) used to be drawn off the
page and is now drawn, rotated or scaled in place. Browsers ignore it there (issue #1684).

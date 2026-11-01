# `letter-spacing` accepts a `<percentage>`

**Before:** `letter-spacing: 10%` was an invalid declaration and was dropped, so the text rendered with
normal spacing (the `<length>`/`normal` grammar only).

**Now:** CSS Text 4 allows a `<percentage>`. The spec leaves the basis to the user agent, so PeachPDF resolves
it against the element's own font size (`10%` at a 20pt font is 2pt per gap, the same as `0.1em`). `calc()`
mixing a percentage and a length works too. `DerivedStyle.MeasureLetterSpacing` resolves it lazily at
layout time, so an inherited percentage re-resolves against each descendant's font size rather than being
frozen to an absolute length at the declaring element (the eager `NoEms` path is shared with `word-spacing`
and was left alone).

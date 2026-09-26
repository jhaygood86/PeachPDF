# Face matching: the oblique fall-through order and `oblique 0deg` against upright faces

Two edge cases of `FontResolver.NarrowByStyle`/`NearestOblique` differ from CSS Fonts 4 section 5.2. After the positive oblique ranges the
specification checks italic faces before oblique ranges at or below 0 (for `italic`, italic values at or below 0 first); the engine goes from
the positive oblique ranges to the ones at or below 0, and never reaches the italic faces once an oblique-range face exists. And a request for
`oblique 0deg` should prefer an upright face (oblique 0 in the specification's scale) to an oblique range that only excludes 0; the engine
treats any request with an angle as a request for oblique ranges. Tracked in [#1447](https://github.com/jhaygood86/PeachPDF/issues/1447).

Both are rare: the first needs oblique ranges that lean to the left and an italic face in the same family, the second an explicit
`oblique 0deg`. They were left out of [the change](../recent-fixes/2026-09-26-face-matching-width-style-weight-order-and-fractional-weights.md)
that put width, style and weight in the specification's order so that it stayed a reorder plus the nearest-oblique choice. Fixing them is
adding the italic faces (and for the second the upright ones) as candidates at the right places of the fall-through.

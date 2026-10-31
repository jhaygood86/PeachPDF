# An animation blends `normal` and a `calc()` with a length

`letter-spacing: normal` against a length in a `@keyframes` blends on an HTML box (`from { letter-spacing: normal } to { letter-spacing: 20px }` used to show the first value until the midpoint and the second after; `word-spacing` the same). SVG text already did.

A `calc()` against a length, or against a `calc()` of another shape, blends for every length property instead of flipping at the midpoint: `from { width: 40px } to { width: calc(100px + 2em) }`, and on an SVG `<text>` `from { letter-spacing: normal } to { letter-spacing: calc(10px + 0.5em) }`. A `calc()` against a keyword, a unitless number or a list still flips.

An SVG `<text>` whose own `letter-spacing` or `word-spacing` is not a length (`letter-spacing="x"`) now starts its animation from the inherited value, where it used to flip at the midpoint.

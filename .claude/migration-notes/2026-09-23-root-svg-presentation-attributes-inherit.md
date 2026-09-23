# The root `<svg>`'s own presentation properties are now inherited

**Before:** `fill`, `stroke`, `stroke-width`, `fill-opacity`, `fill-rule`, markers, `direction` and
the other inherited presentation properties set on the **outermost** `<svg>` element — as an
attribute (`<svg fill="#fff">`) or via `style=`/a matched rule — were ignored. Descendants started
from the initial values instead, so an icon whose only colour was `<svg fill="#fff">` rendered
black. Nested `<svg>`/`<g>` elements were unaffected.

**Now:** the root `<svg>` seeds inheritance for the whole tree like any other ancestor, as
[SVG 2 §13.2](https://www.w3.org/TR/SVG2/painting.html#SpecifyingPaint) and CSS inheritance
require. This applies to standalone SVG (`<img>`, `background-image`, `list-style-image`,
`content: url()`) and to inline `<svg>`.

**What an author may need to change:** nothing, unless a document relied on the old behaviour —
e.g. a root `<svg fill="…">` that was never actually intended to paint.

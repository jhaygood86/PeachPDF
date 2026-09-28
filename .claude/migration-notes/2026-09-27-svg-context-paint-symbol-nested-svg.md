# SVG context paint through `<use>` of a `<symbol>`/nested `<svg>` now measures the right box

**Before:** when a `<use>` targeting a `<symbol>` or a nested `<svg>` gave a gradient or pattern `fill`/`stroke`
that content inside the target reached via `context-fill`/`context-stroke`, the paint server was measured against
the *painting shape's own* bounding box instead of the box of what the `<use>` instantiates - the same
`objectBoundingBox`/`userSpaceOnUse` mapping that already worked correctly when the `<use>` targeted a plain shape
or a `<g>` (fixed for that case previously). Rendered output for this specific, narrow combination (gradient/pattern
context paint reaching *into* a `<symbol>`/nested-`<svg>` target) was visibly wrong - typically collapsing to one
end of the gradient, or a distorted pattern tile, rather than spanning the target's actual content.

**Now:** the paint server is measured against the target's own content, mapped through the same viewBox-to-viewport
transform the `<symbol>`/nested `<svg>` establishes when actually painted - matching the plain-shape/`<g>` case and
the spec's "context element keeps its own coordinate space and bounding box" rule (SVG 2, Painting).

A solid `context-fill`/`context-stroke` color was never affected by this (it resolves through a separate,
always-correct inherited-paint mechanism); only a **gradient or pattern** reaching through this specific
`<use>`-of-`<symbol>`/nested-`<svg>` combination changes.

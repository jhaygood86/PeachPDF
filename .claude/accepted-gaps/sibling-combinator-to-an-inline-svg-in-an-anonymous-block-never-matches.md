# A sibling combinator to an inline SVG inside an anonymous block box never matches

Tracked in #2215. Selectors Level 4 (the next-sibling and subsequent-sibling combinators) is the rule it deviates from.

`a + svg .x` does not apply when the `<svg>` sits in an anonymous block wrapper (an inline `<svg>` after a block-level sibling). An inline SVG's element styling is resolved after layout, on the
tree that already has anonymous boxes, so the `<svg>`'s previous sibling is not found among its parent's children. Inline-level siblings (no anonymous wrapper) and flex/grid items match.

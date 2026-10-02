# SVG text now honours font-palette, font-variant-alternates and related font properties

Before: on SVG `<text>`/`<tspan>` (inline or standalone) `font-palette`, `@font-palette-values`,
`font-variant-alternates`, `@font-feature-values`, `font-variant-emoji`, `font-variation-settings` and
`font-optical-sizing` were ignored; `font-weight` was reduced to bold/not-bold and `font-style: oblique
<angle>` was treated as plain oblique.

Now: they apply as they do to HTML text. An inline `<svg>` reads the page's `@font-palette-values` /
`@font-feature-values`; a standalone SVG reads its own `<style>`. `font-weight` is the real numeric weight
(nearest-weight face matching, as in HTML), and `oblique <angle>` drives the faux-italic skew. An SVG that
already declared any of these will now render differently.

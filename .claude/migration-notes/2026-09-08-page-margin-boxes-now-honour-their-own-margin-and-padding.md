# `@page` margin boxes now honour their own `margin` and `padding`

Previously, `margin` and `padding` declared on an `@page` margin box (`@top-center`,
`@bottom-right`, …) were parsed and then ignored: content painted at the edge of the box's whole
slot in the page margin area. Because a text margin box is painted with a single draw into that
rect, `vertical-align` offered exactly three positions inside it and nothing else moved the content
— `line-height` included. On a 1.5in bottom margin those three are 98pt, 49pt and 0pt from the page
edge, so a page number that needs to sit 15pt from the edge (where a browser's print footer lands)
could not be expressed at all.

Both properties now inset the content, per css-page-3 §5.1. Three details are worth knowing, because
each changes the rendered result for a document that already declares these properties:

- A declared `width`/`height` is the **content-box** dimension, with margin and padding additive on
  top of it (css-page-3 §5.3.2/§5.3.3). `@top-center { width: 200pt; padding: 0 5pt }` now occupies
  210pt of the top row and draws its content 200pt wide.
- A **percentage** margin or padding resolves against the box's own containing block, per axis:
  `left`/`right` against its width, `top`/`bottom` against its height (css-page-3 §6, which overrides
  CSS 2.1's uniform width rule in the page context). That containing block (§5.3.1) is the content
  band's width by the page margin's thickness for a top or bottom row, the margin's thickness by the
  content band's height for a left or right column, and the intersection of the two margins for a
  corner. On a 40pt top margin, `padding-top: 5%` is 2pt.
- A **negative margin** is honoured and grows the box past its band. A browser's footer band is an
  overlay over the page rather than a box inside the page margin, so it keeps its inset even on a
  margin thinner than that inset; a negative margin is how a margin box says the same thing. Padding
  may not be negative and is clamped to zero.

`border` on a margin box is still inert — see
[the accepted gap](../accepted-gaps/page-margin-box-border-is-not-painted-or-charged-space.md).

See [Margin boxes](../../docs/html-css-support.md#margin-boxes)
in `docs/html-css-support.md`.

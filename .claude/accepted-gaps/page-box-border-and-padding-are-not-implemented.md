# The `@page` box's own border and padding are not implemented

css-page-3 §3 gives the page box the same box model as any other: margin, then optionally border and
padding, around the page area (the content area proper). PeachPDF resolves `@page`'s `margin`/`size`
(`PageRuleResolver`/`PageGeometryTable`) and, as of the `@page`/margin-box background work, the page
box's own `background`, but never reads `border-*`/`padding-*` declared directly on `@page`.

The concrete, user-visible narrowing this causes: a page-box `background`'s `background-origin`/
`background-clip` always resolve against the full physical sheet, with no border-box/padding-box/
content-box distinction to choose between — unlike an ordinary element's background, and unlike a
page-*margin*-box's background (`MarginBoxRenderer.PaintBackgroundAndBorder`), both of which have a
real border/padding to distinguish those three areas.

Left out deliberately: the `@page`-background work this gap was found alongside only asked for
background support, and adding the page box's own border/padding is a materially larger change — new
per-page geometry (an inset between the margin area and the page's own content area, which the layout
pipeline doesn't currently model at all) plus a *fourth* paint layer per css-page-3 §3.1's own order
(page background < document canvas < **page borders** < document contents < page-margin boxes) — a
different layer from a page-*margin*-box's border, which already exists and sits elsewhere in that
same order (at the very end, with the other margin-box content).

Tracked as [#1147](https://github.com/jhaygood86/PeachPDF/issues/1147), which carries a suggested
shape. The reader-facing note is the page-box-background paragraph in
[docs/html-css-support.md](../../docs/html-css-support.md)'s `@page` rule section, which says
`background-origin`/`-clip` have no effect there without citing the issue.

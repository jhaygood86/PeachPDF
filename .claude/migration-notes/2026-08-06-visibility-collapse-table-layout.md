# `visibility: collapse` now collapses table rows/columns instead of rendering as `hidden`

**Landed:** 2026-08-06 — Implement table row/column collapse layout for `visibility: collapse` (#639)
**Doc section:** docs/html-css-support.md § [visibility row](../../docs/html-css-support.md)

Since the typed-storage conversion recorded in
[2026-08-04-visibility-collapse-now-accepted.md](2026-08-04-visibility-collapse-now-accepted.md),
`visibility: collapse` was accepted as a value but rendered identically to `visibility: hidden`
everywhere — every downstream layout/paint check only distinguished `visible` from "anything else",
so a collapsed table row/column still reserved its layout space.

`CssLayoutEngineTable` now implements CSS 2.1
[§17.6.1](https://www.w3.org/TR/CSS21/tables.html#dynamic-effects)'s table-specific meaning: a `<tr>`
(or every row inside a `<thead>`/`<tbody>`/`<tfoot>` marked `collapse` — `visibility` is inherited, so
the group's value already reaches its rows without special-casing the group) is removed from the row
loop entirely, taking no height, with the rows after it shifting up to fill the gap. A `<col>`/
`<colgroup>` marked `collapse` has its column's width *and* its own `border-spacing` slot zeroed
after the rest of the table's width algorithm has run, so the columns after it shift left with no
residual gap and the table itself shrinks by exactly that column's width. A document that relied on
a collapsed row/column still reserving its space (the previous,
`hidden`-equivalent behavior) will now see that space reclaimed instead — matching every mainstream
browser's table layout.

`visibility: hidden` is unaffected: it still reserves the element's layout space everywhere, table
rows/columns included, and only omits painting it.

Two narrower cases remain unhandled and are not part of this change: a `rowspan`/`colspan` cell that
spans across a collapsed row/column may size or align incorrectly (the row/column index shift a
collapsed row/column causes is not reconciled against a span crossing it), and a collapsed column's
own cell content can still influence the width the engine settles on for its column before the width
is zeroed, and through `colspan`, its neighbors' widths — because the width-by-content passes
(`GetColumnsMinMaxWidthByContent`/`GetColumnMinWidths`) do not know a column is about to collapse.
Both are pre-existing limitations of this narrower scope, not regressions from a prior release.

# `border-radius` is ignored on a `border-collapse: collapse` table

`BordersDrawHandler.PaintCollapsedTableBorders`'s draw path (via `DrawCollapsedSegment`) never receives a
rounded-corner box - a collapsed table's borders paint as straight per-grid-line segments, each butting at
its intersections rather than mitring into a rounded corner. `border-radius` set on the table or any cell
has no visible effect on a collapsed table's border painting.

This is not a spec deviation: CSS 2.1 [§17.6.2](https://www.w3.org/TR/CSS21/tables.html#border-conflict-resolution)
resolves collapsed borders as independent per-grid-line segments and defines no interaction with
`border-radius` at all, and neither Backgrounds and Borders §5.5's own `border-radius` text nor any later
module fills the gap - the corner-clipping model both specs describe is inherently a single four-sided box's
own border box, which a collapsed table's borders (one independently-resolved segment per shared grid line,
not one border per box) do not have. Chromium and Firefox both ignore `border-radius` on a collapsed table
for the same reason, so this matches real UA behavior rather than deviating from it - no tracked issue filed
per this repo's convention, since there is no spec text to be out of compliance with.

**Deliberately out of scope** of the CSS 2.1 §17.6.2 border-conflict-resolution work
([issue #735](https://github.com/jhaygood86/PeachPDF/issues/735)'s fix): implementing rounded corners for a
collapsed table's resolved-segment model would mean synthesizing a corner arc from up to four independently
resolved, possibly differently-styled/colored/widthed segments meeting at one grid-line intersection - a
different and substantially harder problem than any single box's own `border-radius`, and not something any
real browser attempts either.

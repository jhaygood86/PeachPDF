# `rowspan` cells now keep column alignment in the last column and close correctly beside a slow-resolving sibling

**Landed:** 2026-08-06 — Fix open alignment issues (#522, #593)
**Doc section:** docs/html-css-support.md — `td`/`th` `rowspan` is documented as "fully supported"; these
fixes close two gaps in that support rather than changing the documented claim.

Two related `CssLayoutEngineTable` fixes, both about a `rowspan` cell losing column/row alignment:

**#522 — a `rowspan` in the table's last column got no placeholder in the rows it spans.**
`InsertEmptyBoxes` only ever compared its running column count against a later row's *existing* cells to
find where to insert a `CssSpacingBox` placeholder, so a rowspan whose column sat at or past the last of
those cells — the common case: the table's last column — fell through with nothing inserted. A table with
a trailing-column `rowspan` now gets a real placeholder in every row the span covers, which corrects that
row's own column count/width bookkeeping (`CalculateCountAndWidth`) for tables where this previously
undercounted.

**#593 — a `rowspan` cell finished by an unrelated sibling's resumption never closed at its ending row.**
`TableRowCursor._carriedFinished` is seeded once, when a resumed pass re-enters the row that opened a
rowspan, and was never cleared for the rest of that pass. A short rowspan cell opened alongside a sibling
that itself needed many page-break resumption passes read as permanently "finished" for the whole
remainder of that pass — including at the row that should actually close it — so `CloseSpanningCell` was
never entered for it at all, and the cell's box stayed at its own bare, tiny content height instead of
growing to meet the row ending its span.

A document with a `rowspan` cell in a table's last column, or a `rowspan` cell beside sibling content that
takes several page breaks to finish, will now render with the `rowspan` cell's box correctly sized/aligned
to the row that ends its span, instead of a visibly truncated or misaligned cell.

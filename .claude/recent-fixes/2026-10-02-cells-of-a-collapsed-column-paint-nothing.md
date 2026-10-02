# Cells of a `visibility: collapse` column paint nothing

**Symptom:** in the `table_visibility_collapse` showcase the body cell of the collapsed middle column ("Collapsed") was still drawn, on
top of the cell that had closed the gap ("Follows Column A directly"); its header cell was not drawn. Present in v0.9.20; the
`<col>` width change made it overlap more because the neighbouring cell now wrapped to two lines under it.

**Cause:** `Visibility.Collapse` on a `<col>` removes the column from layout (`IsColumnCollapsed`: zero width, no spacing), but a cell
does not inherit its column's visibility, so the painter's `box.Visibility != Visible` early-out never fired for it. A header cell
happened not to show because its text fits inside the `overflow:hidden` clip of a zero-width cell, the body text did not.

**Fix:** the row loop marks a cell placed in a collapsed column `visibility: collapse`, which the painter already honours for the
whole box and its content (a collapsed *row* got this through inheritance).

**Evidence:** `CollapsedColumnPaintTests` (fails without it); full suite passes; of the 201 showcases only
`table_visibility_collapse` changes, and only by the overprinted text going away.

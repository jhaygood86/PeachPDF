# A `<tbody>`-opened rowspan reaching into `<tfoot>` under-reserves the footer's own column

Tracking issue: [#792](https://github.com/jhaygood86/PeachPDF/issues/792).

Found while implementing [#788](https://github.com/jhaygood86/PeachPDF/issues/788) (a `<thead>`/`<tfoot>`-opened
`rowspan` crossing into `<tbody>`) and verifying that a `<tfoot>`-opened span crossing into `<tbody>` is
structurally unreachable - `_allRows` always places footer rows as its trailing entries regardless of
source order, so nothing ever follows a footer row in the grid for its own span to reach into.

A *mirror-image* gap surfaced during that verification: a `<tbody>`-opened `rowspan` reaching **into**
`<tfoot>` hits the identical clamp from the other side. `GetLastRowInGrid`
(`src/PeachPDF/Html/Core/Dom/CssLayoutEngineTable.cs`) routes a body-starting row through the body-scoped
`GetEffectiveEndRowIndex(bodyIndex, rowSpan)` overload, whose internal walk is bounded by
`_bodyRows.Count` - so `TableGrid.ComputeColumnPlacements` never reserves the footer's own column for
such a cell. The opposite symptom from #788's own phantom-gap: instead of an unfilled gap, the footer's
own real cell in that column is never shifted out of the way, and silently overlaps/collides with the
spanning cell's continuation instead.

Not attempted as part of #788's own fix - different code path (`GetLastRowInGrid`'s body branch, not its
header branch, which #788 did fix for the header-crossing-into-body case), opposite direction
(under-reservation vs. #788's phantom gap), needing its own dedicated fix: extending body-row end-row
computation to look past `_bodyRows.Count` into the footer's own row-space, a body-cell-crossing-into-footer
analogue of `ComputeHeaderRowSpansCrossingIntoBody`/`SeedCrossBoundaryRowSpans`, and `InsertEmptyBoxes`
placing continuation placeholders into the footer's own rows.

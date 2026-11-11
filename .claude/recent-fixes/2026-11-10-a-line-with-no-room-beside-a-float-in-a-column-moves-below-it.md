# A line with no room beside its own container's float moves below it, inside a column too (#2316)

Workstream E of the float issues plan, first item. In `columns: 3`, a paragraph after a float that fills its column and is taller than the page lost its words: they were drawn at x = the content edge (280pt), clipped, so no page showed them.

**Cause.** `ShiftEmptyLineBelowCrowdingFloats` moves a line that has no room beside floats below them (CSS 2.1 §9.5). When the float ends in a later fragmentainer, the shift is allowed past the band (the line then belongs to that later fragmentainer, css-break-3 §4.4). That allowance was off for every line inside a column (`!inColumn`), on the reasoning that a line deferred out of a column fill takes the container's carry with it. The refusal branch then put the line back beside the float - at the float's right edge, with no width.

**Change.** The allowance now also holds inside a column for floats of the line's own multi-column container (`ColumnsContainerOf(float) == ColumnsContainerOf(blockBox)`); floats of a nested or outer container keep the old refusal (the "shifting the lines past it doubled words there" note in the code). Outside columns it is unchanged (floats outside every container).

**Found by running it.** The full engine suite (19,391) passed with the one-condition change: no existing row depended on the refusal for own-container floats. The document renders its four words once, the paragraph in column 3 beside nothing.

**Separate defect seen, not fixed.** In this document the float's text is drawn one column to the left of the float's box (box in column 2, text in column 1, 27pt lower), before and after this change. The float was moved to the next column ("a float that does not fit its column moves to the next") without its content following. Words are visible, so the loss tests do not see it. Needs its own issue and a Chrome comparison (Chrome puts the float in column 1).

**Evidence.** `MulticolFloatIssueDocumentsTests` row 2316-1 is enabled; engine suite 19,382 (Release) and `PeachPDF.Tests` 1,807 pass; zero warnings; the PDF rasterized through MuPDF.

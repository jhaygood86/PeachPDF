# A float wider than its column moves the lines beside it below it (#1554)

A `float: left` of 146pt in a 69pt column of a multi-column container nested in another one lost 8 of the 24
words of the paragraph after it. The words were in no fragment, not clipped.

**Cause.** The float leaves no room beside it in the column, so the lines next to it were placed to its right, past
the column's own inline extent. Columns are told apart by the inline axis (a column's captured instance covers
`[columnInlineLeft, columnInlineLeft + columnWidth]`), so a line outside every column's extent is claimed by none of
them and is dropped. CSS 2.1 §9.5 says what should happen: a line box shortened to nothing is shifted down until
content fits.

**Fix.** `ShiftEmptyLineBelowCrowdingFloats` (the line shift of #1531), which was skipped inside columns, now runs
there, but only for a float that reaches into the column being filled (`InThisColumn`: its margin box overlaps the
line's `[ContentLeft, ContentRight]`). That distinction is the whole point of the earlier skip: the shift looped
forever when a float that sits in one column was heeded by every other column's first line. A right float in column 1
does not overlap column 2's span, so it is ignored there, and the loop does not come back
(`TallFloatInAColumn_DoesNotSendLayoutIntoALoop` still passes).

**Not done.** The single-level case works (it did before: the float's own column is wide enough). A float that is
wider than the *whole container* still overflows it to the right, which is a separate matter (see the accepted gap on
a float that does not fit in its column).

Evidence: `MulticolContentLossTests.FloatWiderThanItsColumn_InNestedColumns_KeepsEveryWord` (146pt nested fails
before: 8 words; 146pt single level and 40pt nested as controls); full net8.0 suite passes. On 300 generated documents
against the #1531 branch: 11 improve and 7 get worse. Only seed 33 was examined: it loses words in a block that hits the
single-column bug fixed by #1538, which this branch does not yet include. The other six (33 aside: 112, 119, 162, 189,
228, 262) are not reduced, so a defect in this change cannot be ruled out for them.

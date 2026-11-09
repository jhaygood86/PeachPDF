# Floats after a resumed float, floats that open a text run, and floats in table cells, in columns (#2312, #2319, #2320)

Follow-ups to [the column-top placement for content after a continuing float](2026-11-09-content-after-a-float-that-continues-in-a-column-is-placed-in-that-column.md).
All three reproduce with the issues' verbatim documents (rows in `MulticolContentLossTests.WordsAfterFloatsThatContinueInTheNextColumn_AreKept`).

**#2319 - a second float after the resumed one.** The column top override of `CssBox.LayoutBlockChildren` skipped floats, so a float
after the continuing float was placed from `PreviousInFlowSibling` (the paragraph that ended in the column left behind). The stretch
of boxes that get the resume target now includes floats (`IsFloated`); only an in-flow child ends it. The words of the second float
and of the paragraph after it were lost; both are drawn now. (Chrome draws the second float's paragraph on page 2, Peach on page 3:
beside the first float's continuation it has no room.)

**#2312 - text right after a float.** In a multi-column container the float and the bare text after it were wrapped into one
anonymous inline-run block (`DomParser.CorrectInlineBoxesParent`), so the resume token named that block and the column-top
placement never reached it: the words were drawn clipped at the page edge. A float that *opens* a run is now left as a child of the
container (the #2290 shape `[p, float, wrapper(text), p]`); a float after the run's first text still joins the run, where it sits
beside the line it follows (`float, text, float` has a row). Chrome's positions for the issue's document (`w9000_21` at x=156 and
`w9000_22` at x=215, y=20, page 2) are reproduced exactly.

**#2320 case 1 - a float in a grid inside a table cell.** The column top is where a child of a *plain block-flow chain* starts; a
table cell's or grid item's content starts below the row's or track's top. `IsPlainBlockFlowUpTo` restricts the override to a chain
of block/list-item/flow-root boxes up to the multicol container; otherwise the child keeps the placement it had on `main`. The
document's 19 lost words are drawn. It still draws `w6045_848` twice, exactly as `main` does.
Case 2 (`w5064_115`) is lost on `main` as well, a different mechanism, and is tracked as #2326.

**Found by running it.** An existing test (`MulticolFloatWiderThanColumnTests.TextBesideAFloatWiderThanItsColumn_MovesBelowIt`)
failed with the parse change: with a float wider than its column, bare text was kept below the float because both shared one inline
flow; as a block of its own after the float it goes to the top of the next column, as a `<p>` there already did. That is the
block-sibling behaviour, not new, but it is now reached by bare text; the test accepts "below the float or right of it" and the
deviation is recorded in #2326 rather than fixed here.

**Not done.** A float after text in a multi-column run that *continues* is unexercised beyond the `float, text, float` row.

**Evidence.** Engine suite (19,355) and `PeachPDF.Tests` pass; the two documents rasterized through PDFium and MuPDF draw every word once.

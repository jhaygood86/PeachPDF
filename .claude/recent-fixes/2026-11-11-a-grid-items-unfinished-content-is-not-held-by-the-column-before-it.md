# The content of a grid or flex item that continues is not held by the column before it (#2320 case 1)

A float inside a grid inside a table cell inside `columns: 3` drew the paragraph after the float twice: its words on two pages (`w6045_848`), and, with a background on it, a rectangle a thousand points tall on two more. `main` did the same; it was recorded as a separate defect when the lost words of the same document were fixed.

**Cause.** `CssLayoutEngineColumns.AddBoxesBeyond` is what says which boxes a column does not hold, read off the break record it stopped at: it follows block records, and a table's unfinished cells, and stopped at anything else (`default: return`). A table cell's record continues into the grid inside it, whose record is a `GridBreakToken`, so the grid item that carries on to the next page was not marked as beyond the column. The column's geometry snapshot kept it, with the position the page before gave it, and the emitter drew the paragraph from that snapshot as well as from the page that really holds it.

**Change.** The default case follows `BreakToken.FanOutContinuations`, the per-item records a grid, flex container or table already exposes, so an item's continuation is walked like a table cell's.

**Found by running it.** Logging the emitter's claim for the word showed three snapshots holding it (slots 0, 1, 2) at different positions, the first two with bands over a thousand points tall.

**A test that passed on the broken output.** `MulticolFloatContinuationPdfTests` had a reduced variant of this document and asserted that the paragraph's red background was painted on exactly one page. In that variant the paragraph is at x = 381 on a 300pt page (never visible), and the one painted background was the thousand-point rectangle on page 0; the fix removed it, so the test failed, and it was the test that was wrong. It now uses the issue's own document: the background is painted on page 2 only, and 76 x 22pt (checked in the PDF through MuPDF: before, rectangles on pages 0, 1 and 2, the first two 1,000pt tall).

**Evidence.** All rows of `MulticolFloatIssueDocumentsTests` are enabled and pass (none skipped); the old `-doubles` allowance in `MulticolContentLossTests` is gone. Release engine suite 19,384, `PeachPDF.Tests` 1,807, zero warnings.

# A line beside a float that starts right of it, and a split rowspan cell, keep their words (#2289, #2314)

Two unrelated content-loss defects fixed together; each drew a word on no page.

## #2289: the line lookup only saw a left float that covered the cursor

`DomUtils.GetLastLeftIntersectingFloatBox` chains left floats from the line's start: each hop asks for a float whose span covers the cursor's x
(`targetRight > left && targetLeft <= left`). With two floats side by side (60% + 40%) and the first ended on page 1, page 2 has only the second,
which starts at x=60%. Nothing covers x=0, so the lines of the paragraph after it ran the full width over the float's words and the last word was laid
out where the emitter did not claim it.

Fix: `CssFloatCoordinates.SeekPastGap`, set only for the first hop while the cursor is still at `Line.ContentLeft`. With it the Left case also accepts a
left float whose vertical extent overlaps the line and whose right edge is past the cursor, whether or not it starts at the cursor. After the first
hop the old rule applies, so a chain of floats and every cursor that is not at the line start behave as before (RTL, vertical modes, lines already
pushed past a float).

Spec and engines: CSS 2.1 §9.5 shortens a line box beside a float to the float's margin box whenever their vertical extents overlap, wherever the float
starts. Chromium (`ExclusionSpace` layout opportunities), WebKit (`logicalLeftOffsetForLine` over all overlapping floats) and Gecko
(`nsFloatManager::GetFlowArea`) all bound the line by every overlapping float. Peach's chain-from-the-cursor lookup was the deviation; the gate to the
line start is conservative and a deliberate remainder (the general rule would also need the right-float walk and the shape-outside path reviewed).

## #2314: a split rowspan cell was centred/bottom-aligned against room it does not have

`CssLayoutEngine.ApplyCellVerticalAlignment` computes free space from the cell's far edge (the rows it spans). Under a repeated `<thead>`/`<tfoot>` that
extent includes the room the group takes on every page, so a cell with content taller than a page got a phantom `dist` (24pt on the fixture) and its
lines were pushed past the strip they were laid out in; the text after the page-tall block was drawn on no page. `RoomAboveTheNextFoot` clamps by the raw
page foot and ignores the repeated group's room, so it did not catch it.

Fix: a cell whose measured content ends past the foot of the page band it starts in is split and has no free room to distribute, so alignment is
skipped (top). css-tables-3 §6.1 gives a fragmented cell no extent of its own beyond its content; Gecko forces top alignment for a split cell
(`nsTableRowFrame::ReflowCellFrame`, read from source). Chromium is reported to do the same, not verified from source here. A cell whose content ends
inside its band keeps the old alignment and clamp (`AMiddleAlignedRowspanCellThatFitsItsBand_StillCentresItsContent`).

Not done: the fix lives in `ApplyCellVerticalAlignment`, not `CloseSpanningCell`, so it does not touch the hunk open PR #1839 edits there.

## Evidence

`FloatAndRowspanContentLossTests` (8 loss cases fail without the change, pass with it; plus the still-centres control). Full `PeachBrackets.Engine.Tests`
(19353) and `PeachPDF.Tests` (1801) pass on net10.0. The issues' own documents were rendered through the CLI and every word extracted once with PyMuPDF.

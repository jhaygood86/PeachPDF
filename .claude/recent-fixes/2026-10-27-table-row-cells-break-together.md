# A table row's cells break together at the page foot (#1484)

Two independent causes made the row's second cell sit at the foot with its text cut while the first cell moved on.

1. **A keep-with-next run headed by the first content of a cell was moved to the next page**
   (`EarlyBreak.TravellingRun`). The cell's `h3`+`h3` chain (headings carry `break-after: avoid`) started below the
   page top only by the table border/padding/margin, so the run guard ("starts below the page being left") let it
   travel, leaving cell 1 empty on page 1 while cell 2 stayed - and, being taller than a page, cell 1 then needed
   three pages. A table cell is a parallel flow with no break point before it (css-break-3 §4.1), so the run is now
   trimmed from the front past a member that is the first in-flow child of a cell (`BeginsATableCell`); the break is
   taken between the remaining members or on the subject alone (the `RunDropped`/`RunTrimmed` rungs).
2. **A short cell was centred in the whole height of a row taller than its band.** The row is laid out continuously
   and each page shows a strip, so `vertical-align: middle` put the content wherever the row's middle landed -
   measured 222..233 against a foot at 230, one line cut by the page. `AlignmentBottomFor` aligns a finished cell
   within the band's remainder (less a repeated footer) when its content fits there, so it stays whole beside the
   first fragment of the row. This is not Chrome's placement (Chrome centres in the whole row and pushes the
   straddling line to the next page); it keeps the content whole without needing a per-line relocation in an
   alignment offset, which does not exist yet.

**Found by running it, not reading it.** `stoppedCells` is empty for this row: it is not one of the rows whose cells
stop at the foot, it is a continuously laid out tall row, so the "a continuing row overfills its fragment" reasoning
in the vertical-alignment loop does not apply and the fix could not be keyed on it.

**Not done.** A multi-line cell centred in a tall row can still have one line straddle a later boundary; that needs
the alignment offset to be nudged by up to a line height per boundary.

**Evidence.** `TableRowCellsBreakTogetherTests` (every word inside its page; both cells' words begin on page 1);
Engine.Tests 16477 and PeachPDF.Tests 1499 in Release; the issue's document rasterized with PDFium and MuPDF
(2 pages, was 3, nothing cut).

# A nested table's row honours `break-inside: avoid` (#1862)

`CssLayoutEngineTable.AvoidedBreakCorrectionAppliesTo` declined any table inside a table cell, because moving a
nested row used to start it at the real page top while the enclosing table's sliced-row model displaced it
under the repeated header, drawing the row's last line on two pages (#1795).

With the nested table breaking in the sliced row's run frame (see
[the #1795 note](2026-10-27-nested-table-breaks-in-the-sliced-rows-run-frame.md)) the decline is simply
removed: a moved row begins at the next strip, which is below the enclosing header. Nothing else was needed -
the repro passes with the one-line lift, and `IsInsideATableCell` had no other caller, so it is gone.

**Not part of this change.** On the issue's document the nested table's vertical cell borders poke up through
the repeated `HDR0` cell on page 2. That is present on the unmodified build too (checked by rendering both),
so it is a separate collapsed-border-segment clipping defect, not something this lift introduces.

**Evidence.** `NestedTableAvoidedRowBreakTests` (all 19 lines of the avoided row on one page, each once);
Engine.Tests 16476 and PeachPDF.Tests 1499 in Release; the issue's document rasterized with PDFium and MuPDF
(the row is whole on page 2, the header repeats).

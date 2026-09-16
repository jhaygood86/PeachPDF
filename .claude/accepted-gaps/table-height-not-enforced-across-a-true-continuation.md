# A table's explicit height/min-height is not enforced when its row loop must continue into a later top-level pass

[Issue #1116](https://github.com/jhaygood86/PeachPDF/issues/1116)'s measure-then-redo mechanism
(`CssLayoutEngineTable.PerformLayout`: lay the table out once, then again with each row's proportional
share of any shortfall fed back in) is gated on the table's row loop completing entirely within its
first top-level layout pass — `resume is null` on entry, and `tableBox.PendingBreakToken is null` after
`Layout()` returns. A table so large that a single cell's own content must itself continue into a
*separate, later* top-level pass (a true `TableContinuation`) gets no height enforcement at all.

The redistribution surplus is computed from the table's *total* natural row-axis extent, which isn't
known until the *last* of the table's top-level passes completes - by then the rows from earlier passes
are already committed/painted on earlier pages and cannot be redone (the redo mechanism works by laying
the whole table out again from markup, which would duplicate or overwrite already-emitted content).

This is narrow: ordinary multi-page tables, which take per-row page breaks *inside* one
`LayoutBodyRows` call (the common way a table spans pages), are unaffected and are fully handled,
including their own per-row pagination decisions being correctly re-evaluated against the grown rows on
the redo pass. Only a table whose single cell's content alone needs a third fragmentainer hits this gap.
Filed as [issue #1132](https://github.com/jhaygood86/PeachPDF/issues/1132).

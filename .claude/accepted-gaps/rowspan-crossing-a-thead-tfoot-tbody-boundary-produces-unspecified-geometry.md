# A rowspan cell spanning from `<thead>`/`<tfoot>` into `<tbody>` produces unspecified geometry

Tracking issue: [#788](https://github.com/jhaygood86/PeachPDF/issues/788).

Found during a post-change review pass of issue #784. `TableGrid`/column-placement (`AssignBoxKinds`'s
combined `_allRows`, `GetLastRowInGrid`'s unclamped header-row arithmetic) genuinely treat a
`<thead>`/`<tfoot>`-opened rowspan as reaching into `<tbody>`, reserving that column there and shifting
the tbody row's own cell sideways to make room. But `DetachAndMeasureRepeatedRowGroups`'s own
rowspan-closing bookkeeping (`headerSpanningCellsEndingOnRow`/`footerSpanningCellsEndingOnRow`,
`GetEffectiveEndRowIndex` capped at the group's own row count) is entirely disjoint from the body's own
(`cursor.RowSpannedBoxes`), so such a cell is silently coerced to close at the group's own last row -
never reaching the body row the grid layer already reserved a column for. The result is a phantom,
unfilled gap in the tbody row: no cell paints there and no `CssSpacingBox` placeholder stands in for it
either, since `InsertEmptyBoxes` only ever iterates `_bodyRows`.

This doesn't crash (an existing fix already clamps `TableGrid.Build`'s own row-count arithmetic against
the whole table rather than one group), but produces geometry the suite's own
`TableLayout_RowspanExceedingTheadsOwnRowCount_DoesNotThrow` test already documents as unspecified in
its own doc comment ("the exact geometry a malformed rowspan this large produces isn't otherwise
specified").

Not attempted as part of #784's own narrower fix (a rowspan cell *entirely contained within* a multi-row
header/footer group, which only needed one existing scan widened). A real fix for the cross-boundary
case would need the group-local closing bookkeeping to stop clamping at the group's own last row and
defer actually closing such a cell until the real body row it ends on has laid out - on an entirely
separate pass and cursor, potentially on a different page - plus a way for `InsertEmptyBoxes` to place a
continuation placeholder in `_bodyRows` for a cell that isn't itself part of it, plus reconciling this
with header/footer repetition at all (what does "the header's rowspan continues into the body" mean on
a later page, where a fresh header proxy repeats?). Real browsers' own behavior spanning a `rowspan`
across this boundary is itself inconsistent/questionable, which may make this worth keeping as a
documented, permanent gap rather than implementing.

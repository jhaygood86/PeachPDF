# `visibility: collapse` table layout: rowspan-across-collapsed-row and collapsed-column width leakage closed

Closes [#665](https://github.com/jhaygood86/PeachPDF/issues/665), the follow-up to
[#639](https://github.com/jhaygood86/PeachPDF/issues/639) that implemented CSS 2.1 §17.6.1 table
`visibility: collapse`. Both gaps recorded in
`.claude/accepted-gaps/visibility-collapse-table-layout-residual-gaps.md` (now deleted, both closed)
are fixed in `CssLayoutEngineTable`.

A third, narrower gap both #639 and this change deliberately leave open - a `colspan` cell
straddling a collapsed column together with a visible one, for both border-spacing (#639) and
content-based width (this change) - is now tracked on its own as
[#667](https://github.com/jhaygood86/PeachPDF/issues/667) and recorded in
`.claude/accepted-gaps/visibility-collapse-table-straddling-column-cell.md`, since it does not
expire the way this file does.

## Gap 1: a rowspan/colspan cell crossing a collapsed row

`InsertEmptyBoxes` walked `currentRow + 1 .. currentRow + rowSpan - 1` as indices into `_bodyRows` -
but `_bodyRows` already has collapsed rows filtered out, while `rowSpan` is still the raw markup
count (which includes any collapsed rows in its range). A span that opened before a collapsed row and
reached into or past it therefore walked the same number of *filtered-list* steps as its raw span
count, landing its `CssSpacingBox` placeholder one row too far for every collapsed row it crossed -
silently misaligning that column's grid for every row after the misplaced placeholder.

The fix adds `_bodyRowOriginalIndices`, built alongside `_bodyRows` in `AssignBoxKinds`: for each row
kept in `_bodyRows`, its ordinal position among the table's rows in *source* order, collapsed rows
counted too. `GetEffectiveEndRowIndex(startRowIndex, rowSpan)` uses the pair to translate a cell's raw
rowspan into the correct filtered-list index: it computes the target original index
(`_bodyRowOriginalIndices[startRowIndex] + rowSpan - 1`) and walks `_bodyRows` forward only as long as
each row's own original index is still within that range. `InsertEmptyBoxes` uses this to bound its
placeholder loop, `CssSpacingBox`'s `EndRow` is now passed in directly (computed once, by this same
helper) rather than re-derived from the raw `rowspan` attribute a second time, and
`LayoutBodyRow`'s own `rowSpannedBoxes` bookkeeping (which closes a spanning cell's vertical alignment)
calls the same helper - so a span's start, its placeholder, and its own close all agree on where it
ends.

**Trap found by running it, not by reading it:** `TableRowCursor.RowIndex` is `-1` while a
`<thead>`/`<tfoot>` group is being measured on its own isolated cursor
(`DetachAndMeasureRepeatedRowGroups` never sets `RowIndex` for those rows - it stays at the type's own
documented sentinel). `GetEffectiveEndRowIndex(-1, rowSpan)` initially indexed
`_bodyRowOriginalIndices[-1]` directly and threw `ArgumentOutOfRangeException` - caught by
`TableWithRowspan_ProxiesLayoutCorrectly` and two others in `TableHeaderRepetitionIntegrationTests`,
all tables with a rowspan cell inside a *detached* header/footer (not the `_bodyRows` case gap 1
targets at all). The fix treats `startRowIndex < 0` as its own case and falls back to the pre-existing
raw arithmetic (`startRowIndex + rowSpan - 1`) - correct for that path since a header/footer
measurement's own rows were never part of `_bodyRows`'s numbering, collapsed or not.

## Gap 2: a collapsed column's own content pushing its pre-collapse width up

`GetColumnsMinMaxWidthByContent` and `GetColumnMinWidths` measured every cell's content-based
min/max width including cells confined entirely to a collapsed column, before `CollapseColumnWidths`
(which runs last, by design) zeroes that column. The already-existing `CellOccupiesOnlyCollapsedColumns`
helper (added by #639 for the border-spacing fix) is reused to skip such a cell in both methods -
matching the same "straddling a collapsed and a visible column is still the accepted, narrower gap"
boundary #639 already drew.

**Measured, not assumed:** the naive repro (an explicit-width table, wide unbreakable word in the
collapsed column, assert the neighbor column's width) passed even *without* the fix - explained by
`EnforceMinimumSize`'s own neighbor-narrowing loop only firing when its two different min-width
metrics (`GetColumnsMinMaxWidthByContent`'s and `GetColumnMinWidths`'s, computed by different
functions - `CssBox.GetMinMaxWidth`'s minWidth vs `CssBox.GetMinimumWidth()`) actually disagree, which
they don't for simple single-word content, and separately because a wide-open available width let
`Math.Min(..., maxFullWidths[i])` cap the affected column back to its natural max regardless. The gap
is real but only actually starves a neighbor column in an **auto-width** table
(`CssLayoutEngineTable.DetermineMissingColumnWidths`'s auto branch, which spreads genuinely scarce
leftover width across columns) - confirmed by toggling the fix off and rerunning
`CollapsedColumn_WideContentDoesNotStarveNextColumn` (in `TableVisibilityCollapseIntegrationTests.cs`),
which fails without the skip (15.9pt vs the correct/control 34.8pt - forced to wrap for lack of space)
and passes with it.

## Evidence

`dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` - full suite, 8177 passed, 0
failed. `diff-cover` against `origin/main`: 100% diff coverage. `dotnet build PeachPDF.slnx -t:Rebuild`:
0 warnings.

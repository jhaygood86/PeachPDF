# `visibility: collapse` table layout: straddling colspan cell gap closed

Closes [#667](https://github.com/jhaygood86/PeachPDF/issues/667), the narrower follow-up gap
[#639](https://github.com/jhaygood86/PeachPDF/issues/639) and
[#665](https://github.com/jhaygood86/PeachPDF/issues/665) both deliberately left open: a `colspan`
cell that straddles a collapsed column together with a visible one. Both of those fixes gated on
`CellOccupiesOnlyCollapsedColumns`, a binary all-or-nothing check - a straddling cell got neither
treatment. `.claude/accepted-gaps/visibility-collapse-table-straddling-column-cell.md` is deleted,
the gap it tracked is closed.

## The underlying model

`GetWidthSum` already established the governing rule: the table loses exactly one border-spacing
slot per collapsed column, and (by the pre-existing `LayoutBodyRow` skip for a cell entirely inside
collapsed columns) that slot is specifically the one **immediately after** the collapsed column - not
the one before it. A new helper, `GetInteriorSpacing(columnIndex, colspan)`, applies that same rule to
the boundaries *inside* a single cell's own span (sums `GetHorizontalSpacing()` for each boundary
whose left-hand column isn't collapsed), so it's now shared by both the width-summing side
(`GetCellWidth`) and the min-width side (`GetColumnMinWidths`'s `spannedWidth`) instead of both using
a blanket `(colspan - 1) * GetHorizontalSpacing()` that didn't know a collapsed column could be in the
middle.

Three call sites needed the asymmetric treatment:

1. **`LayoutBodyRow`'s trailing border-spacing.** Was gated on `CellOccupiesOnlyCollapsedColumns`
   (true only if *every* spanned column is collapsed). Now gated on `IsColumnCollapsed(columnIndex +
   colspan - 1)` - the cell's own *last* spanned column - which subsumes the old fully-collapsed case
   and additionally omits the slot when a straddling cell's span ends on a collapsed column.
2. **`GetCellWidth`'s interior spacing.** Switched from `(colspan - 1) * GetHorizontalSpacing()` to
   `GetInteriorSpacing`, so a cell straddling a *leading* collapsed column (span order
   `[collapsed, visible]`) doesn't carry a spacing unit for a boundary that's supposed to be
   omitted (the boundary immediately after that leading collapsed column, which happens to fall
   inside this cell's own span rather than between two different cells).
3. **`GetColumnsMinMaxWidthByContent`/`GetColumnMinWidths`'s width apportionment.** Both already
   skipped a cell confined *entirely* to collapsed columns; a straddling cell wasn't skipped but still
   divided its content-based min/max width evenly across every spanned column, including the collapsed
   one that will never carry any of it - understating the visible column's fair share. Now divides by
   the count of *visible* spanned columns only, and only assigns the result to those. `GetColumnMinWidths`
   had the same issue in its `affectColumn` choice (always the span's literal last column, even if
   collapsed) - it now walks back to the last *visible* column in the span, which
   `CellOccupiesOnlyCollapsedColumns`'s prior skip guarantees exists.

## Evidence

Three new tests in `TableVisibilityCollapseIntegrationTests.cs`, one per call site above, each
verified to fail on pre-fix code (by temporarily reverting `CssLayoutEngineTable.cs` alone via
`git stash`) and pass post-fix:
`ColspanCell_StraddlingVisibleThenCollapsedColumn_NoResidualBorderSpacing`,
`ColspanCell_StraddlingCollapsedThenVisibleColumn_NoExtraInteriorSpacing`,
`ColspanCell_StraddlingCollapsedColumn_ContentDoesNotShareWidthWithIt`. Full suite (`dotnet test
PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`): 8180 passed, 0 failed. `diff-cover` against
`origin/main`: 100% diff coverage on the 22 changed lines. `dotnet build PeachPDF.slnx -t:Rebuild`: 0
warnings.

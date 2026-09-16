# Collapsed-table rows/columns meet flush instead of overlapping by a border width

Surfaced as a residual, non-compounding-looking discrepancy while fixing #1127 (a different bug): even
the pre-#1127-regression baseline trailed Chrome by a small, steadily *growing* amount across a table's
rows. Investigating that gap turned up a second, independent, older bug: under `border-collapse:
collapse`, every interior row-to-row and column-to-column boundary lost one full border width from the
table's overall size, compounding once per row/column. Filed and fixed as #1138.

## The load-bearing idea

`ApplyCollapsedUsedBorderWidths` gives every cell/row exactly **half** the resolved collapsed border
width as its own used border on each shared edge — correct, and baked into `Location`/`ActualBottom`/
`ActualRight`. Separately, `VerticalSpacingAt`/`HorizontalSpacingAt` pulled the row/column cursor back by
the **full** resolved width at an interior grid line — a second, independent border-width deduction. Box-
model proof of the bug: with a shared border of width `W` centered at point `P`, row(i-1) reserves
`[P-W/2, P]` as its own bottom border (`ActualBottom == P`), and row(i) reserves `[P, P+W/2]` as its own
top border (`Location.Y == P`) — the two rows should therefore sit exactly flush (`Location.Y -
ActualBottom == 0`), not overlap by `-W`. The full-width interior pull double-subtracted the one shared
border, cancelling its contribution to inter-row/inter-column spacing to zero.

Confirmed three independent ways before writing the fix: hand-derivation from the box model; a scratch
`LayoutHarness` test showing `row2.Location.Y - row1.ActualBottom == -0.75` for a 0.75pt resolved border
(should be `0`) — reproduced identically on the column axis; and rendering #1127's own repro through the
CLI and reading real glyph positions with PyMuPDF — the "flush" prediction's row quantum (content +
padding + **one** border width) reproduces Chrome 141's measured 25.5pt quantum exactly, where the buggy
code's quantum (24.75pt) has **zero** border contribution.

## Why the earlier work (#735, #744) didn't catch this

The `-width` interior-overlap model was deliberate, landed and test-pinned work, not an oversight —
`.claude/recent-fixes/2026-08-13-collapsed-table-borders-css-2-1-17-6-2.md` describes it as the
"overlap-then-paint-border-last model." That work verified `GetWidthSum()`'s aggregate *total* against a
known-good number and confirmed a *single* boundary painted plausibly, but never cross-checked an
individual row/column's *absolute* position against a true external reference (a real browser) — exactly
the check that closing #1127 did, by chance, for an unrelated bug. `GetWidthSum` itself turned out not to
be independent of the buggy primitive either, despite its own doc comment's claim (it calls
`HorizontalSpacingAt` via `SumHorizontalSpacingExcludingCollapsedColumns`) — it happened to still produce
the right total under the fix, but that was verified, not assumed (an existing test,
`TableWidthMatchesColumnWidthsMinusInteriorGaps_NoOuterEdgeResidual`, already pinned it and needed no
changes).

## The fix

- `VerticalSpacingAt`/`HorizontalSpacingAt`'s interior-line branch changed from `-width` to `0` (the
  outer-edge branch, `-width/2`, is untouched — the table's own border box still supplies its own half
  there).
- `GetGridLineY`/`GetGridLineX` (paint-time border-segment centering) and `EmitHeaderFooterBorderSegments`'s
  local `SnapshotLine` (which hand-duplicates the same correction for a repeated `<thead>`/`<tfoot>`,
  since it reads `CssProxyBox.SourceGeometry` snapshots rather than live grid positions) both drop their
  now-unneeded "recover the true center from the overlap band" half-width correction term, since a
  neighbor's own edge now directly *is* the shared line's center.
- `RowAxisFarEdgeCorrectionSign`, a sign helper that existed only to drive that correction, is now dead
  and deleted.

## A real bug introduced and caught during implementation

The `adjacentRowIndex` epsilon-tolerance comparison (finds which body row is visually adjacent to a
repeated header's/footer's own edge) branched on `RowAxisFarEdgeCorrectionSign > 0`. Replacing the
now-deleted sign property with the underlying `_rowAxisStartIsAtMax` bool, the first attempt added a
stray `!` negation, inverting the comparison direction for the common (non-`vertical-rl`) case. This
broke two repeated-header/footer pagination tests
(`RepeatedThead_BoundaryToBody_ResolvesFreshPerPage_NotReusedFromPage1`,
`RepeatedTfoot_BoundaryToBody_ResolvesFreshPerPage_NotReusedFromTheLastPage`), both of which failed with
the header's boundary segment wrongly resolving against the wrong page's row. Caught by the full test
suite before this landed; fixed by removing the negation.

## Evidence

- New tests in `CollapsedBorderLayoutTests.cs`: `MultiRowTable_RowHeightsSumExactly_NoCompoundingDrift`/
  `MultiColumnTable_ColumnWidthsSumExactly_NoCompoundingDrift` assert no compounding drift across three
  rows/columns; confirmed non-vacuous (fail with the old `-width` behavior reverted).
- `CollapsedBorderLayoutTests.cs`'s and `CollapsedBorderGeometryTests.cs`'s existing tests, which pinned
  the old overlap as ground truth, rewritten to assert flush geometry / border-segment-centered-on-a-point
  instead — including the header/footer-boundary and vertical-divider-agreement cases.
- Full net8.0 suite: 12,071 passed, 0 failed, 9 platform skips. Diff coverage on the changed source file:
  100%.
- Re-rendered #1127/#1138's own repro through the CLI: row-to-row deltas now match Chrome to within
  0.04pt for the marker after the table, with the previously-compounding drift gone entirely (every row
  now a constant, non-growing ~0.65pt off Chrome — an unrelated, much smaller, pre-existing difference).
- The `table_collapsed_border_resolution` showcase (from #735/#744) re-rasterized through both PDFium and
  MuPDF, including its repeated-`<thead>` page — both renderers agree, no border doubling, gaps, or
  misalignment.
- `dotnet build -t:Rebuild`: 0 warnings, 0 errors.

One pagination test's fixed tolerance (`CssLayoutEngineTablePageBreakTests.TableLayout_MultiPageTable_RowsDoNotOverlapPageMargins`)
needed a modest, documented increase: with rows now legitimately taller (matching Chrome), a page's last
row can sit slightly further past the nominal content-bottom before the page-break estimate (itself
always an approximation, per its own doc comment) catches up — an accepted, pre-existing category of
slop the test's own comment already documented, not a new defect.

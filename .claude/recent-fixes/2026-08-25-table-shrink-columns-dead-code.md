# `ShrinkColumnsToFitAvailableWidth` is live: bounds check, missing recompute, and the vertical-mode root cause (#819)

`CssLayoutEngineTable.CanReduceWidth(int)` had its bounds check inverted (`_columnWidths!.Length >=
columnIndex`, true for every in-range index), so `CanReduceWidth(int)` always returned `false`, and so did
the parameterless `CanReduceWidth()` that loops over it — making `ShrinkColumnsToFitAvailableWidth`'s outer
`while (widthSum > GetAvailableTableWidth() && CanReduceWidth())` provably dead. See the now-deleted
`.claude/accepted-gaps/table-shrink-columns-dead-code.md` for the prior (issue #814) attempt that fixed the
bounds check alone, found it regressed vertical-writing-mode column sizing, and reverted rather than dig
further.

## Load-bearing idea

Fixing the bounds check alone is not enough — there were three independent bugs, and shipping only the
documented two (bounds check + inner-loop wraparound) would have reproduced the same regression the prior
attempt hit. The third, previously undiscovered: `ShrinkColumnsToFitAvailableWidth`'s outer `while`
condition compared against a `widthSum` computed **once**, before the loop, and never recomputed inside it.
Once the bounds check made `CanReduceWidth()` genuinely reachable, the loop's only way to terminate was
`CanReduceWidth()` going false — i.e. it shrank *every* column all the way down to its own minimum,
regardless of how much shrinking the table's own explicit width actually needed. That, not a physical-axis
mismatch in `GetAvailableTableWidth()`/`GetWidthSum()` (both already correctly writing-mode-aware, per
the accepted-gap file's own hint), was most of the vertical-mode regression: a vertical table's
`GetColumnMinWidths()` returns effectively 0 for every column with no explicit `min-height` (there is no
vertical-writing-mode-aware content-measurement equivalent — a documented, pre-existing, separate gap), so
once the stale-`widthSum` bug let the loop run to completion, it collapsed every auto-sized vertical
column toward zero.

Fixed all three together:
1. `CanReduceWidth(int)`: `columnIndex >= _columnWidths.Length || columnIndex >= GetColumnMinWidths().Length`.
2. The inner `while (!CanReduceWidth(curCol)) curCol++;` search now wraps `curCol` back to 0 instead of
   walking off the end — the outer condition already guarantees some column is reducible before each
   entry, so wrapping is guaranteed to find it within one full pass.
3. `widthSum` is recomputed after every single-point reduction (mirroring `ClipColumnsToMaxWidth`'s own
   pattern), so the loop stops the moment the table actually fits.

## What running it (not reading it) found

With only the three fixes above, `dotnet test --filter FullyQualifiedName~Table` still failed 5 tests, all
genuinely new findings, not restatements of the two documented bugs:

- **A vertical table's `GetAvailableTableWidth()` fallback isn't a genuine constraint when the table has no
  explicit height.** Under `_isVertical`, the "no explicit size" fallback reads
  `ContainingBlock.Size.Height` — but unlike `Width` (always resolved top-down before a child lays out),
  an auto-height container's `Size.Height` is only set in its own post-order height epilogue
  (`CssLayoutEngine`'s `IsHeightCalculated` setter), which runs *after* this table, one of its children,
  has already laid out. Confirmed by instrumenting `GetAvailableTableWidth()` directly: for the auto-height
  fixture that already passed on unpatched `main`, it returned `containingBlockInlineSize=0,
  IsHeightCalculated=False` on every one of 39 calls during the newly-live shrink loop — a placeholder, not
  a real 0-height constraint — which collapsed the table's columns toward zero. Fix: skip
  `ShrinkColumnsToFitAvailableWidth` entirely when `_isVertical && !_widthSpecified &&
  !ContainingBlock.IsHeightCalculated` (no genuine constraint exists yet); a table with its own explicit
  `height`, or nested inside a definite-height ancestor, is unaffected by this guard and still shrinks
  normally.
- **A collapsed column's still-present (pre-`CollapseColumnWidths`) width inflated the shrink target.**
  `CollapseColumnWidths` zeroes a `visibility: collapse` column's width, but runs *after*
  `EnforceMaximumSize` (by design — see its own remarks on why it must run last). Comparing the raw
  `GetWidthSum()` (which still counts a collapsed column's full pre-zero width) against
  `GetAvailableTableWidth()` made the shrink loop think a collapsed column's about-to-vanish width counted
  toward the deficit, and wastefully shrank *visible* columns to compensate for width collapse was already
  going to remove in full. Added `GetWidthSumExcludingCollapsedColumnWidths()` (subtracts every collapsed
  column's current width from `GetWidthSum()`) as the comparison basis, and excluded a collapsed column
  from `CanReduceWidth(int)` entirely (it is already headed to zero regardless of anything this method
  decides). Found via three pre-existing `TableVisibilityCollapseIntegrationTests` fixtures whose *control*
  table (no collapsed column, meant as an unaffected baseline) turned out to have its own width
  (`205px`) genuinely narrower than its two declared columns + spacing actually need (`215px`) — invisible
  while the shrink pass was dead code, and exposed once it went live. Corrected the fixtures' control width
  to `215px` (the real, self-consistent total) rather than changing the assertions to match a coincidence.
- **A vertical table cell's own explicit `height` is an unshrinkable per-cell floor CSS 2.1 §17.5.3
  requires, and `GetColumnMinWidths()` didn't know it.** A vertical table's column-sizing hint *is* a
  cell's own `height` (`CellInlineSize`), but that same property also makes the cell's own layout
  (`CssLayoutEngine.ApplyHeight`'s `Math.Max` against `IsTableCell`) reassert it regardless of what the
  table algorithm decides. Without folding that into `GetColumnMinWidths()`'s vertical branch, the shrink
  loop could reduce `_columnWidths` below a value a cell was going to repaint at anyway — internally
  consistent bookkeeping (interior spacing, the next column's start, `GetWidthSum()`) diverging from what
  actually paints. Found by writing a positive "vertical table's own explicit height genuinely shrinks its
  column" test and discovering the assertion failed not because shrink didn't run (it did, confirmed by
  direct instrumentation — `_columnWidths[0]` went 150 → 100 exactly as expected), but because the cell's
  own explicit `height: 150pt` silently overrode it right back to 150 during the cell's own layout pass. A
  second discovery from the same investigation, *not* fixed (out of scope for #819): an auto-height cell in
  a vertical table does not stretch to fill its column's shared extent at all — `CreateLineBoxes` (which
  runs before `ApplyHeight`) sets `ActualBottom` from the cell's own content, discarding whatever the table
  row loop pre-set, and `ApplyHeight`'s subsequent `Math.Max` against an auto height (`0`) can't recover
  it. This is a pre-existing, deeper gap (`width: auto` in normal block layout means "stretch to fill",
  while `height: auto` means "shrink to content" — the exact asymmetry that makes the horizontal analog a
  non-issue) that no existing vertical-table test happened to exercise, since every one of them gives every
  cell an explicit `height`. Sidestepped in this fix's own new tests by using `<col>`-declared column
  sizing (not a table cell, so CSS 2.1 §17.5.3's floor doesn't apply to it) rather than relying on this gap.

## Deliberately not done

- Did not attempt to give vertical tables a real content-measurement equivalent to
  `CssBox.GetMinimumWidth()`/`GetMinMaxWidth()` (the pre-existing gap `GetColumnsMinMaxWidthByContent`'s
  own remarks already document) — out of scope for this issue, unaffected either way by the fixes here.
- Did not fix the newly-discovered "auto-height cell in a vertical table doesn't stretch to its column"
  gap described above — a real, separate defect, but not one #819's own scope (`CanReduceWidth`/
  `ShrinkColumnsToFitAvailableWidth`) touches or regresses; every existing and new test avoids triggering
  it by giving relevant cells an explicit height or sizing columns via `<col>` instead. Tracked as
  [#836](https://github.com/jhaygood86/PeachPDF/issues/836) and recorded in
  [.claude/accepted-gaps/vertical-table-auto-height-cell-does-not-stretch.md](../accepted-gaps/vertical-table-auto-height-cell-does-not-stretch.md).

## Evidence

- New tests: `CssLayoutEngineTableTests.TableLayout_ExplicitColumnWidthsExceedTableWidth_ShrinksColumnsToFit`
  (horizontal shrink is live and correct), `TableLayout_ShrinkSearchWrapsAroundUnreducibleColumn_TerminatesQuickly`
  (an unreducible *last* column forces the inner search to wrap every cycle; asserts sub-5-second
  completion — the old missing-wraparound bug hung for 10+ seconds or threw), and
  `TableLayout_ExplicitWidthWithCollapsedColumn_ShrinksOnlyVisibleColumns` (collapsed-column exclusion).
  `TableWritingModeIntegrationTests.VerticalRl_ExplicitHeight_ShrinksColumnToFit` (vertical shrink fires
  when a genuine constraint exists).
- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0 --filter FullyQualifiedName~Table` —
  612/612 passed (up from 608 pre-existing + this change's 4 new tests, 5 initially failing before the
  vertical-guard/collapse-exclusion/cell-floor fixes above, all subsequently fixed rather than the tests
  weakened).
- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` — full suite green: 9227 passed, 9
  pre-existing platform-gated skips, 0 failed.
- Diff coverage (`diff-cover` against `main`): 100% (22/22 changed lines in
  `CssLayoutEngineTable.cs` covered).
- `dotnet build PeachPDF.slnx -t:Rebuild` — 0 warnings, 0 errors.

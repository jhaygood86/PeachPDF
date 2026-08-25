# A vertical table's auto-height cell now stretches to fill its column (#836)

Closes the gap `.claude/accepted-gaps/vertical-table-auto-height-cell-does-not-stretch.md`, discovered
incidentally while fixing #819 (see `.claude/recent-fixes/2026-08-25-table-shrink-columns-dead-code.md`
for the investigation that surfaced it).

## Load-bearing idea

CSS 2.1 §17.5.3 makes a table cell's own height the *larger* of its content height and its column's
assigned extent - not its content height alone. That already worked for a cell with an explicit
`height`: `CssLayoutEngine.ApplyHeight`'s own `Math.Max(box.ActualBottom, box.Location.Y + height)`
floors it against whatever `CreateLineBoxes` (which runs first, as part of `cell.PerformLayout`) set
from content. It did not work for `height: auto`, because `ApplyHeight` has no explicit value to float
against - `GetBoxHeight` just returns the content-driven height for `auto`, so the `Math.Max` floors
against nothing.

`CssLayoutEngineTable.LayoutBodyRow` already pre-sets a vertical cell's column-axis extent
(`cell.ActualBottom = cell.Location.Y + width`, `width` from `_columnWidths[]`) immediately before
calling `cell.PerformLayout(g)` - but `CreateLineBoxes` unconditionally overwrites `ActualBottom` from
content during that same call, discarding it. The method already had a narrow fix for exactly this
clobbering mechanism, scoped to `CssSpacingBox` placeholders only (`if (_isVertical && cell is
CssSpacingBox) cell.ActualBottom = cell.Location.Y + width;`, added for a rowspan-placeholder bug its
own comment describes) - a spacing box's `WritingMode` never inherits (bare tag, no style), so its
own auto-height-from-empty-content resolution always collapses `ActualBottom` back to `Location.Y`
regardless of writing mode. Generalized that same re-assertion to every vertical cell, not just spacing
boxes, and changed it from a bare overwrite to `Math.Max(cell.ActualBottom, cell.Location.Y + width)` so
a cell whose own content genuinely needs more than the column's assigned extent isn't clamped down to
it - the direct table-engine analog of `ApplyHeight`'s own floor, just sourced from the column-width
decision instead of an explicit `height` property.

## What running it (not just reading it) confirmed

- The straightforward repro (a `<col>`-declared column height, an auto-height cell) failed against
  pre-fix source exactly as predicted: the cell's own tiny one-word content height (13pt), not the
  column's 150pt.
- First attempt at the repro used `<col style="height: 150pt">` and passed even against pre-fix source -
  a false negative, not a working test. Root cause: `CalculateColumnWidths`'s `<col>` width/height scan
  only recognizes percentage and pixel/unitless units (`CssUnit.Pixels or CssUnit.None`), not `pt` - a
  `pt`-valued `<col>` height is silently ignored and the column falls back to 0/content-derived width
  instead. Confirmed by instrumenting `LayoutBodyRow` directly (`_columnWidths=[0]` with the `pt` value,
  `_columnWidths=[150]` with the equivalent `px` value) before rewriting the test to use `px`, matching
  the pre-existing `VerticalRl_ExplicitHeight_ShrinksColumnToFit` test's own (previously unexplained)
  choice of `px` over `pt` for its own `<col>` declaration.
- The "taller content must not be clamped down" half also needed an empirical correction: an initial
  attempt used several `<br>`-separated lines to force genuine multi-line content height, but that cell's
  measured height came back exactly equal to the column floor regardless of line count - this engine's
  vertical-table content measurement is a separate, already-documented gap
  (`GetColumnsMinMaxWidthByContent`'s own remarks: "CssBox.GetMinimumWidth has no vertical-writing-mode-
  aware equivalent"), and forced line breaks inside a narrow-width vertical cell apparently don't reliably
  grow `ActualBottom` the way a plain nested block with an explicit height does. Switched the test to a
  child `<div style="height: 80pt">` instead, which reliably reproduces "content taller than the column's
  assigned extent" without depending on that adjacent, unrelated gap.

## Deliberately not done

- Did not add a defensive `PendingBreakToken is null` guard around the new floor, despite the new
  `Math.Max` running before the method's own `PendingBreakToken`-stopped-cell correction a few lines
  below (which does its own, different `Math.Max` against `GetMaximumBottom`). Traced why the two can't
  actually collide: this class forces a vertical table's own `pageHeight` to `double.MaxValue` before
  this loop ever runs (its pagination-cursor setup), and every break check in this file is gated on
  `pageHeight < double.MaxValue - 1` - so no cell reached from `LayoutBodyRow` for a vertical table ever
  stops mid-fragment, and `PendingBreakToken` is always null here. A guard would be dead code for a
  scenario that cannot currently occur; added a one-line comment cross-referencing the invariant instead,
  so a future change that ever lets a vertical cell fragment mid-column has something to trip over before
  it silently combines with this floor.
- Did not attempt the adjacent, already-documented vertical-table content-measurement gap
  (`GetColumnsMinMaxWidthByContent`'s remarks) that the `<br>`-based test attempt ran into - out of scope
  for #836, unaffected either way by this fix.

## Evidence

- New tests (`TableWritingModeIntegrationTests.cs`): `VerticalRl_AutoHeightCell_StretchesToColumnExtent`
  (the core repro - a `<col>`-sized column, auto-height cell, asserts the cell fills the column's 150pt
  rather than its own ~13pt content height) and
  `VerticalRl_AutoHeightCell_TallerContentStillWinsOverColumnExtent` (the `Math.Max` direction - a cell
  whose content needs 80pt against a 39.75pt column floor must end up at 80pt, not clamped down). Both
  confirmed to fail (first one) / already-pass-for-the-right-reason (second one, since content already
  exceeded the floor even pre-fix - it exists to guard the `Math.Max` direction against a future
  regression to a bare overwrite, not to reproduce #836 itself) against pre-fix source, and both pass
  post-fix.
- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0 --filter FullyQualifiedName~Table` -
  614/614 passed (612 pre-existing + 2 new), no regressions.
- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` - full suite green: 9242 passed, 9
  pre-existing platform-gated skips, 0 failed.
- `dotnet build PeachPDF.slnx -t:Rebuild` - 0 warnings, 0 errors.

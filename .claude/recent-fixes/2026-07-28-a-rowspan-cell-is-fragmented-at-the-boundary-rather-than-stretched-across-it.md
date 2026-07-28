# A rowspan cell is fragmented at the boundary rather than stretched across it

_Landed 2026-07-28._

[#511](https://github.com/jhaygood86/PeachPDF/issues/511), the second of the three
[#432](https://github.com/jhaygood86/PeachPDF/issues/432)/PR #510 left behind. The row loop's straddle
correction declined to move a row that *ends* a `rowspan`, so such a row was left where it fell and drawn
cut through by the page boundary.

## The reframing, and it came from reading the other engines

The obvious fix — and the first draft of the plan — was to move the **whole spanned run**: take the break
before the row that *opens* the span so the span travels intact. It was designed, then dropped, because
both other engines do something else:

- **Gecko** splits the cell. `nsTableRowGroupFrame::SplitRowGroup` calls `SplitSpanningCells` — *"Reflow
  the cells with rowspan > 1 which originate between aFirstRow and end on or after aLastRow"*, and inside
  it *"Only reflow rowspan > 1 cells which span aLastRow. Those which don't span aLastRow were reflowed
  correctly during the unconstrained bsize reflow."* Those cells are re-reflowed against the remaining
  block-size and continued through `CreateContinuingRowFrame`. It is a long-buggy path (Mozilla bugs
  1154623, 400149, 301378) but the *design* is to split.
- **Blink** does too, through parallel flows. `table_row_layout_algorithm.cc`: *"Subtract this difference,
  so that this cell won't overflow the row — unless the cell is rowspanned. In that case it doesn't make
  sense to compensate against just the current row"*, beside *"we always visit all cells in a row (cannot
  break halfway through; each cell establishes a parallel flow that needs to be examined separately)"*.

Neither keeps the spanned rows together. There is **no WPT coverage** — `css/css-break/table/` has no
rowspan test — so the engines' own code is the standard, and it agrees with css-tables-3 §6.1.

So: the ending row is moved like any other, and the **cell** is fragmented. The primitive was built one
issue earlier — PR #508's `RecordContinuationShell`, whose own "deliberately not done" list names this
exact case ("A `rowspan` cell's own continuation").

**What keeps it bounded:** a spanning cell whose content did not fit its band stopped when its own row was
placed, and the row loop stops at a row a cell stopped in. So a spanning cell that reaches its ending row
is one whose content fits where it already is — this fragments its *box*, not its flow.

## What Step 0 measured, and two things it disproved

Re-measured on `a6171b7` before any edit. The issue's own numbers reproduce exactly (ending row at
Y 214, bottom 334, band [20, 280]). Two things the issue and the gap file said did not survive:

- **The defect was not confined to the straddle correction.** All three routes to a break between two
  rows of a span produced the stretched box: the correction declining (cell 175→334), the loop's
  `EstimateRowHeight` prediction breaking before a row in the **middle** of a span (175→359), and a
  forced `break-before: page` on one (19→359). That is why the fix asks the **bands** in `LayoutBodyRow`
  rather than fixing whichever arm broke — a fix keyed to the correction would have fixed a third of it.
- **The gap file's claim about `CssSpacingBox` was false.** It said `InsertEmptyBoxes` gives row *r* a
  spacer "exactly when" some earlier row's cell ends on *r*. Measured: **no spacer at all** when the
  spanning cell is in the last column — `InsertEmptyBoxes`' inner loop only walks the later row's
  *existing* cells, so a spacer belonging after the last one is never inserted. 5 of 7 probe fixtures had
  zero spacers. Anything keyed to spacer presence would have missed the issue's own fixture. Worth
  keeping: **a claim about a data structure is not a measurement of it.**

## A second defect the probe found on the way

`boxesToVerticallyAlign` is `row.Boxes ∪ boxesThatEndOnRow`, so where a spacer *does* exist the spanning
cell is reached twice — once as `spacer.ExtendedBox`, once as itself — and
`ApplyCellVerticalAlignment` **offsets a subtree rather than assigning a position**. Two identical
documents differing only in which column the spanning cell sits in put its content 24.75pt apart
(93.2 vs 68.5 on a 99pt leftover): D/2 then a further D/4. A `<td>`'s used `vertical-align` is `middle`,
so this was not exotic. Closed here, since the same helper is what makes each cell closed once.

`ASpanningCellReachedTwice_IsAlignedOnce` states it as an **equality between the two documents** rather
than against a number, because which of them has a spacer at all is the variable.

## What the rasterization changed, and it was not the tests

The first working version closed the cell at the **band's foot**. Both renderers showed the tint hanging
below the last row on the page — past the table's own bottom edge, because `FragmentPainter` clips that
border to `PageBreakBottoms`. It now closes at `min(PageBreakBottoms[cellSlot], band foot − footer room)`,
never above the cell's own content. **The suite was green either way**; only the picture said so.

## Deliberately not done

- **A spanning cell whose *content* overflows its band keeps the stretched box.** Closing a box above its
  own content puts what lies below inside no fragment at all — measured immediately as ~100 unclaimed
  words in `TableCellBreakTokenTests.APaginatingTable_DropsNoWord`, the suite's word census and the one
  thing that catches it. That case wants the flow-level continuation §6.1 asks for.
  [#521](https://github.com/jhaygood86/PeachPDF/issues/521), with
  [a gap file](../accepted-gaps/table-a-rowspan-cells-own-content-is-not-continued-past-its-band.md).
- **A forced `break-before` inside a span is still honored exactly where declared.** §3.1 requires it, and
  rerouting it to the row opening the span would break a page earlier than the author asked. The cell is
  fragmented there like anywhere else, so the rendering is right; only the run does not travel.
- **`InsertEmptyBoxes`' last-column hole is not fixed.** It changes spacer presence for every table with a
  trailing `rowspan`, which is a wider behaviour change than this issue.
  [#522](https://github.com/jhaygood86/PeachPDF/issues/522).
- **The pre-existing duplicate fragments** for a spanning cell (reached once per `CssSpacingBox` standing
  in for it, and a spacer is itself in two child lists) are untouched — identical rectangles, so overdraw,
  and already recorded as an accepted limitation on `APaginatingTable_DropsNoWord`.

## Evidence

Full net8.0 suite green (**6,941 passed**, 9 skipped, up 10 from `main`'s 6,931), net10.0 green (6,941),
CLI suite green (96), `dotnet build PeachPDF.slnx -t:Rebuild` with **zero warnings**, `diff-cover`
**100% over 80 changed lines** against `origin/main`.

**72 of 72 existing showcases byte-identical** to `main` after normalizing creation date/time, `/ID`,
subset tags and the annotation `/M`/`/NM` — the change is confined to the case the issue is about. One
showcase is new (`paged_media_table_rowspan_break`), rasterized on both pages with PDFium **and** MuPDF
and read; both agree.

Tests: new `TableRowspanContinuationTests` (8 methods, 9 cases). **Six fail on `main`'s engine**, checked
by reverting the source files and re-running; the three that pass there are the control
(`ASpanInsideOneBand_ProducesNoContinuation`, which stops "it has a continuation" passing against a change
that gives everything one) and two guards that must hold both before and after. Plus
`RetractingARowsPlacement_PutsBackTheSpanningCellItWroteTo` in `TableRowLoopResumptionTests`, which pins
the foreign-write record directly — including that replaying the retraction is a no-op, since the
alignment offset would otherwise compose.

The alignment test needed a **partial** revert to fail on `main` — only `CssLayoutEngineTable.cs`, since
the new cursor API is what the test file compiles against. Worth knowing before concluding a test "passes
on main": it may simply not have built.

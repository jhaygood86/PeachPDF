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
rowspan test — so the engines' own code is the standard.

**And the spec says it outright, which the review pass established after the design was already chosen.**
`www.w3.org` and `drafts.csswg.org` are both 403 through this environment's proxy; the normative text is
reachable as `w3c/csswg-drafts` `css-tables-3/Overview.bs` on `raw.githubusercontent.com`. Worth knowing,
because a review that cannot fetch a spec reports its citations as unverified. §6.1 keeps a row
unfragmented only *"if the cells spanning the row do not span any subsequent row"* — so the row that
**ends** a span is moved whole and one in the **middle** of a span is *freely fragmentable*, the spec's
own term, with *"all the remaining height in the fragmentainer"* going to its cells. It also requires that
*"top borders must not be repainted in continuation fragments"*, which is now pinned by a test rather than
assumed.

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

## What the review pass caught after the suite was green

Four, and none of them was visible to 6,941 passing tests:

- **The multi-column gate was `is not { HasOwnBand: true }`, which is true for `null`** — a measurement
  pass behind a detached fragmentainer, where a close would state continuation geometry at a provisional
  position nothing ends up at. Now `is { HasOwnBand: false }`.
- **The table's slice did not follow a cell that reached lower than the rows above the break**, so the
  bottom border would have been drawn across a tall spanning cell opened by a short row — the same defect
  class the rasterization caught earlier, one layer down. `PageBreakBottoms` is now raised to the cell.
- **The `CssSpacingBox` arm bypassed the loop's own `FinishedOnAnEarlierPass` guard**, which tests the
  spacer rather than the cell it stands in for.
- **A retracted placement left its continuation shells behind.** Safe today only because the correction
  always moves forward and the re-placement restates a superset; the row loop now sweeps them explicitly,
  and `TableRowCursor.Retract`'s doc no longer claims shells never need taking back.

Three simplifications landed with them, all behaviour-neutral (72 of 72 showcases still byte-identical and
the new one unchanged): `LayoutBodyRow`'s `slot` parameter was `cursor.SlotIndex` at every call site —
`BandReached` and `MoveToSlot` both *assign* the field before returning it — the deferred continuation
list deferred nothing, and the dedupe set duplicated the cursor's own record.

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
- **A split cell's `vertical-align` resolves against the fragment on the page it began on**, not against
  the whole cell. Documented rather than changed: skipping the alignment instead — the rule the loop
  applies to stopped and resumed cells — would move every fragmented spanning cell's content to the top of
  its box, and `middle` is a `<td>`'s used value.
- **The pre-existing duplicate fragments** for a spanning cell (reached once per `CssSpacingBox` standing
  in for it, and a spacer is itself in two child lists) are untouched — identical rectangles, so overdraw,
  and already recorded as an accepted limitation on `APaginatingTable_DropsNoWord`.

## Evidence

Full net8.0 suite green (**6,944 passed**, 9 skipped, up 13 from `main`'s 6,931), net10.0 green (6,944),
CLI suite green (96), `dotnet build PeachPDF.slnx -t:Rebuild` with **zero warnings**, `diff-cover`
**100% over 86 changed lines** against `origin/main`.

**72 of 72 existing showcases byte-identical** to `main` after normalizing creation date/time, `/ID`,
subset tags and the annotation `/M`/`/NM` — the change is confined to the case the issue is about. One
showcase is new (`paged_media_table_rowspan_break`), rasterized on both pages with PDFium **and** MuPDF
and read; both agree.

Tests: new `TableRowspanContinuationTests` (11 methods, 12 cases). **Six fail on `main`'s engine**, checked
by reverting the source files and re-running; the ones that pass there are the control
(`ASpanInsideOneBand_ProducesNoContinuation`, which stops "it has a continuation" passing against a change
that gives everything one) and two guards that must hold both before and after. Plus
`RetractingARowsPlacement_PutsBackTheSpanningCellItWroteTo` in `TableRowLoopResumptionTests`, which pins
the foreign-write record directly — including that replaying the retraction is a no-op, since the
alignment offset would otherwise compose.

The alignment test needed a **partial** revert to fail on `main` — only `CssLayoutEngineTable.cs`, since
the new cursor API is what the test file compiles against. Worth knowing before concluding a test "passes
on main": it may simply not have built.

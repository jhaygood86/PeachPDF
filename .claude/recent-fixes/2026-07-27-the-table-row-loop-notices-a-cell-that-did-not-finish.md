# The table row loop notices a cell that did not finish

_Landed 2026-07-27._

`CssLayoutEngineTable.LayoutBodyRow` called `cell.PerformLayout(g)` and read one thing back:
`cell.ActualBottom`. A cell that ran out of fragmentainer before it ran out of content says so in
`CssBox.PendingBreakToken`, and nothing was listening. That missing consumer — not the monolithic
gate — is [#390](https://github.com/jhaygood86/PeachPDF/issues/390) stage 4's first obligation, and
this is it: the row loop now asks, `TableRowCursor` records the answers, and `LayoutCells` publishes
them on the table as `CssBox.UnfinishedTableCells`. **Nothing acts on them yet.**

**Behaviour-neutral, and neutral by construction rather than by luck.** The engine runs inside
`CssBox.LayoutMonolithicContent`, which detaches the fragmentainer, so no descendant of a cell has
one to run out of and the record is empty for every fixture. All 69 showcases byte-identical
(normalizing creation date, `/ID`, `/M`, `/NM` and subset tags) *and* pixel-identical rasterized page
by page under PDFium; 6,671 tests; 96 CLI tests; 100% diff coverage; zero-warning solution rebuild.

## Why the question is asked exactly where it is

**A cell's record is readable for one instant.** `CssBox.BeginLayoutPass` clears
`PendingBreakToken` at the top of every `PerformLayoutImp`, so the answer exists between the cell's
layout returning and the next layout of that box — which is the statement inside the row loop's
`foreach`, and nowhere else. Anything that tried to collect this afterwards would collect nulls.

**It is deliberately not published as the table's own `PendingBreakToken`.** That record means
"resume me": `PerformLayoutImp` returns early on it, `PublishBreakToTheContextRoot` hands it to the
fragmentation context, and `LayoutBlockChildren` stops the parent's child loop and wraps it. Setting
it would make everything above the table try to resume a table that cannot yet be resumed — which is
the whole of stage 4, not this step. `UnfinishedTableCells` is read by nothing in the library.

**A `<thead>`/`<tfoot>` measurement cursor keeps its own record**, and that falls out of
`ForRowGroupMeasurement` already returning a fresh cursor. It is not incidental: those rows are laid
out once to learn their height and then repeated by proxy, so where one of them stopped says nothing
about where the body resumes — and by the time a resumed pass ran, the group would not be in the tree.

## What was found by running it

**Ten table shapes, 244 words, a 300pt page: not one produces a cell token today.** Bare `<td>`,
`<p>`-in-`<td>`, two rows, repeating `<thead>`, repeating `<tfoot>`, `column-count` in a `<td>`,
`display:flex` in a `<td>`, `display:grid` in a `<td>`, a `rowspan` cell, and a table nested in a
`column-count` container. That is the measurement the step rests on, and it is pinned by
`TableCellBreakTokenTests.APaginatingTable_RecordsNoUnfinishedCell` rather than left as prose.

**With the monolithic gate lifted, eight of those ten record exactly one unfinished cell** — the
experiment run to prove the seam is not dead code, then reverted. What it records is the right thing
and the right shape:

| fixture | recorded token |
|---|---|
| bare `<td>` | `InlineBreakToken` on the `<td>` itself (`ResumeWordIndex = 38`, `CompletedLineCount = 19`) |
| `<p>` in `<td>` | `BlockBreakToken` on the `<td>`, with the `<p>`'s `InlineBreakToken` as its `ChildToken` |
| `column-count` in `<td>` | `BlockBreakToken` on the `<td>`, wrapping the container's own |
| nested table | the **inner** table records it, the outer records nothing |

The two that record nothing are `display:flex` and `display:grid` in a `<td>`, which is consistent:
those engines go through `LayoutEngineContent` and measure their items with breaking suppressed.
Pages drop to 1 in every recording case — the content loss the gate is still there to prevent.

## The test double, and why one was needed

While the gate is down **no markup can produce a cell that stops**, so nothing markup-driven can pin
the call site: deleting `cursor.RecordIfUnfinished(cell)` would leave every showcase and every test
green. `TableCellBreakTokenTests.StoppingCell` is a `CssBox` subclass that overrides
`PerformLayoutImp` to hand back a record, injected into a row's `Boxes` through `LayoutHarness`'s
`prepare` hook — the same shape as `NoProgressBackstopTests.StallingBox`, for the same reason. It
replaces an anchor cell in place rather than being inserted beside it, because
[`ParentBox`'s setter appends](../invariants/fragmentation-cssbox-parentbox-setter-appends-to-the-new-parents-boxes.md)
and a row's cells are its columns.

Four neutralizations, each measured rather than asserted:

| neutralization | tests failing |
|---|---|
| delete `cursor.RecordIfUnfinished(cell)` from `LayoutBodyRow` | 1 |
| make `RecordIfUnfinished` record nothing | 4 |
| delete the publication onto `_tableBox` | 1 |
| record every cell, finished or not | 14 |

## Deliberately not done

- **The monolithic gate stays down.** It is the last thing to move, not the first — lifting it loses
  half a table's content while the row loop cannot act on what it now notices.
- **[#432](https://github.com/jhaygood86/PeachPDF/issues/432) was not touched.** The row loop's band
  is still a counter, and
  [the invariant](../invariants/fragmentation-a-stale-cursor-can-be-load-bearing-compensation-for-a-bad-estimate.md)
  says why correcting it in isolation regresses four tests.
- **Only `PendingBreakToken` is asked**, not `RequestedBreakBeforeTop`. A break falling *before* a
  cell is a different question with a different answer — it moves the whole row, which is the row
  loop's own per-row break check ([§4.4](https://www.w3.org/TR/css-break-3/#break-between)) rather
  than a cell's continuation.

## What the next step costs

Acting on the record means stopping the row loop at the first row that did not finish and turning
`UnfinishedTableCells` into a real `BreakToken` on the table. Before that can be behaviour-preserving,
four things `LayoutCells` does unconditionally have to become once-per-table rather than once-per-pass
— `RestoreStructureFromAnyPreviousRun` (which destroys earlier pages' repeated headers), the
`_tableBox.PageBreakBottoms = null` reset (which has to accumulate), the two whole-table pre-checks
(which move `_tableBox.Location`), and the header/footer measurement layouts (a `CssProxyBox` moves
the one shared source subtree before snapshotting it). None of them is safe on a continuation.

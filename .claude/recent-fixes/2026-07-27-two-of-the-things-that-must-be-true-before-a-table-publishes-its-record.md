# Two of the things that must be true before a table publishes its record

_Landed 2026-07-27._

[#464](https://github.com/jhaygood86/PeachPDF/issues/464) is one step — set the table's own
`PendingBreakToken` **and** move `CssBox.LayoutMonolithicContent` off the table arm, because a resumed
table whose cells still cannot see a fragmentainer re-places rows instead of continuing them. This
change is **not** that step. It is one of the two preconditions
[#466](2026-07-27-the-table-row-loop-stops-where-a-cell-stopped-and-a-later-pass-continues-it.md)
wrote down, plus a third that only running the gate move found, and the measurements that say what
the gate move now costs.

**The gate did not move.** Nothing sets a table's `PendingBreakToken`, so every one of these is still
reachable only from a test — which is exactly the point of landing them first.

## What the gate move actually measured, which is not what was predicted

#452 priced lifting the gate at **244 → 121 words** for a bare `<td>` and 246 → 135 with a repeating
`<thead>`, and read that as "the row loop cannot act on the token". The row loop can act on it now
(#466), so the gate was moved in a scratch tree and re-measured, 244 words on a 300pt page:

| fixture | words emitted, gate down | gate moved | + the fix below |
|---|---|---|---|
| bare `<td>` | 244 | 121 | **244** |
| `<p>` in `<td>` | 244 | 121 | 244 emitted, **12 of them twice** |
| two rows | 245 | 122 | **245** |
| repeating `<thead>` | 245 | 147 | 246 emitted, **1 twice** |
| two cells, one short | 245 | 141 | 245, but the short cell **moves to the continuation's page** |

The trace said the resumed pass *was* running — the driver opened it, the engine continued the right
row — and yet half the words were on no page. The cause is one value, and it is now
[an invariant](../invariants/fragmentation-a-cell-that-stopped-has-a-box-that-does-not-describe-its-content.md):

**A cell that stopped comes back with the `ActualBottom` its placement gave it — its own top.**
`CssLayoutEngine.CreateLineBoxes` sets that field after the flow finishes, and a flow that runs out of
fragmentainer returns before it. `CssLayoutEngineTable.LayoutBodyRow` then reads it twice: as the
row's `MaxBottom`, and as the box `CssLayoutEngine.ApplyCellVerticalAlignment` distributes
`ClientBottom - contentBottom` within. That difference is **negative** for a degenerate box, so a
`vertical-align: middle` cell — which every `<td>` is by inheritance — pushed its whole fragment *up*
by half its own depth: the first line landed at document Y **−104**, a page and a half above the
document origin, and the words above the grid were claimed by no fragmentainer.

That is fixed here, gate or no gate: a cell that stopped keeps its bottom at where its content
actually reached (`CssBox.GetMaximumBottom`) and is not vertically aligned at all. The spec statement
is the simpler one — a fragment that overflows has no spare room to align within.

## The two preconditions that landed

1. **A finished cell is distinguishable from an unentered one.** `TableBreakToken.FinishedCells`
   carries the cells of the stopped row that finished; without it both are simply absent from
   `UnfinishedCells` and a continuation enters both from the start. A continuation now places
   *nothing* for a finished cell — not its position, not its content, not its alignment — and only
   moves the column cursor past it. The remaining half, that it should contribute an **empty
   fragment** rather than none, is
   [an accepted gap](../accepted-gaps/table-a-finished-cell-produces-no-fragment-on-the-continuation.md)
   with its own issue; it needs the emitter, not another field on the record.

2. **A cell that stopped is not measured or aligned against its own degenerate box** — the invariant
   above.

## The precondition that did *not* land, and why

**The inconsistent-record guard was built and then dropped.** `CssLayoutEngine.CreateLineBoxes`
finalizes from `InlineBreakToken.CompletedLineCount`, so a block holding more lines than the record
accounts for has those re-finalized and `CssLineBox.AssignRectanglesToBoxes` throws `An item with the
same key has already been added`. Discarding the lines past the record (`CssBox.DiscardLineBoxesFrom`,
which exists for exactly this) is almost certainly the right answer, and it is conservative in both
directions — a record naming line *n* was written by the pass that finalized lines 0..*n*−1, so
nothing an earlier fragmentainer emitted can be inside the range.

**It is not here because no test written for it could tell it from its own absence.** Resuming a
plain `<div>` through `CreateLineBoxes` with a deliberately stale record does not throw, and the
obvious characterization — that no word ends up hosted on two lines — fails with the guard in place
too, because a record whose `ResumeWordIndex` is 0 re-places the words on the line it kept. The throw
#466 measured came from a cell resumed *inside the table engine*, where the boxes the lines host are
shared differently. Landing a guard that a neutralization sweep cannot fail is how a guard rots, so it
is filed with that reproduction rather than shipped: it is the one thing on #464's precondition list
still outstanding.

## What is still in the way of the gate, measured rather than guessed

Two duplications survive the fix above, and neither is a table-engine question:

- **`<p>` inside `<td>`: 12 words emitted twice.** The resumed cell's *block* child is re-placed
  rather than continued in place — its `Location.Y` moved from 21.5 to 140.3 between the two passes —
  so the frozen page-1 fragment and the page-2 fragment both claim the lines in between.
- **Repeating `<thead>`: one word twice**, the header cell's, through the proxies.

Both are `CssBox`'s generic block-resume path rather than `CssLayoutEngineTable`, which is worth
knowing before the next attempt: the table engine is no longer the part that loses the content.

## Every part neutralized

| neutralization | tests failing |
|---|---|
| `FinishedCells` never recorded | 1 |
| `RowIndex` no longer clears the per-row finished list | 1 |
| a continuation enters a finished cell anyway | 1 |
| a finished cell re-aligned by the continuation | 1 |
| a stopped cell's `ActualBottom` left at its placement value | 1 |
| a stopped cell vertically aligned anyway | 1 |

Two of those came back **passing** on the first run and were fixed rather than written up: the
alignment skip was invisible until the stopped cell had a taller sibling to raise the row's bottom,
and the dropped guard above is the other. A sweep that reports every part load-bearing on the first
attempt has usually not been read carefully enough.

Two of the tests carry explicit controls, because every assertion here is of the form "nothing
happened": `AContinuationNamingNoFinishedCell_RePlacesItInTheFragmentainerItIsFilling` is what makes
"the finished cell stayed put" mean something, and `ACellThatFinished_IsStillVerticallyAligned` is
what makes "the stopped cell's content stayed where the flow put it" mean something.

**6,808 tests, one full net8.0 suite run, zero failures**; the corpus is untouched by construction —
#466's probe found no showcase reaching the row loop's stop at all, and a continuation is reachable
only by running the engine again with a record in hand.

## Deliberately not done

- **The gate stays down**, and the table's `PendingBreakToken` is still never set (#464).
- **[#432](https://github.com/jhaygood86/PeachPDF/issues/432) untouched.** The row loop's band is
  still a counter, and
  [the invariant](../invariants/fragmentation-a-stale-cursor-can-be-load-bearing-compensation-for-a-bad-estimate.md)
  says why correcting it alone regresses four tests.
- **The inconsistent-record guard**, per the section above.
- **The two surviving duplications above are not chased here.** They are in the generic block-resume
  path, and half-fixing that while the gate is still down would be a change nothing can check.

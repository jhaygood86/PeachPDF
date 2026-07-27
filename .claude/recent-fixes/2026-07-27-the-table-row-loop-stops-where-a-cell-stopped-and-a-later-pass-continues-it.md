# The table row loop stops where a cell stopped, and a later pass continues it

_Landed 2026-07-27._

`CssLayoutEngineTable`'s row loop placed every body row whatever it was told, and a run continuing an
earlier fragmentainer pass started at row 0 with an empty cursor. It now **stops** at the first row a
cell did not finish in, **records** where it stopped, and a run handed that record **re-enters that
row with the earlier pass's cursor** rather than a fresh one — which makes `TableRowCursor` the
second thing a table pass hands the next, alongside
[`TableSetup`](2026-07-27-the-table-engine-settles-some-things-once-per-table-not-once-per-pass.md).

The record is `Html/Core/Fragmentation/TableBreakToken.cs`, published on `CssBox.TableContinuation`,
which replaces `CssBox.UnfinishedTableCells` — the cell list [#452
recorded](2026-07-27-the-table-row-loop-notices-a-cell-that-did-not-finish.md) is one of its fields
rather than a second record of overlapping state. It carries the row to re-enter, the slot the break
fell in, the cursor's widest edge, the rowspan bookkeeping keyed by **absolute** body-row index, and
each unfinished cell's own token; a continuation hands those tokens back to their cells, so each
continues where *it* stopped.

**Behaviour-neutral, and measured rather than argued**: 69/69 showcases byte-identical against `main`
at `0c596fc`, 6,753 tests, 96 CLI tests, 100% diff coverage, zero-warning solution rebuild.

## The headline: this step is *not* unreachable, and the previous measurement was wrong

The last three steps were neutral because nothing could reach them. This one was expected to be the
same — [#452](https://github.com/jhaygood86/PeachPDF/issues/452) measured ten table shapes and found
that **no cell produces a `PendingBreakToken` while the fragmentainer is detached**, so a row loop
that stops on one could never stop.

A probe that recorded every entry into the new stop, run over the whole suite and the whole showcase
corpus, says otherwise:

| | reached |
|---|---|
| 69 showcases | never |
| 6,753-test suite, from markup | **once** |

The one is `MulticolLayoutIntegrationTests.InsideAnotherEngine_NoContentIsDropped(outerStyle:
"display:table")` — a `columns: 2` container inside a `display: table`. `CssLayoutEngineColumns`
establishes a fragmentation context of its **own** inside the table's monolithic detach, so
`CurrentFragmentainer is { HasOwnBand: true }` while `IsFragmenting` is false, and a column running
out is a break like any other: the `<td>` carries a `BlockBreakToken`. Recorded as
[an invariant](../invariants/fragmentation-a-detached-fragmentainer-does-not-stop-a-nested-engine-from-recording-a-break.md),
because the general form is what matters — **detaching suppresses the page vehicle, not breaking**,
and #452's ten fixtures all nested the interesting content directly in a `<td>` rather than nesting a
container that paginates.

There the row that stops is the table's **last**, so the loop skips no row and the step is still
neutral. That is a fact about the fixture, not about the guard.

## What was therefore split out, and what it costs

Publishing the record as the table's own `PendingBreakToken` — the one line that makes a table
resumable from outside — is **not** in this change. It is
[#464](https://github.com/jhaygood86/PeachPDF/issues/464). Measured on that fixture and reverted,
twelve 40px items in a two-column container on a 120pt page:

| | pages | distinct item rectangles | emitted words |
|---|---|---|---|
| today | 1 | 12 of 12 | 10 |
| record published | 2 | **7 of 12** — five drawn exactly on top of another | 26 |

The resumed pass re-places rows over content an earlier pass already emitted, which is what the
monolithic gate is still there to prevent.

## What was found by running it

**A cell resumed inside one layout generation throws on an inconsistent record.** Handing a cell an
`InlineBreakToken` whose `CompletedLineCount` is smaller than the number of line boxes it already
holds re-finalizes those lines, and `CssLineBox.AssignRectanglesToBoxes` throws `An item with the
same key has already been added`. A *consistent* record works, and that is what pins the per-cell
hand-back: a cell resumed at word 15 of 20 gains exactly its five remaining words, while the same
continuation carrying no record for it adds none. Worth knowing before the gate moves — a resumed
pass is exactly where an inconsistent record would arrive, and nothing guards it. Recorded on #464.

**`ForgetCarriedRecords` was built and then deleted.** The first draft cleared the carried per-cell
records after the row that owns them was re-entered, against a cell being asked again later in the
same pass. There is no such cell: a `CssBox` is a child of exactly one row, and a `rowspan` cell is
reached again through a `CssSpacingBox` placeholder, which is a different box. A guard nothing can
reach is also a guard no test can pin, which is how it rots — so matching by reference and leaving
the list alone is both simpler and honest.

**Carrying the rowspan map is not observable from geometry today**, and the test says so rather than
pretending otherwise. A row that ends a span also holds a `CssSpacingBox` for the same cell, and that
path sets `ExtendedBox.ActualBottom` too, so an empty map and a carried one produce the same
alignment. The carry is still what the map means — absolute keys exist precisely so a cell begun
pages earlier is found — so it is asserted on the cursor and on the published record instead.

## Every part neutralized in turn

A step reachable from exactly one fixture is one where an unpinned part would be invisible. Of the
15 tests in `TableRowLoopResumptionTests` plus the four in `TableCellBreakTokenTests`:

| neutralization | tests failing |
|---|---|
| the row loop never stops | 1 |
| the record is never published | 3 |
| the row loop always starts at row 0 | 1 |
| no per-cell record handed back to the cell | 1 |
| the cursor is restarted rather than continued | 2 |
| `MaxRight` not carried | 2 |
| the rowspan map not carried | 1 |
| the row-group measurement cursor inherits the carried records | 1 |

Two of those tests have explicit controls, because "nothing moved" is satisfied vacuously by a run
that would not have moved anything: `AContinuationThatNamesTheFirstRow_RePlacesEveryRow` is what
makes "rows 0 and 1 stayed put" mean something, and
`AContinuationCarryingNoRecordForACell_EntersItFromTheStart` is what makes "the cell gained exactly
five words" mean something.

## Deliberately not done

- **The monolithic gate stays down**, and the table's `PendingBreakToken` is still never set (#464).
- **[#432](https://github.com/jhaygood86/PeachPDF/issues/432) untouched.** The row loop's band is
  still a counter, and
  [the invariant](../invariants/fragmentation-a-stale-cursor-can-be-load-bearing-compensation-for-a-bad-estimate.md)
  says why correcting it alone regresses four tests.
- **A continuation re-enters every cell of the row that stopped**, including the ones that finished,
  which duplicates their content. css-tables-3 §6.1's per-cell rule needs the fragment model to know
  a cell fragment is empty on the continuation; recorded on #464 rather than half-built here.
- **A record that does not name a row** — anything but a `TableBreakToken` — starts the loop at the
  first body row. That is the total reading, and it is what keeps `TableOncePerTableTests`'
  `BlockBreakToken` continuations meaningful.

## What the next step costs

#464: set the record as the table's `PendingBreakToken`, which is the same step as moving the
monolithic gate, because a resumed table whose cells still cannot see a fragmentainer re-places rows
rather than continuing them. #452 measured the gate at 244 → 121 words for a bare `<td>` and
246 → 135 with a repeating `<thead>`. After that,
[#432](https://github.com/jhaygood86/PeachPDF/issues/432) and
[#439](https://github.com/jhaygood86/PeachPDF/issues/439) fall out of a row loop that can place a
row, see it straddle, and break there instead of predicting it from `EstimateRowHeight`.

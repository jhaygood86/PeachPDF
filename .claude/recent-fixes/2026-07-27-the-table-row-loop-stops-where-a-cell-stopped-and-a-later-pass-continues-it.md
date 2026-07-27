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
at `1408bb7`, 6,779 tests, 96 CLI tests, 100% diff coverage, zero-warning solution rebuild.

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
| the whole test suite, from markup | **once** |

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

A step reachable from exactly one fixture is one where an unpinned part would be invisible. Every
part was neutralized in turn and the table test classes (263 tests) re-run:

| neutralization | tests failing |
|---|---|
| the row loop never stops | 2 |
| the record is never published | 5 |
| the row loop always starts at row 0 | 3 |
| no per-cell record handed back to the cell | 1 |
| the cursor is restarted rather than continued | 4 |
| `MaxRight` not carried | 2 |
| the rowspan map not carried | 1 |
| the row-group measurement cursor inherits the carried records | 1 |
| the closing footer proxy written on a pass that stopped | 1 |
| the row group spanned over rows no pass placed | 1 |
| the break point before the resumed row re-decided | 2 |
| the record not cleared in the constructor | 1 |
| a record naming a row the table does not have honoured anyway | 2 |
| the resumed rows start at the table's own top | 3 |

Three of those tests carry explicit controls, because "nothing moved" is satisfied vacuously by a run
that would not have moved anything: `AContinuationThatNamesTheFirstRow_RePlacesEveryRow` is what
makes "rows 0 and 1 stayed put" mean something,
`AContinuationCarryingNoRecordForACell_EntersItFromTheStart` is what makes "the cell gained exactly
five words" mean something, and the forced-break test runs a *fresh* pass over the same table first,
to show the break really is taken when it is this pass's to take.

**A run that is killed mid-sweep leaves the mutation in the file, and the next sweep then restores
it.** Two neutralization runs were interrupted here and the leaked mutations looked exactly like
order-dependent test flakiness — four tests failing under a broad filter and passing under a narrow
one, twice. The script now snapshots the pristine source once, up front, and restores from that in a
`finally`. Worth knowing before reading any sweep's output as evidence.

## What the review caught, and the two worth carrying forward

Six findings were real and are in the diff. The two with teeth are both "a continuation is not a
fresh run, and two more places had not been told":

**The break point *before* the row a continuation re-enters was being decided twice.** The per-row
check was guarded `i > 0`, which on a continuation resuming at row *k* re-asks
`ForcedBreakFallsBeforeRow(k)` — deterministic from style, so a forced break the stopping pass
already took is taken **again**: the row is pushed a further page down, a second header and footer
proxy are laid out, and `PageBreakBottoms[slot]` is written with this pass's `MaxBottom`, which is the
band top it has just started at — over the slice bottom an earlier pass recorded for that page, which
is the thing #457 went to trouble to preserve. It is now `i > ResumeRowIndex`, which is also what
[§4.4](https://www.w3.org/TR/css-break-3/#break-between)'s "no empty fragmentainer" says from the
other side: the resumed row *begins* this fragmentainer, so nothing precedes it here to break from.

**A continuation's rows were starting at the table's own content top, which is the page it began
on.** The first draft's comment justified this with `CssBox.ResumeInTheNextFragmentainer` — which
returns immediately unless `CurrentFragmentainer is { HasOwnBand: true }`, and
`FragmentainerContext._ownBand` is **null for the page context**. So on the page grid nothing moves a
resumed table, and a box that spans fragmentainers keeps the one `Location` it was placed at by
design. The rows go where the record says instead: `PageTopOf(carried.ResumeSlotIndex)`, floored at
the table's own top. Two consequences fall out — the cursor's `SlotIndex` and its `CurrentY` now name
the same fragmentainer, so `WillCrossPageBoundary` can fire again for the rest of the table, and a
repeating `<thead>`'s proxy lands on the resumed page rather than on top of the first pass's.

The other four: the record is cleared in the constructor rather than in `LayoutCells` (a run that
dies between them would otherwise leave the previous layout's `CssBox` references standing — the same
finding the last review made about `TableSetup`, in the same file); a record naming a row this table
does not have is read as continuing nothing rather than indexing past the end; `cref="TableSetup"`
inside `CssBox` binds to the *property* rather than the type, which is the exact defect the last
review found and this one reintroduced two lines away; and a stray blank line.

Two findings were checked and left. The `SlotIndex` seed is *not* the correction the stale-cursor
invariant warns against — it is seeded once per pass, as a fresh run seeds it from the table's top,
and the deliberate staleness within a pass is untouched. And `BeginLayoutPass` would drop a carried
per-cell record if a continuation ever ran in a new layout generation; that exposure is the generic
path's too and is not new here.

## Deliberately not done

- **The monolithic gate stays down**, and the table's `PendingBreakToken` is still never set (#464).
- **[#432](https://github.com/jhaygood86/PeachPDF/issues/432) untouched.** The row loop's band is
  still a counter, and
  [the invariant](../invariants/fragmentation-a-stale-cursor-can-be-load-bearing-compensation-for-a-bad-estimate.md)
  says why correcting it alone regresses four tests.
- **A continuation re-enters every cell of the row that stopped**, including the ones that finished,
  which duplicates their content. css-tables-3 §6.1's per-cell rule needs the fragment model to know
  a cell fragment is empty on the continuation; recorded as
  [an accepted gap](../accepted-gaps/table-a-fragmented-row-re-enters-its-finished-cells.md) with
  #464, rather than half-built here.
- **Steps 5 and 6 close the table only over the rows a pass placed.** A repeating `<tfoot>`'s closing
  proxy sits under the *last* row, so a pass that stopped earlier would put it in the middle of the
  table on the page it is leaving (measured: at y=36.5 under a row ending at 35.0, with two rows still
  unplaced), and a `<tbody>`'s own box spanned rows still sitting at the origin, giving the group a box
  starting above the table. Step 7 is deliberately not gated: a fragment's bottom *is* the slice
  bottom.
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

# The table engine settles some things once per table, not once per pass

_Landed 2026-07-27._

`CssLayoutEngineTable` is constructed afresh every time it runs and, until now, decided everything
afresh too. That is right for the reasons it usually runs again over a table it has already laid out
— the per-page-width reflow loop, `ShrinkToFit`, a §4.3 relocation laying the subtree out again at
its destination — because each of those is a **fresh layout that has to start from the markup**. A
resumed fragmentainer pass is the one run that is not: it continues a table whose earlier rows are
already emitted. Five things the engine did unconditionally destroy that table when done again on top
of it, and they are the groundwork
[#390](https://github.com/jhaygood86/PeachPDF/issues/390) stage 4 has to lay before the row loop can
act on the record [#452 gave it](2026-07-27-the-table-row-loop-notices-a-cell-that-did-not-finish.md).

The engine is now handed the table's resumption record (`CssBox.LayoutContents` passes `resume`
through `LayoutMonolithicContent`), and what it settles once lives in `Html/Core/Fragmentation/TableSetup.cs`,
held on `CssBox.TableSetup`. **`resume` is null at every table arm of `LayoutContents` for every
document**, so every guard below is unreachable from markup and the change is behaviour-neutral by
construction rather than by luck: 69/69 showcases byte-identical (creation date, `/ID`, `/M`, `/NM`
and subset tags normalized), 6,718 tests, 96 CLI tests, 100% diff coverage, zero-warning solution
rebuild.

## What is once per table now, and what each would have destroyed

| once-per-table | what re-running it does to a continuation |
|---|---|
| `RestoreStructureFromAnyPreviousRun` | pulls the detached `<thead>`/`<tfoot>` back out of its proxies and drops them — and the proxies are the only surviving reference to it, so **every earlier page's repeated header goes with them** |
| `_tableBox.PageBreakBottoms = null` | throws away where the table's slice ended on the pages earlier passes filled, which is what clips its borders there |
| the two whole-table pre-checks | move `_tableBox.Location`, so a table whose earlier rows are already emitted at their own coordinates is moved out from under them |
| `margin-left: auto` centering (in `Layout`) | the same hazard on the other axis, and **not on the original list of four** — the offset is derived from the containing block rather than from where the table is, so applying it twice centers it twice |
| steps 1–3, now `DetachAndMeasureRepeatedRowGroups` | re-positions the one shared source subtree (`CssProxyBox` moves it to the proxy's own position before snapshotting), whose earlier snapshots are already frozen in the emitter |

`InsertEmptyBoxes` is already once-only per box (`CssBox._tableFixed`) and the column widths are
recomputed deterministically from style and word metrics, so neither needs carrying — measured
previously, not re-derived here.

## The one thing that had to change beyond gating, and it is not obvious

**Skipping the restore leaves the proxies in the table's child list, and `AssignBoxKinds` walks that
list.** A proxy inherits its source's style, so its `Display` *is* `table-header-group`: the first one
would be taken for `_headerBox` and the rest classified as body rows, and a proxy has no cells, so
positioning one as a row throws `Sequence contains no elements`. That is
[#353](https://github.com/jhaygood86/PeachPDF/issues/353) arriving from the other direction — the same
crash, reached by *not* restoring rather than by not removing. `AssignBoxKinds` therefore skips
`CssProxyBox` (a no-op on a fresh run, where the restore has already dropped them all) and reads the
header/footer back off the setup instead of looking for them in a tree they are not in.

Two related traps that this diff sits next to and does not spring:
[`ParentBox`'s setter appends](../invariants/fragmentation-cssbox-parentbox-setter-appends-to-the-new-parents-boxes.md),
which is why the restore moves rather than inserts — untouched here; and `RemoveHeaderFooterFromTree`
recording `_headerIndex` from `IndexOf`, which on a continuation would return **-1** and quietly
overwrite the index the group has to go back to. That is asserted directly
(`AContinuation_LeavesWhatTheEarlierPassSettledUntouched`) rather than left to the geometry to notice.

## What was found by running it

**"`resume` is null at every table arm" was re-measured rather than inherited from the map.** A probe
that throws on a non-null record at that arm ran the whole suite (6,718 tests) and the whole showcase
corpus green, so not one table in either receives a record — which is what makes every guard here
unreachable, and is the specific claim the neutrality of this step rests on.

**Every guard is load-bearing, measured by neutralizing each one in turn** — the point being that a
guard nothing can reach from markup is exactly the kind that rots silently. Of the 13 tests in
`TableOncePerTableTests`:

| neutralization | tests failing |
|---|---|
| restore made unconditional again | 3 |
| `PageBreakBottoms` cleared unconditionally | 1 |
| both whole-table pre-checks unconditional | 2 |
| header/footer measurement unconditional | 2 |
| `margin-left: auto` centering unconditional | 1 |
| `AssignBoxKinds` stops skipping proxies | 7 |
| `AssignBoxKinds` stops inheriting the setup | 3 |

**A pre-check that stays put proves nothing on its own**, which is why those two tests run the engine
twice from the *same* position — once as a continuation and once fresh — and assert the fresh run
does move the table. Put the table five points short of the second page's top and the fresh run
relocates it every time; the continuation leaves it exactly where it is. The same control shape is
what makes the centering test mean something.

**The inherited header height is read back out of geometry rather than out of the field.** Two
continuations are given two different `TableSetup.Header.Height` values (40pt and 90pt) and the first
body row's top differs by exactly 50pt. A run that measured the header for itself would place both
rows identically, and no assertion about `_headerHeight` could tell the difference from outside.

## Deliberately not done

- **The monolithic gate stays down and nothing acts on `UnfinishedTableCells`.** This is the step
  *before* the gate, not the one that moves it.
- **The row loop is not resumed.** A continuation still starts the body rows from the top —
  `TableRowCursor` is per-pass state and carrying it is the next step, not this one. Its `MaxRight`
  seed, which steps 2 and 3 raise to the header's right edge, therefore starts at `startX` on a
  continuation; that belongs with the cursor rather than with the setup.
- **[#432](https://github.com/jhaygood86/PeachPDF/issues/432) was not touched.** The row loop's band
  is still a counter, and
  [the invariant](../invariants/fragmentation-a-stale-cursor-can-be-load-bearing-compensation-for-a-bad-estimate.md)
  says why correcting it in isolation regresses four tests.
- **A continuation with no settled setup falls back to a fresh layout** rather than dereferencing a
  null. A table that has settled nothing has no earlier pass to destroy, so that is the safe reading
  as well as the total one.

## What the next step costs

Acting on the record: stop the row loop at the first row that did not finish, publish
`UnfinishedTableCells` as a real `BreakToken` on the table, and resume the loop from
`TableRowCursor` rather than from row 0 — which means the cursor joins `TableSetup` as state a pass
hands to the next, with `RowSpannedBoxes` keyed by **absolute** end-row index so a cell begun pages
earlier is still found. Only then does the monolithic gate move, and only after that do
[#432](https://github.com/jhaygood86/PeachPDF/issues/432) and
[#439](https://github.com/jhaygood86/PeachPDF/issues/439) fall out of a row loop that can place a
row, see it straddle, and break there instead of predicting it from `EstimateRowHeight`.

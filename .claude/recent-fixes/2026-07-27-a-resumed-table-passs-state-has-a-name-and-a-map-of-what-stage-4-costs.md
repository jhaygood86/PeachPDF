# A resumed table pass's state has a name, and a map of what stage 4 costs

`CssLayoutEngineTable.LayoutCells` carried its row loop's state in five locals plus a dictionary. That state
is exactly what a `BreakToken` would have to carry for a resumed fragmentainer pass to pick a table up
mid-flight ([#390](https://github.com/jhaygood86/PeachPDF/issues/390) stage 4), so it is now
`Html/Core/Fragmentation/TableRowCursor.cs` — the resumption record's contents, still held the way the
engine holds them. `LayoutBodyRow` takes the cursor rather than four positional parameters and a returned
tuple, and the header/footer measurement passes get a cursor of their own
(`ForRowGroupMeasurement`), which is what makes it visible that they share only `MaxRight` with the body's:
their rows are not body rows, so neither the row numbering nor the rowspan bookkeeping is theirs.

Behaviour-neutral: all 69 showcases byte-identical (creation date, `/ID`, subset tags and annotation `/M`
normalized), 6,623 tests passing.

## What the mapping found by running the code, not by reading it

**The table engine never receives a resumption record today, and a paginating table takes one pass.** Traced
at `CssBox.LayoutContents`'s table arm: `resume` is null for every fixture, and `container.FragmentainerPasses`
is **1** for a two-page table where the identical text in a `<div>` takes **3**. The whole of a table's
pagination happens inside one fragmentainer pass, which is why none of the driver's machinery applies to it.

**A table cell's text is paginated by the legacy per-word relocation, and the count is small.** A cell
holding 244 words across two pages makes exactly **one** `CssRect.BreakPage` call — once the first
straddling word is relocated to the next band's top, the rest follow from the cursor. The `<div>` control
makes zero (it takes the break-token path). With a repeating `<thead>` the count is 12, because the header
subtree is re-positioned once per proxy and its words are re-asked each time.

**What breaks first if the table simply stops being monolithic, measured.** Swapping
`LayoutMonolithicContent` for `LayoutEngineContent` on the table arm — so cell content sees a live
fragmentainer and `CssLayoutEngine.CreateLineBoxes` takes the token arm — loses half the content, because
`LayoutBodyRow` calls `cell.PerformLayout(g)` and reads only `cell.ActualBottom`: the cell's
`PendingBreakToken` has no consumer and is dropped on the floor.

| fixture (244 words, 300pt page) | emitted today | emitted non-monolithic |
|---|---|---|
| bare `<td>` | 244 | **121** |
| `<p>` inside `<td>` | 244 | **121** |
| two rows, tall first cell | 246 | **123** |
| repeating `<thead>` | 246 | **135** |

Pages drop from 2 to 1 in every case, and the second row's cursor collapses from Y=280 to Y=23 because the
cell's `ActualBottom` is now only its first fragment. So stage 4's first real obligation is a consumer for
that token in the row loop — not the monolithic gate.

**A repeating `<thead>` does not repeat when the break falls inside a cell.** The per-row check is guarded
`i > 0`, so a single-row table whose cell overflows onto page 2 emits its header once. Falls out of the same
missing consumer.

## The defect this turned up, and the correction that is not one

`TableRowCursor.SlotIndex` is a *counter* — one increment per break — so it names the band the loop last
opened, not the band `CurrentY` reached. A row taller than a band leaves them several slots apart, and
`CalculatePageBreakOffset` then returns a **negative** offset: the rows after a 1400pt row on a 260pt band
are placed at `PageTopOf(1) = 280`, **1141pt inside** the row they follow, and painted over it. Filed as
[#432](https://github.com/jhaygood86/PeachPDF/issues/432) and characterized by
`TableRowCursorBandTests.RowsAfterARowTallerThanABand_AreCurrentlyPlacedInsideIt` rather than fixed here.

It was fixed here first, and then unfixed, which is the part worth recording. Deriving the band from the
cursor (`Math.Max(tableTopSlot, SlotStartingAt(CurrentY))`) corrects all three heights exactly — row 1 lands
at 723.0 / 1023.0 / 1423.0, a row that genuinely does not fit the band it reached still starts the next one
down, and `PageBreakBottoms` keys the slice bottom to the right band. It also **regresses four tests**,
because the stale counter is what compensates for `EstimateRowHeight`'s undershoot: the estimate is one line
of text (~17pt for a 35pt row), and only a band the loop has already passed ever notices. Re-derive per row
and every row sits comfortably inside a fresh band, so no break is taken at all — a 40-row 1400pt table on
842pt pages records zero breaks and a repeating header appears on one page instead of five. Recorded as
[an accepted gap](../accepted-gaps/table-row-cursor-names-a-counted-band.md) and
[an invariant](../invariants/fragmentation-a-stale-cursor-can-be-load-bearing-compensation-for-a-bad-estimate.md).

## What a resumed table pass has to carry, verified against the code

Beyond the cursor's own members (`CurrentY`, `MaxRight`, `MaxBottom`, `SlotIndex`, `RowIndex`,
`RowSpannedBoxes` keyed by **absolute** end-row index), four things a resumed pass must *not* redo, each of
which is unconditional today:

- **`RestoreStructureFromAnyPreviousRun`** pulls a detached `<thead>`/`<tfoot>` back out of its proxies and
  removes them. On a resumed pass that destroys every earlier page's repeated header — the proxies are the
  only surviving reference to the detached group.
- **`_tableBox.PageBreakBottoms = null`** at the top of `LayoutCells`. It has to accumulate across passes;
  it is what clips the table's borders per page.
- **The two whole-table pre-checks**, which move `_tableBox.Location`. Re-running them on a continuation
  moves a table whose earlier rows are already emitted.
- **Steps 2 and 3**, which lay the header/footer rows out once to measure them. `CssProxyBox` moves the one
  shared source subtree to its own position before snapshotting, so the source carries only the *last*
  proxy's geometry — re-measuring on a resumed pass re-positions a subtree whose earlier snapshots are
  already frozen in the emitter.

`InsertEmptyBoxes` is already once-only (`CssBox._tableFixed`), and the column widths are recomputed
deterministically from style and word metrics, so neither needs carrying.

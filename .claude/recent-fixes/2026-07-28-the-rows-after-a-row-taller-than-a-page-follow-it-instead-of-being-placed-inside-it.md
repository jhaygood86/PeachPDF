# The rows after a row taller than a page follow it, instead of being placed inside it

_Landed 2026-07-28._

[#432](https://github.com/jhaygood86/PeachPDF/issues/432). `CssLayoutEngineTable`'s row loop tracked the
band it was filling as a **counter** advanced by one per break, so it named the band the loop last
*opened* rather than the band `cursor.CurrentY` had reached. A `<tr>` whose cell held a block taller than
one band carried the cursor several bands past the counter, and

```csharp
cursor.OpenNextSlot(cursor.CurrentY + CalculatePageBreakOffset(container, cursor.CurrentY, slot));
```

reduced to `CurrentY := PageTopOf(slot + 1)` — above where the tall row ended. The rows after it were
placed *inside* it and drawn over its content: 1141pt of overlap for a 1400pt row on a 260pt band.

Two changes, and **the order is the whole point**. The band is now derived from where the cursor is
(`TableRowCursor.BandReached`, a monotonic floor), and the loop no longer decides only from
`EstimateRowHeight`: it places the row and asks `HtmlContainerInt.FallsPast` of the bottom the row really
reached, retracting the placement and placing it again on the other side of the break when the answer
says so.

## The measurement that was stale, and what it cost to find out

[The invariant](../invariants/fragmentation-a-stale-cursor-can-be-load-bearing-compensation-for-a-bad-estimate.md)
said the derivation alone regresses four named tests. Re-run on `77e845d` — after #488, #495 and #508,
all merged in the two days before this — **the set is different in both directions**:

| test | recorded | measured |
|---|---|---|
| `PageBreakTableKeepWithNextIntegrationTests.LongRepeatingHeaderTable_…` | regresses | regresses — header on **1** page, wants ≥ 3 |
| `RepeatedTableHeaderClipIntegrationTests.ClippedRepeatedHeader_IsPaintedOnEveryPageItRepeatsOn` | regresses | regresses — `HEADERMARKER` never drawn |
| `RepeatedTableHeaderClipIntegrationTests.ClippedRepeatedHeader_ClipsAtItsOwnPagesPosition` | regresses | regresses — **2** proxies, wants ≥ 3 |
| `TableRowCursorBandTests.AManyRowedTableRecordsOneBreakPerBandItFills` | regresses | **passes** |
| `WholeTableRelocationTests.ATableThatBrokeBetweenItsOwnRows_IsNotMoved` | not named | **regresses** — `PageBreakBottoms` null |

The 40-row table's failure was arithmetic coincidence, not mechanism: its rows happen to land where the
estimate is adequate. Its stand-in was never written down. Same lesson #495 recorded and worth re-reading
before trusting any number in an issue: **a measurement is evidence about the tree it was taken on.**

## Place, then correct — and why the prediction stays

The obvious shape is to measure every row's natural height up front and decide from that. It was designed
and rejected, for four reasons found by reading rather than by running:

- `DetachFragmentainer` alone does **not** give an unfragmented height. With no fragmentainer,
  `CssLayoutEngine.FlowBox` takes the `word.BreakPage()` arm — the legacy per-word relocation — so a
  detached measurement returns a height with words teleported. It needs `SuppressWordPageBreaks` as well,
  the way `CssLayoutEngineFlex.PerformLayoutBlockified` does.
- A row's height is not well-defined out of sequence: `LayoutBodyRow`'s `CssSpacingBox` arm reads
  `sb.ExtendedBox.ActualBottom`, geometry written by an *earlier* row.
- A nested `<table>` in a cell would be laid out twice, the second run going through
  `RestoreStructureFromAnyPreviousRun` over proxies the measurement created — the whole of #353.
- Under `UseVariablePageWidth` a cell measured at a provisional Y is measured to the wrong page's measure.

Placing the row answers all of it for free, at the real position, in sequence, with the real
fragmentainer — and costs one extra row layout **per page** rather than per row.

**The predictive arm is kept, deliberately.** Dropping it would let every page-ending text row fragment
its cell instead of moving whole, turning one in-table break into a whole extra driver pass per page.
It is the cheap path for the common case; the correction is what makes the answer right.

The correction declines in five cases, each recorded on `StraddleCorrectionAppliesTo`: a cell stopped
(the row is fragmenting, and retracting would retract a record a cell has published); the row begins the
band (§4.4's "no empty fragmentainer" — and this is #432's own fixture, which is why a row taller than a
band stays and overflows); the next band could not hold it either (§4.3's ladder ends in leaving content
alone, and moving it would walk it down one band per row); the row ends a `rowspan` (its alignment has
already deep-offset a cell belonging to an earlier row, which is not this row's geometry to take back —
the `ApplyCellVerticalAlignment` hazard #488 recorded); and a fragmentainer with a band of its own, where
the page grid describes nothing.

## What the retraction has to take back

`TableRowCursor.BeginRow`/`Retract` restore `MaxBottom`, `MaxRight`, and **truncate `RowSpannedBoxes`** —
`LayoutBodyRow` appends to the list for the row a `rowspan > 1` cell *ends* on, so a retracted row would
otherwise leave an entry a row several fragmentainers later aligns against twice. A key the row created
outright is removed rather than left empty, so `Continuation()` never publishes one. `FinishedCells` is
cleared by re-assigning `RowIndex`; `UnfinishedCells` cannot have grown, because the correction is gated
on `!cursor.Stopped`. The box tree goes back through `PassRewind.RollBackTo(null, row.Boxes)`, which is
the existing primitive: without it the abandoned placement's per-line rectangles stay on the cells, keyed
by line boxes nothing points at any more.

## Measured, `main` vs this change

| | `main` | this change |
|---|---|---|
| row 1 after a 700 / 1000 / 1400pt row | **280, 280, 280** | **723, 1023, 1423** |
| overlap at 1400pt | **1141pt** | 0 |
| `PageBreakBottoms` for that table | `{0: 1421.5}` — a slice bottom five bands below its key | none; nothing broke |
| `paged_media_table_tall_row` pages | 2, rows drawn over the block | **3**, rows below it |

Rasterized with PDFium and MuPDF on every page and read; both agree. On `main` the second page shows the
header and all three following rows painted on top of the tall row's gradient.

**`PageBreakBottoms` is keyed by the band being filled**, which is right by construction now the band is
derived. `FragmentPainter` reads the record by the fragment's own fragmentainer index, so the old key was
the second half of the defect.

## The boundary tolerance, which only Windows produced

The first draft of `BandReached` was `Math.Max(SlotIndex, SlotStartingAt(CurrentY))`. `SlotStartingAt`
applies the **top-edge** convention — a coordinate within `PageBoundaryEpsilon` *above* a boundary begins
the later band — which is right for a box placed at a band top and carrying arithmetic noise, and wrong
for this cursor, which is a *derived* position: the last row's bottom plus the table's vertical spacing.

`AnOrdinaryTableBreaksBetweenTheBandsItsRowsActuallyFill` failed on **Windows only**, at 279.5 against an
expected 280. The second row ends half a point higher there than it does on Linux and macOS, so the
cursor landed inside the epsilon, the loop believed it was already filling band 1, took no break, and the
table crossed a page boundary with **no slice bottom recorded for the page it left**. Nothing is visibly
misplaced — the row is half a point from where it should be — but the border clip and any repeated header
for that page are gone, which is the class of defect the whole change is about, reintroduced by a
tolerance.

The gate is now `FallsPast(CurrentY, BandOfSlot(SlotIndex))` — the same question the correction itself
asks, per [one membership question, one tolerance](../invariants/fragmentation-one-membership-question-is-asked-with-one-tolerance.md).
Pinned by `TableRowLoopResumptionTests.ACursorWithinTheBoundaryToleranceOfTheNextBand_HasNotReachedIt`,
which asserts on the coordinate rather than through a document, because whether a fixture lands inside
the half point is a fact about font metrics. Changed no showcase.

**What to take from it:** a three-platform CI matrix is the only thing that produced this, and the
failure looked like a rounding disagreement rather than a missing record. `SlotStartingAt` /
`SlotEndingAt` / `FallsPast` are three different questions, and picking one because it is nearby rather
than because the edge is the kind it names is how a tolerance becomes a defect.

## What this change gives up, and it is filed

A repeating `<thead>` is created only before the loop and inside `TakeBreakBeforeRow`, so it lands on the
page the table begins on and on every page a break opens — **and on no other**. Before this, the bogus
break after a too-tall row drew one on that page too, over the row's own content at a band the content
had passed. Removing the break removes the accidental header.

The gap is older than the change (any table spanning pages without breaking has always had one header);
it is now easy to see. [#509](https://github.com/jhaygood86/PeachPDF/issues/509), with
[a gap file](../accepted-gaps/table-a-repeated-header-is-only-carried-onto-a-page-a-break-falls-on.md)
and a reader-facing limitation in `docs/html-css-support.md`. Not fixed here because "a break was taken"
is the wrong question and the right one — which bands the table's slice covers — is the same set
`PageBreakBottoms` needs entries for, so half of it trades a missing header for a border drawn across the
middle of a row.

## What the review caught, and two of them are filed

**A row that ends a `rowspan` is left straddling, and the docs edit said the opposite.** The correction
declines there because `ApplyCellVerticalAlignment` has already deep-offset the spanning cell — a child
of an *earlier* row — and neither `Retract` nor `PassRewind` restores geometry the row does not own. The
first docs wording ("a row that would straddle and could fit a page of its own is already carried onto
the next page whole") is a specific, checkable claim and is false for exactly that case; the old vaguer
sentence was less wrong. [#511](https://github.com/jhaygood86/PeachPDF/issues/511), with
[a gap file](../accepted-gaps/table-a-row-that-ends-a-rowspan-is-not-relocated.md). The guard is exact
rather than conservative — `InsertEmptyBoxes` and `LayoutBodyRow` key the spacer and the registration the
same way — with one hole at `LayoutBodyRow`'s `currentColumn >= _columnWidths.Length` early `break`.

**A forced break inside a cell is not always a stop, and the gap it leaves is read as height.** The
going-in reading was that `break-before: page` inside a `<td>` sets the cell's `PendingBreakToken`, so
`cursor.Stopped` is true and the correction never runs. That is only true for *some* positions: measured,
a break in row 3's cell was serviced by relocating the block, the cell did not stop, and `MaxBottom -
rowTop` came back as 144pt for 40pt of content — gap included — which is then what the destination's
capacity is compared against. **Re-measured against `77e845d`, `main` drops the same break in every
fixture that could be built**, so this is a route rather than a regression;
[#512](https://github.com/jhaygood86/PeachPDF/issues/512), the same class as
[#434](https://github.com/jhaygood86/PeachPDF/issues/434) one level out.

**Three claims in the first draft were simply wrong**, and are the kind a future reader would trust:
`SlotIndex` is "never read on its own" (it is, in five places, each asking something else); `#break-between`
cited as §4.4 when this file links that anchor as §3.1 nine times elsewhere; and `#possible-breaks` cited
as §4.1 in the docs while the repo cites it as §4.3 everywhere else, including #432's own body.

The review also **destroyed the working tree** by reverting the changed files to `main` to test a
hypothesis and staging the revert. Nothing was lost that was not recoverable from the commit, but a
review agent turned loose in the repository is a writer, and the two uncommitted edits at the time were
gone. Commit before reviewing.

## Deliberately not done

- **The two whole-table pre-checks still decide from `EstimateRowHeight`.** Feeding them real heights was
  planned and dropped: `WholeTableRelocationTests.ATableTheEstimateMisjudged_IsMovedWholeOnceItsHeightIsKnown`
  asserts `Location.Y == 842` exactly, reached today by the epilogue's §4.3 mover; an accurate pre-check
  fires instead and lands the table at 843 through `GetVerticalSpacing()`'s −1, which is the nudge
  `ARelocatedCollapsedBorderTable_IsNotNudgedPastThePageTop` exists for.
  [The gap file](../accepted-gaps/table-pre-checks-decide-from-an-estimate.md) already argues a misjudged
  pre-check is not a gap, because the epilogue asks the real question of every table.
- **`WillCrossPageBoundary` still uses a bare `>` where the rest of the fragmentation code uses
  `FallsPast`'s shared epsilon.** The new arm uses `FallsPast`. Aligning the old one is a behaviour change
  (the prediction fires 0.5pt later) and belongs in its own commit with its own showcase diff.
- **No intermediate `PageBreakBottoms` entries for the bands a too-tall row overflows through.** There was
  no break on them; an invented entry would draw a bottom border across the middle of a row. Same answer
  #509 needs.

## Evidence

Full net8.0 suite green (6,885 passed, 9 skipped, up 3), net10.0 green, CLI suite green (96),
`dotnet build PeachPDF.slnx -t:Rebuild` with zero warnings. **70 of 70 existing showcases
byte-identical** to `main` after normalizing creation date/time, `/ID`, subset tags and the annotation
`/M`/`/NM` — the change is confined to the case the issue is about. One showcase is new.

Tests: `TableRowCursorBandTests`'s characterization theory became
`RowsAfterARowTallerThanABand_ContinueBelowIt`, asserting the three positions *and* that no row starts
above the bottom of the row before it, plus `ARowTallerThanABand_IsNotMovedAndRecordsNoBreak`,
`NoRowStraddlesABandBoundaryWhenTheEstimateUndershootsIt`,
`EverySliceBottomIsKeyedToTheBandItFallsIn`, `ARepeatedHeaderIsNeverDrawnOverTheRowBeforeIt` and
`ARowThatOpensARowspanCanStillBeCorrectedOntoTheNextBand`. **Every one of them fails on `main`'s
engine — seven test cases, counting the theory's three — and passes with this change**, checked by
reverting the two source files and re-running; the three "must not lose" tests in the same class pass
both ways. Two more in `TableRowLoopResumptionTests` pin the cursor directly:
`RetractingARowsPlacement_TakesBackOnlyWhatThatRowAddedToTheRowspanMap` and
`ACursorWithinTheBoundaryToleranceOfTheNextBand_HasNotReachedIt`. The header one is worth
keeping: it is the assertion `RepeatedTableHeaderClipIntegrationTests` was missing — that fixture's third
page existed only because the header proxy was drawn 2pt *inside* the row before it, #432 at small scale,
green the whole time.

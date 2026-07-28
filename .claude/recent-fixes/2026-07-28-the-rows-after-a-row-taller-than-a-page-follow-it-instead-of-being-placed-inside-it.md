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

Full net8.0 suite green (6,882 passed, 9 skipped), CLI suite green (96),
`dotnet build PeachPDF.slnx -t:Rebuild` with zero warnings. **70 of 70 existing showcases
byte-identical** to `main` after normalizing creation date/time, `/ID`, subset tags and the annotation
`/M`/`/NM` — the change is confined to the case the issue is about. One showcase is new.

Tests: `TableRowCursorBandTests`'s characterization theory became
`RowsAfterARowTallerThanABand_ContinueBelowIt`, asserting the three positions *and* that no row starts
above the bottom of the row before it, plus `ARowTallerThanABand_IsNotMovedAndRecordsNoBreak`,
`NoRowStraddlesABandBoundaryWhenTheEstimateUndershootsIt`,
`EverySliceBottomIsKeyedToTheBandItFallsIn` and `ARepeatedHeaderIsNeverDrawnOverTheRowBeforeIt`. **All
seven fail on `main`'s engine and pass with this change**, checked by reverting the two source files and
re-running; the three "must not lose" tests in the same class pass both ways. The header one is worth
keeping: it is the assertion `RepeatedTableHeaderClipIntegrationTests` was missing — that fixture's third
page existed only because the header proxy was drawn 2pt *inside* the row before it, #432 at small scale,
green the whole time.

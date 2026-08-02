# A rowspan cell's own overflowing content is fragmented per band, not stretched across them

Closes [#521](https://github.com/jhaygood86/PeachPDF/issues/521).

## What was actually wrong

`CloseSpanningCell` (`CssLayoutEngineTable.cs`) fragments a `rowspan` cell's box at a band boundary
whenever its *span* crosses one (#511). It declined to do so — leaving the box stretched across the
boundary, the pre-#511 defect — wherever the cell's own *content* also reached past the band it opened
in, out of a documented concern (the deleted accepted-gap file, `table-a-rowspan-cells-own-content-is-not-continued-past-its-band.md`)
that stating a continuation shell over a band the content already occupies would displace that content —
measured at the time of #511/PR #523 as ~100 words "claimed by no page."

## What the investigation found

That concern no longer holds against today's code, and reproducing it was the load-bearing step before
touching anything: removing the guard's decline clause and running the full suite (7392 tests) produced
**zero failures**, not the ~100-word loss the gap file recorded. Reading `FragmentEmitter.ShellIn`'s call
site confirms why — a stated shell is consulted only when a band's real per-pass walk found `lines`,
`words`, and `children` all empty; a band that already holds real content for the box never reaches that
branch at all, so stating a redundant shell over it is discarded outright, never a risk. The likely
explanation is that a later, unrelated commit (repeating-group slicing in #524, or the fragment-emission
pruning fixes in #586–#588) closed whatever actually caused the loss the gap file measured — the accepted
gap had simply never been revisited since.

The genuinely new work was a second gap the removal exposed: where the cell's content reaches into the
*same* band the row ending the span lands in, there is no later, empty band left to state a shell over at
all — closing the box using the `cellSlot`-band arithmetic (`PageBottomOf(cellSlot)`, clamped up to
`contentBottom`) closed it **above** the rest of that band's rowspan, since `contentBottom` there can be
smaller than `rowMaxBottom`. Measured concretely on `paged_media_table_rowspan_break`'s own new Q4
fixture: the rowspan's remaining rows in that band lost their tint and border entirely — a real
regression from the pre-fix stretched box, which at least covered every row the span still named. Fixed
by asking `SlotEndingAt(contentBottom)` against the row's own slot: when the content's band is `>=` the
ending row's, the close is `Math.Max(rowMaxBottom, contentBottom)` instead, with `PageBreakBottoms[slot]`
*created* (not merely raised, unlike the `cellSlot` entry) — nothing else necessarily writes a
slice-bottom for the band the row loop is still filling, unlike an earlier band it has already broken
away from via `TakeBreakBeforeRow`.

A post-change review of that first version found a genuine regression in it before it merged: the
`contentSlot >= slot` branch also wrote `PageBreakBottoms[slot]` unconditionally, including on a pass
where the row loop was still filling that same band with *later* content — corrupting the table's own
bottom-border clip for content that hadn't been laid out yet. Removed entirely; that branch only needs to
raise `rowMaxBottom`, not record a slice-bottom the row loop hasn't finished deciding. The review also
found the `else` (`contentSlot < slot`) branch's close could reach several bands past the content's own
band by anchoring on `cellSlot` instead of `contentSlot`; both the `PageBottomOf` lookup and the
`StateSpanningCellContinuation` call now anchor on `contentSlot`.

Verifying the user-visible fix by rasterizing the Q4 fixture surfaced two more defects, both pre-existing
and neither specific to page-boundary closing:

- **The sibling cells in the row that ends the span didn't raise their own height to match the spanning
  cell.** `TableRowCursor.MaxBottom` deliberately excludes a spanning cell from a row's height (needed for
  the straddle correction to still move the row), so a plain `<td>` beside a still-overflowing rowspan
  cell closed at the smaller value — its bottom border cut across the rowspan cell rather than meeting its
  edge. Fixed with a pre-pass in `LayoutBodyRow`, run before the row's ordinary vertical-alignment loop,
  that raises `rowMaxBottom` to the spanning cell's own `contentBottom` wherever its content reaches at
  least as far as the row's own band — so every cell the row aligns sees the same final bottom.
- **`CloseSpanningCell` was never being called at all for a cell that took more than one resumption pass
  to finish**, regardless of the fix above. The vertical-alignment loop's dispatch asked
  `ResumedFromAnEarlierPass`/`stoppedCells` before asking whether the cell was in this row's own
  `boxesThatEndOnRow` — and `ResumedFromAnEarlierPass` matches by reference against a carried record
  seeded when the cell's own *opening* row was first resumed, several rows earlier, a record that is never
  cleared once consumed. Reordering the two checks (`boxesThatEndOnRow` first) fixed it; a cell reaching
  that branch belongs to an earlier row by construction, so the stale resumed-match is never the right
  answer there.

## What was deliberately not done

- No new break-token machinery (an `OverflowedSpanningCells`-style field on `TableBreakToken`, mirroring
  `FlexColumnBreakToken`) was added, despite that shape looking plausible before the investigation. It
  would have been solving a problem that doesn't exist: the cell's content already stops and resumes
  correctly across real per-pass fragments through the table's ordinary continuation
  (`TableRowCursor.UnfinishedCells`/`Continuation`) — the same channel every other overflowing cell uses,
  rowspan or not. The only things missing were the cosmetic box-close question this fix addresses, the
  sibling-alignment question, and the dispatch-ordering bug that kept the close from running at all.
- A separate, pre-existing defect found while verifying the fix's own new test
  (`ASpanningCellWhoseContentOverflowsItsBand_IsFragmentedAcrossEveryBandItOccupies`) was left alone and
  filed as [#590](https://github.com/jhaygood86/PeachPDF/issues/590): `FragmentEmitter.RecordChain` only
  walks a `BlockBreakToken`'s linear `ChildToken` chain, so it never marks a `TableBreakToken`'s per-cell
  continuations — any table cell whose own content spans multiple *real* fragments (rowspan or not)
  reports every one of those fragments as owning both its own top and bottom edge, repainting
  `box-decoration-break: slice`'s border/background on every page rather than slicing it. Confirmed
  independent of rowspan and of this fix on a plain, non-rowspan multi-page `<td>`.

## Evidence

- Full `net8.0` suite: 7395 passed, 0 failed, 9 skipped (7393 after the original fix, 7395 after the
  correction above added the two discriminating tests).
- `diff-cover` against `origin/main`: 100% on the changed lines.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- Two-renderer (PDFium + MuPDF) rasterization of `paged_media_table_rowspan_break`'s Q4 fixture, viewed
  directly: the spanning cell's tint/border now correctly cuts off at the page foot with no border drawn
  there, resumes at the next page's top with no border repainted there either, and — for the sibling
  alignment fix — the row that ends the span (December) shows both the sibling `<td>`s' and the spanning
  cell's bottom borders meeting at the same height, on both renderers.
- Dedicated regression tests: `ASpanningCellWhoseContentFinishesInTheSameBandTheSpanEndsIn_ClosesAtTheRowsOwnBottom`
  (same-band close) and `ASpanningCellWhoseContentFinishesSeveralBandsBeforeTheSpanEnds_ClosesAtItsOwnBand`
  (`contentSlot < slot` case), each verified to fail against the corresponding defect before its fix
  landed.
- **Correction, found by a post-merge review after this fix had already landed**: the two tests above both
  place the spanning cell in a non-last column, so the row that ends the span always reaches it through the
  `CssSpacingBox` arm — which reaches `CloseSpanningCell` regardless of how the vertical-alignment loop's
  dispatch is ordered. Reverting *only* the dispatch-ordering fix left both tests passing unmodified, which
  directly contradicted this file's original claim that they guarded it. Reverting *only* the sibling-
  alignment pre-pass also left them passing, because the same `CssSpacingBox` arm's earlier per-cell
  tracking loop had already folded the spanning cell's height into `rowMaxBottom` before the pre-pass could
  matter. Two further, narrower tests were added to actually discriminate each fix on its own:
  `ASpanningCellInTheLastColumn_StillClosesAfterMultipleResumptionPasses` (last-column span, so only
  `RowSpannedBoxes` reaches it — verified to fail against the pre-dispatch-fix code) and
  `ASpanningCellsOverflowIntoTheEndingRow_RaisesItsShortSiblingToMatch` (a genuinely one-line sibling, not a
  tall `<div>` whose own height happened to already exceed the spanning cell's — verified to fail with just
  the pre-pass disabled). A second, related, currently-shipping defect was found during this same
  investigation and filed as [#593](https://github.com/jhaygood86/PeachPDF/issues/593): the loop's
  `FinishedOnAnEarlierPass` guard, one line above the `boxesThatEndOnRow` check the dispatch fix reordered,
  has the identical stale-match shape whenever an unrelated *sibling* in the cell's own opening row needed
  its own resumption pass — left unfixed here since it is a distinct, pre-existing defect, not a regression
  from this change.

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

## What was deliberately not done

- No new break-token machinery (an `OverflowedSpanningCells`-style field on `TableBreakToken`, mirroring
  `FlexColumnBreakToken`) was added, despite that shape looking plausible before the investigation. It
  would have been solving a problem that doesn't exist: the cell's content already stops and resumes
  correctly across real per-pass fragments through the table's ordinary continuation
  (`TableRowCursor.UnfinishedCells`/`Continuation`) — the same channel every other overflowing cell uses,
  rowspan or not. The only thing missing was the cosmetic box-close question this fix actually addresses.
- A separate, pre-existing defect found while verifying the fix's own new test
  (`ASpanningCellWhoseContentOverflowsItsBand_IsFragmentedAcrossEveryBandItOccupies`) was left alone and
  filed as [#590](https://github.com/jhaygood86/PeachPDF/issues/590): `FragmentEmitter.RecordChain` only
  walks a `BlockBreakToken`'s linear `ChildToken` chain, so it never marks a `TableBreakToken`'s per-cell
  continuations — any table cell whose own content spans multiple *real* fragments (rowspan or not)
  reports every one of those fragments as owning both its own top and bottom edge, repainting
  `box-decoration-break: slice`'s border/background on every page rather than slicing it. Confirmed
  independent of rowspan and of this fix on a plain, non-rowspan multi-page `<td>`.

## Evidence

- Full `net8.0` suite: 7392 passed, 0 failed, 9 skipped.
- `diff-cover` against `origin/main`: 100% on the changed lines.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- Two-renderer (PDFium + MuPDF) rasterization of `paged_media_table_rowspan_break`'s new Q4 fixture,
  viewed directly: the spanning cell's tint/border now correctly cuts off at the page foot with no
  border drawn there, and resumes at the next page's top with no border repainted there either —
  matching css-tables-3 §6.1's "top borders must not be repainted in continuation fragments."
- A dedicated regression test for the same-band close
  (`ASpanningCellWhoseContentFinishesInTheSameBandTheSpanEndsIn_ClosesAtTheRowsOwnBottom`) was verified
  to actually fail against the fix's first (incomplete) version before the `PageBreakBottoms[slot]`
  creation was added, and to fail again when the `contentSlot >= slot` branch is forced off — confirming
  it is a real regression guard, not a tautology.

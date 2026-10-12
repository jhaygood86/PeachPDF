# Unplaced words no longer make a re-laid-out paragraph reopen every page

## The load-bearing idea

`CssBox.OwnGeometryTop()` is the document Y the emitter reopens fragmentainers from when a box's geometry is about to be
replaced (`RectanglesReset` -> `NotifyGeometryChanged` -> `HtmlContainerInt.InvalidateEmittedFragmentsFor` ->
`FragmentEmitter.InvalidateFrom`). It took the minimum over the box's line rectangles **and every word**. A paragraph that
`orphans`/`widows` pulls back and re-lays out still holds the words past the cut with
`AwaitsTheNextFragmentainer` set, and those words are deliberately not parked at a coordinate (see that property), so
their `Top` is whatever it was: `0` for a word that was never placed. One such word put the box's "top" at the top of the
document, `PageIndexOf(0)` was slot 0, and `InvalidateFrom` re-opened **every slot from the first to the current one**.

Each re-opened slot is then re-emitted by `CatchUpStaleSlotsBehind` with a walk of the whole document. Reopened slots per
event grow with pages, the events grow with pages, and each walk grows with the document: cubic.

`OwnGeometryTop()` now skips words that await the next fragmentainer. If that leaves nothing at all (no rectangles and
every word awaiting) it falls back to the old minimum over all words, so that case behaves exactly as before.

## What was found by running it, not by reading it

- A document that took 17s for 131 pages had only **84** `InvalidateFrom` calls. The cost was never the number of
  invalidations; it was that each reopened ~15 slots on average and every one of them was re-emitted at ~9,800 `BuildDraft`
  visits. Measured with temporary counters: `BuildDraft` visits from catch-up 159k (33 pages) -> 1.09M (66) -> 12.5M (131),
  i.e. 7x then 11.5x per doubling.
- Every call passed `throughSlot == null`, so "reopen everything after" looked like the defect; it is not. A real
  relocation legitimately reopens everything after it. The defect was the *start* of the range.
- Logging the reopened box showed `rectMin` at slot ~30 while `wordMin` was `0`, and every zero-`Top` word was
  `AwaitsTheNextFragmentainer` with `Line == null`.
- A bisect on the CSS (no `break-after`, no stylesheet, no inline emphasis) never moved it; `orphans:1; widows:1` did
  (17s -> 4.7s) because no paragraph is then pulled back. That was the pointer to the rewind path.

## Evidence

- Output is unchanged: the same words and the same drawing operations on every page, compared with PyMuPDF
  (`get_text("words")` + `get_drawings()`) for 33-, 66- and 131-page prose documents and a widows/orphans-1 document,
  built with and without the change.
- Prose, Release CLI, matched generated document: 131 pages 19.3s -> 4.8s; 1,315 pages did not finish in 50 minutes -> 27-33s,
  peak working set 1.3GB.
- `StaleSlotReopeningCostTests`: a `BuildDraftCalls` guard (4x the paragraphs may cost < 8x the box visits; without the fix
  52k -> 1.1M visits, ~21x) and three `OwnGeometryTop` unit tests. Both the guard and the main unit test fail without the fix.
- Full suite green on net8.0 in Release; whole solution `-t:Rebuild` has 0 warnings.

## What this did not fix

Multi-column documents are unaffected (`multicol` 1,272 pages still takes ~179s); that is a different cost and needs its
own profile. Prose is now ~5x Typst's time at 1,300 pages, a constant factor rather than a growth rate.

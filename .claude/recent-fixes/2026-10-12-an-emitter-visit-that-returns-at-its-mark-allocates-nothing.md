# An emitter visit that returns at its "emitted nothing" mark allocates nothing, and an ordinary box is walked without an iterator

Follows [the unreached-boxes fix](2026-10-12-a-rewind-does-not-mark-the-unreached-half-of-the-document-as-reached.md), which made
the not-yet-reached half of a long document return at its mark instead of being recursed into. That left the visit itself as the
cost: each page still calls `BuildDraft` on every such child, and each call paid for work that only a box that gets walked needs.

## What changed

- **`BuildDraft` is split in two.** The front half keeps every early exit (display none, an absolute box held by a later column,
  line-clamp suppression, the displacement, `MayBeObservedEmpty`, and the "emitted nothing here" mark) and calls
  `BuildDraftCore` for the rest. The reason is a C# detail, not tidiness: a method whose lambdas or local functions capture its
  parameters gets its closure class allocated **when the method is entered**, not where the lambda is first used. The
  allocation profile put `<>c__DisplayClass97_0` at 5.4% of everything allocated at 1,315 pages - one per visit, including the
  millions of visits that returned at the mark on the first lines. The closure now exists only in the core.
- **The mark check moved ahead of the fixed-position offset and size, `ComputeInlineExtentDelta`, `PageRegionOf` (which
  allocates a `FragmentRegion`) and `ContentStaysInOneRun`.** It needs only `isFixed`, the displacement and the capture, so this is a
  pure reorder; none of the skipped computations has a side effect.
- **`ChildrenOf`'s ordinary-content walk is two helpers** (`OrdinaryChildrenStart`, the live-child cursor; `OrdinaryChildIsVisited`,
  the placeholder / ahead-of-slot / unplaced-float filter), used by the iterator and by a plain loop in `BuildDraftCore`. A box
  that is not a rowspan placeholder and holds no captured instance at the slot - nearly every box - is walked by the loop; the
  iterator still serves everything else, so its state machine and closure are no longer allocated once per visited box.
  The helpers are the code that was in the iterator, moved, so the two paths cannot drift.

## What was found by running it

- `ChildrenOf`'s state machine was 4.7% of allocation and its closure 0.7%, `BuildDraft`'s own closure 5.4% (prose, 1,315 pp).
- After this, the emitter's own frames (`EmitSlot`, `EmitPass`, `BuildDraftCore`, `Finish`, `CatchUpStaleSlotsBehind`) are ~8% of
  the allocation of a 1,315-page prose document (they were 65% before the unreached-boxes fix, 22% at 131 pages). What is left
  is text shaping (`PlacedGlyph[]` alone is 13%, ~30% inclusive of everything under `Shaper.Shape`) and layout.

## Evidence

- Output identical to the build without it: same words and drawing operations on every page for 21 documents - 131-page prose,
  80-page table, 100-page invoices, 127-page two-column, math, images, two small documents, the prose sweeps (default and
  `widows/orphans: 1`), and two-column variants (3 columns, `orphans/widows: 3`, `break-inside: avoid`, `column-fill: auto`,
  `column-rule`, `column-span: all`) - PyMuPDF `get_text("words")` + `get_drawings()`.
- In-process, warm, allocated per render (this change alone, on top of the unreached-boxes fix): 1,315-page prose 10.0 -> 8.9 GB,
  16.3s -> 14.7s; 131-page prose 952 -> 892 MB; 80-page table 1,263 -> 1,172 MB; 100-page invoices 480 -> 440 MB; 127-page
  two-column 2,044 -> 1,212 MB (-41%, 2.02s -> 1.78s) **without** the multi-column unheld-children skip. With that skip
  (#1718, merged since) underneath, the two-column gain is small - 1,106 -> 1,073 MB at 127 pages, 13.2 -> 12.5 GB at 1,272 -
  because most of those visits were the walk that skip already removed. Do not read the 41% as additional to it.
- Full suite green on net8.0 in Release; whole solution `-t:Rebuild` has 0 warnings. With `PEACHPDF_VERIFY_FRAGMENT_PRUNING=1`
  the failing set is the 9 tests that already fail on `main` plus `StaleSlotReopeningCostTests.EmissionWork_*`, a box-visit count
  the oracle's unpruned reference walk inflates (fails identically without this change).
- No new test: this is a refactor whose contract is "identical output", which the comparison above and the existing emitter,
  multi-column, table and pruning-parity suites pin; an allocation-count assertion would be platform- and parallelism-sensitive.

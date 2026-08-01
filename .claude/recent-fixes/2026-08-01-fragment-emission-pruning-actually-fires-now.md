# Fragment-emission pruning actually fires now: a global epoch was retiring every mark

Follow-up to #581 (itself filed from #584's "where it stands" section) and the parent investigation,
#572. This environment had the actual `dictionary.mhtml` fixture (the real css4.pub Icelandic
Dictionary archive) for the first time in this issue's history — every prior session reasoned from
synthetic micro-benchmarks or code inspection alone. That let the "prime suspect" #581 and #584 both
named — `PassRewind.RollBackTo`'s per-child reset over a multi-column container's whole remaining
child list — actually be measured, not just reasoned about again.

## The prime suspect was wrong

Instrumented every call site reaching `CssBox.DiscardEmittedNothing()` and ran the real document
through `PdfGenerator.GeneratePdf` (Release, `PageSize.Letter`, matching #572's own reproduction
config). `PassRewind.RollBackTo`'s cumulative cost across the whole render: **30 milliseconds**. The
`_awaitingRefill` guard #579 added already makes 96% of its calls free no-ops; it was never the
dominant cost on the real document, only on the synthetic single-container micro-benchmark #573 was
argued from.

## The real cause: a global epoch, not a scoped one

`FragmentEmitter._observationEpoch` was a single counter, bumped once per `InvalidateFrom` call (a
reopened, already-frozen fragmentainer — the `widows`/`orphans` rewind, mostly, and this document's
`widows: 1; orphans: 1` over ~14,645 mostly 1-3-line entries fires it constantly: 636 times over a
764-page render). Bumping it retired **every** "this box produced nothing" mark in the entire
~255,000-box tree at once, not just marks that could plausibly have been affected by the reopened
range — even though each individual reopened range was tiny (average ~1 slot). Measured on current
`main` (`c339c25`) before this fix: 131 million marks recorded, only 213,000 ever survived to save a
walk; `FragmentEmitter.BuildDraft` was called **279 million times**, 89% of a 2m39s–2m44s wall-clock
render.

## The fix: scope invalidation to what a reopening could actually affect

[InvalidationHistory](../../src/PeachPDF/Html/Core/Fragmentation/InvalidationHistory.cs) replaces the
single counter with an incremental suffix-minimum over every reopening's own `fromSlot`, in order. A
mark recorded after `T` reopenings, naming slot `K`, is still trustworthy iff no reopening *since*
started at or before `K` — answerable in O(1) amortized per query (the backward-walk on `Record` is a
standard incremental-suffix-min maintenance: an earlier slot's minimum is only overwritten while the
newly-appended value is smaller, and stops the moment it finds one already that small, since a
suffix's minimum can only rise as the suffix shrinks). `CssBox.RecordEmittedNothingAt`/
`EmittedNothingAtOrBefore` now take the history instead of a bare int, comparing the box's own
recorded slot against it rather than requiring exact epoch equality with the whole document's current
state.

Alone, this took the render from 2m39s/271GB to **1m30s/171GB** (146M `BuildDraft` calls, down from
279M).

## The second, compounding fix: reading is not writing

That still left 92% of the remaining `BuildDraft` volume (135M of 146M calls) inside
`FragmentEmitter.Finish()`'s stale-slot replay — 531 slots, each walked with `mayPrune: false`
(unpruned) because `_pruningSuspended` gated *both* whether a new mark could be written during an
out-of-order replay (still necessary — the lowest stale slot in a batch might find a subtree "empty"
when its content actually lives in a not-yet-replayed higher slot in the same batch) and whether an
*existing* mark could be read at all. Those are different questions once invalidation is scoped: an
existing mark, already validated against `InvalidationHistory`, states that a box's fragments sit
entirely behind a slot no out-of-order replay could still be filling — reading it is exactly as sound
out of order as in it. `RecordEmptyObservations` already blocks new marks during any `mayPrune: false`
pass on its own (`frontier && mayPrune && !wasSuspended`), so nothing else needed to change to make
that half safe.

Split the two: `_pruningSuspended` keeps gating writes (and the diff-testing oracle's own forced-full
reference walk still needs the same behavior it always had — see below). A new
`_forcingUnprunedReferenceWalk` flag, set only inside `VerifyAgainstTheFullWalk`'s own `BuildDraft`
call, is what the read-side skip check is gated on instead.

Combined with the first fix: **54.9s wall-clock, 118GB allocated, 79.3M `BuildDraft` calls** — a 65%
wall-clock reduction and 56% allocation reduction from the pre-fix baseline on the real document, and
close to the 53.8s this document rendered in before the fragment-tree rewrite (commit `c047bb90`,
per #572's own history). Not pursued further: a third mechanism (allowing *new* marks to be written
during stale-slot replay, bounded to slots at or past the whole batch's own end rather than just the
current one) could close more of the remaining gap, but doing that safely needs its own design and
oracle-backed verification pass rather than a third change stacked onto this one.

## Why the verification oracle mattered here specifically

Both changes touch exactly the mechanism `PEACHPDF_VERIFY_FRAGMENT_PRUNING` exists to protect — three
earlier attempts at the pruning feature itself each silently broke 460-480 tests before that oracle
existed (see `.claude/recent-fixes/2026-07-31-emission-no-longer-rewalks-the-whole-tree-per-page.md`).
Splitting `_pruningSuspended` into two flags in particular is exactly the kind of change that could
silently let the verification build's "full, unpruned reference walk" guarantee erode if the read-gate
were loosened without a dedicated flag — the oracle would have caught it by comparing the pruned and
reference builds directly rather than needing a human to notice.

## Evidence

- Full `net8.0` suite, with and without `PEACHPDF_VERIFY_FRAGMENT_PRUNING=1`: 7384 passed, 0 failed,
  9 skipped, both ways, both before and after removing the temporary measurement instrumentation.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- `diff-cover` against `origin/main`: 100% (21 of 21 coverable lines).
- Before/after wall-clock and allocation figures above, measured via a temporary
  `PEACHPDF_DICTIONARY_BENCH_PATH`-gated block in `PeachPDF.TestHarness/Program.cs` (removed before
  landing) rendering the real `dictionary.mhtml` end-to-end through `PdfGenerator.GeneratePdf` with
  `MimeKitNetworkLoader`, mirroring `PeachPDF.Cli`'s own MHTML-loading path.

Issue #581 can close on this measurement. #572 itself should stay open only if there's appetite for
the stale-replay-write follow-up noted above; the "nearly 8 minutes" the issue reporter saw should now
read as under a minute on the same document.

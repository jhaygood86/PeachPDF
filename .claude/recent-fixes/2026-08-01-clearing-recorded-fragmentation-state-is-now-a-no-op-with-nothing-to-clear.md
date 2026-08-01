# Clearing recorded fragmentation state is now a no-op with nothing to clear

Follow-up to #581 (itself filed from #584's "where it stands" section). `FragmentEmitter`'s three
"clear" methods — `ClearNestedFragmentainers`, `ClearContinuationShells`, `ClearFragmentDisplacements`
— called `CssBox.DiscardEmittedNothing()`/`DiscardEmittedNothingIncludingDescendants()`
**unconditionally**, before even checking whether their own dictionary removal (`_nested`/
`_continuationShells`/`_displacements`) found anything to remove.

`CssLayoutEngineColumns.Layout` calls `ClearNestedFragmentainers(columnsBox, startSlot)` once per
`Layout()` invocation — i.e. once per page a resumed multi-column container continues onto — including
the very first attempt on a fresh page, before that page has filled a single column. Nothing has been
recorded yet for that key at that point, so the call was a guaranteed no-op for the data it clears,
but it still discarded the container's "emitted nothing" observation and, walking its ancestors, every
ancestor's too, up to the first already-clear one. Since that observation is also what
`CssBox.NeverTouchedThisLayout` reads, one spurious call disables *both* of `FragmentEmitter.BuildDraft`'s
pruning conditions for the whole ancestor chain — on every single page a large container spans.

## The fix

Each of the three methods now only discards when its own removal genuinely removed something,
tracked via `Dictionary.Remove`'s own boolean return (the single-key path) or a `removedAny` flag
accumulated across the multi-key path — the same "already in the state this call would produce is a
no-op" shape PR #579 established for `CssBox.ResetForRefill`'s `_awaitingRefill` guard, applied to a
different choke point. This changes nothing about *what* gets removed from the three dictionaries,
only whether the (already-idempotent) invalidation notification fires when there was nothing to
invalidate.

## What was measured, and what was not confirmed

A synthetic multi-page, multi-column fixture (60 and 240 `<div>` items in a `columns:2` container,
`html > body > div#mc` — no deep ancestor chain above the container) showed the fix saves a small,
real number of `CssBox.DiscardEmittedNothing` calls: 2889 → 2873 at 60 items (16 saved, 16 pages),
10917 → 10849 at 240 items (68 saved, 68 pages) — almost exactly one saved call per page, matching the
predicted mechanism precisely. Verified sensitive: a version of the regression tests below run against
the pre-fix code fails every time; against the fix, all pass.

Two things this does **not** claim. First, at this fixture's shallow ancestor depth the saved calls
are a small fraction (~0.5%) of the total discard traffic — most of it is genuine, necessary
invalidation from real per-child reflow (`RectanglesReset`, `Location`/`Size` writes), not this
defect. Second, and more important: this environment has no `dictionary.mhtml` fixture or
`PeachPDF.Benchmarks` project (confirmed absent from the repo), so the issue's own ~195k-marks-per-slot
figure — measured on the real 255,000-box, 31-chapter css4.pub Icelandic dictionary, where a
multi-column container sits several ancestors below chapter-level boxes that are exactly the ones
worth skipping — could not be reproduced or re-measured here. Whether this fix meaningfully closes
that gap on the real document, or whether it is a smaller contributor among others, is not established
by this change alone. Issue #581 stays open pending that measurement.

## Verification

- Two new tests per method in `FragmentEmitterTests.cs`: one confirms clearing with nothing recorded
  leaves an existing `EmittedNothingAtOrBefore` observation on the target box intact (fails without
  the fix), one confirms clearing something genuinely recorded still discards it (guards against an
  over-eager guard breaking real invalidation).
- Full `net8.0` suite, with and without `PEACHPDF_VERIFY_FRAGMENT_PRUNING=1`: 7383 passed, 0 failed,
  9 skipped both ways.
- `dotnet build PeachPDF.slnx -t:Rebuild` — 0 warnings.
- `diff-cover` against `origin/main` — 100% diff coverage.

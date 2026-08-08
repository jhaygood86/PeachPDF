# Stale fragmentainer slots are caught up during forward layout, not left for `Finish()`

Follow-up to `.claude/recent-fixes/2026-08-01-stale-slot-replay-writes-investigated-and-found-not-to-help.md`,
which investigated (and ruled out) making `Finish()`'s own stale-slot replay write new pruning marks.
This is a different fix, upstream of that one: most stale slots should never have reached `Finish()`'s
replay at all.

## The load-bearing idea

`FragmentEmitter.InvalidateFrom` un-freezes an already-emitted slot whenever something moves content that
was already frozen there. Two of its three real callers reach it from inside the driver's own pass loop
(`HtmlContainerInt.LayoutDocument`) as part of re-entering an earlier pass, and both already move the
driver's own resumption point back to what they invalidated — `TryRewindForRunPull` takes `ref int slot`
for exactly this. The third, dominant one does not: `CssBox`'s `Location` setter calls
`OnBlockAxisRelocated` on every block-axis reposition, which un-freezes an already-frozen slot through
`InvalidateEmittedFragmentsFor` when the box being moved already had a fragment — but it has no pass to
re-enter and nothing to hand the driver, so the driver's own `slot` variable never moves. Measured on the
css4.pub Icelandic Dictionary: 695 real (non-early-returned) `InvalidateFrom` calls, 602 of them from this
path (translation), 88 from a multi-column rectangle reset, 5 from a refill — and of the 592 distinct
slots any of them ever invalidated, **all 592** were still stale when `Finish()` started its replay. None
were ever covered by a later `EmitPass` call. `Finish()`'s replay is a full, unpruned walk from the
document root for each one — roughly 145,000 `BuildDraft` calls per orphaned page on this document,
`~44s` of a `~65-71s` CLI render (measured via temporary `Stopwatch` instrumentation, since removed).

The fix does not touch `BuildDraft`, `_frozen`, or any pruning mark — the exact machinery the prior
investigation found couldn't be made both safe and effective mid-replay. Instead,
`FragmentEmitter.CatchUpStaleSlotsBehind(slot)` is called once at the top of every driver-loop iteration,
before that iteration's own pass runs: it re-freezes every stale slot behind the pass about to run, via
plain `EmitSlot(stale, mayWrite: true, frontier: stale >= _lastEmittedSlot)` — the same call `Finish()`'s
own replay already makes for every stale slot, just made the moment the driver would otherwise have moved
past it instead of at the very end. `EmitSlot`, not `EmitPass`: there is no meaningful incoming/outgoing
resumption record for a slot nothing here re-enters as a pass, and none is needed — `Finish()`'s own
replay re-freezes every stale slot without one too, and no test depends on continuation bookkeeping
`Finish()` itself never re-establishes for a slot it heals this way.

## Why this doesn't reopen the ambiguity the prior investigation found

`Finish()`'s replay couldn't safely write new pruning marks mid-batch because a low stale slot's "empty"
observation might be contradicted by that same box's real content sitting at a *higher*, not-yet-rewalked
stale slot in the same batch — layout is over by then, so nothing will ever correct a wrong mark. Called
from the driver loop instead, that ambiguity doesn't exist: everything at or after the pass about to run
has not been touched by this layout generation yet, so nothing still to come can retroactively give a
lower, already-stale slot more content. That is the identical fact `EmitPass`'s own `frontier` argument
already rests on for an ordinary forward slot — a slot caught up here just reaches that state on a later
call than the one that first opened it, rather than the one immediately after.

## What was ruled out along the way

- **A `TryRewindForWidows`-specific fix** (reconstructing the invalidated slot's original entry token and
  re-emitting it via `EmitPass` right after `PassRewind.RollBackTo`) was designed and implemented first,
  based on an initial (wrong) assumption about which invalidation source dominated. Measuring which
  caller actually reached `InvalidateFrom` (via a temporary `CallerMemberName`-based tag, since reverted)
  found `TryRewindForWidows` never fires at all on this document — `TryKeepFewerLinesForWidows`'s own
  "two fragments only" restriction declines every time here, and the render falls back to the ordinary
  whole-box push instead. The widows-specific fix was reverted in favor of the general one; both were
  measured, not assumed.
- **Reading a box's `Location`/`ActualBottom` directly** instead of walking, considered as a cheaper
  alternative to eager re-emission, was ruled out again (it already failed once, per
  `2026-07-31-emission-no-longer-rewalks-the-whole-tree-per-page.md`'s "attempt 2") — this document's
  `.chapter { columns: 2 }` content lives in per-column `BoxGeometrySnapshot`s, not in the box's own
  fields, for the entire body.

## Evidence

- Full `net8.0` suite: 8180-8181 passed (varies by 1 run-to-run), 0 failed beyond a single, pre-existing,
  order-dependent flake in `ContainerQueryLayoutIntegrationTests` (passes standalone every time — see
  `project_container_query_test_flakiness` in memory) — confirmed unrelated by running in isolation both
  before and after this change.
- Same, with `PEACHPDF_VERIFY_FRAGMENT_PRUNING=1`: identical result, zero `PruningDiverged` exceptions.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- `diff-cover` against `origin/main`: 100% (8 of 8 coverable lines) — genuinely exercised (5,287 real hits
  on the re-emission line across the existing suite, confirmed via the raw Cobertura XML, not merely
  reachable).
- css4.pub Icelandic Dictionary via the real `peachpdf` CLI (live URL, Release, net10.0): **~62-66s before
  → ~28s after** (three consecutive runs: 28.1s, 28.0s, 27.9s), comfortably under a 50s target that the
  pre-fix render missed.
- Same document's PDF output: 834 pages before and after, **byte-identical extracted text on all 834
  pages** and **pixel-identical rasterization on all 834 pages** (PyMuPDF, 72 DPI) comparing the pre-fix
  and post-fix renders.

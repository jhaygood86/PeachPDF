# `column-span: all` now breaks the column flow (#602)

`CssLayoutEngineColumns.cs` never read `CssBox.ColumnSpan`, so a `column-span: all` element rendered as
an ordinary in-column box. This closes that gap for a **direct** child of the multi-column container:
the column flow now splits into independently-balanced runs before and after the spanning box, which
renders at the container's full content width.

## The design that worked: model a span as a one-column run, reuse `FillColumns` unchanged

The container's direct in-flow children are partitioned (`BuildSegments`) into alternating "runs" and
`column-span: all` singletons, in document order. Each run is driven through the *existing*
`FillColumns` primitive exactly as before (`LayoutRunSegment` is the old single-run Phase 1/balance-retry
loop, now scoped to one run's own children); a span is modeled as one column that happens to be the
whole container width — `columnCount = 1`, `columnWidth = containerWidth` — so it gets `FillColumns`'s
pagination/break-token/resume handling for free rather than a second, separately-tested code path.

## What actually broke, found only by running it — not by reading the diff

Reading the design, it looked complete. Three separate defects only surfaced by building fixtures and
running them, each with a distinct, non-obvious root cause:

1. **A load-bearing `PassRewind.RollBackTo` call moved to the wrong side of Phase 1.** The single call
   this file used to make (undoing Phase 1's own writes before the real fill) was hoisted to the very
   top of `Layout`, before any segment's Phase 1 even ran — a no-op for a container's first segment,
   but for every later one it left Phase 1's virtual geometry stuck on the boxes the real fill then
   read. Symptom: every existing multi-column test with a forced page break failed with content frozen
   at the wrong Y. Fix: the rollback belongs *inside* `LayoutRunSegment`, right after its own Phase 1.

2. **`CssBox.FillFragmentainerWithBlockChildren` has no notion of "this run's own children" — only of
   `columnsBox.Boxes` as a whole.** Handing it a sliced-per-segment list controls nothing: with no resume
   token, it always starts at `Boxes[0]`, so a run *after* a span silently re-walked into the container's
   real first child (the span itself) and re-laid it out at the wrong (column, not container) width.
   Fixed two ways: (a) a `column-span: all` direct child now forces a break-before in
   `CssBox.LayoutBlockChildren` while a multi-column context is being filled — the same mechanism
   `break-before: column` already uses — so an ordinary run's own fill stops cleanly at the span instead
   of walking into it; (b) a **second**, symmetric break-*after* check, because the break-before check is
   guarded on `i > start` (so a span never rejects itself when it is legitimately the box a fill begins
   at) — without it, a leading span's own single-column fill walked straight past itself into whatever
   ordinary content followed, at the wrong width. Both raise a new `BlockBreakToken.IsColumnSpanHandoff`
   flag rather than being inferred from whichever box the token happens to name (that box is the span
   itself in the first case, ordinary content in the second) — `CssLayoutEngineColumns` reads the flag,
   not the box.

3. **A synthetic "start here" token still has to be a real one.** Once (2) made a run correctly *stop*
   at a span boundary, the *next* segment's own fill still needs to know where in `columnsBox.Boxes` to
   begin — a `null` resume always means `Boxes[0]`. `Layout`'s segment loop now threads a `startAt`
   token forward across iterations, separate from `resume`/`segmentResume` (which still gates Phase
   1/balance — "is this run's content already measured from an earlier page?", a different question):
   the span-boundary carry a segment ends on becomes the *next* segment's own traversal start, even
   though it is otherwise as fresh as the very first segment.

4. **`FragmentEmitter._nested` is keyed by `(columnsBox, slot)`, one list per key, and the existing
   single-key wipe (`ClearNestedFragmentainers`) assumed only one run ever occupied a slot.** With
   multiple runs sharing a slot, a later run's own balance retry would have erased an earlier run's
   already-finished columns. Added `ClearNestedFragmentainersFrom(contextRoot, slot, keepFirst)` —
   truncates rather than wipes — and a running `recordedSoFar` count in `Layout` so each run's retries
   discard only what they themselves recorded. Verified directly:
   `ColumnSpanAll_RetryInThePostSpanRun_DoesNotEraseThePreSpanRunsFragments` forces a post-span run
   retry and asserts (via `FragmentPaintHarness.FragmentOf`) that the pre-span run's own fragment
   survives it — not just that layout doesn't overlap, since this bug is invisible at the `CssBox`
   geometry level and only shows up in fragment-tree materialization.

## Deliberately not done

Only a **direct** child of the multi-column container is recognized as spanning — the same scope the
new break-before/break-after hooks are explicitly guarded to (`ReferenceEquals(this, ContextRoot)`,
`EstablishesMultiColumnContext`). A `column-span: all` on a deeper descendant has no effect. Tracked as
[#625](https://github.com/jhaygood86/PeachPDF/issues/625) /
`.claude/accepted-gaps/column-span-all-only-recognized-on-a-direct-child.md`.

## Evidence

- Full `net8.0` suite: 7809 passed, 0 failed (pre-existing, order-dependent flake in an unrelated
  container-query color test reproduced once and passed on every other run, isolated and on re-run).
- 118/118 `Multicol*` tests, including 10 new `ColumnSpanAll_*` ones covering basic geometry, per-run
  `column-fill: balance` independence, `column-rule` segment scoping, leading/trailing/consecutive
  spans, the `columnCount <= 1` degenerate no-op, the `_nested`-clearing regression above, and a
  multi-page resume that continues only the post-span run.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.

# #917's pagination cost is now linear, not superlinear

Building on [the `CapturedInstance` decoupling](2026-09-07-fragmentemitter-no-longer-type-checks-cssproxybox.md),
this lands the full fix for [#917](https://github.com/jhaygood86/PeachPDF/issues/917) (pagination cost growing
superlinearly with page count) — including the "ahead of slot" mechanism an earlier pass at this same session
built, found four bugs in, and reverted. It was not actually unfixable; the four bugs were four different
missing exclusions from one otherwise-sound idea, and a fifth, more fundamental one (a mark-semantics error)
needed a different mechanism shape entirely. All five are now understood and fixed.

## What shipped

**2a — repeating-group content can earn a pruning mark**, and **2b — `CommitGeometricallySettledObservations`**,
a geometrically-proven mid-pass commit for content already *behind* the slot just emitted. Both described in
the superseded write-up this file replaces; unchanged here.

**The child-skip cursor** (`CssBox.LiveChildStart`) — a `FragmentEmitter.ChildrenOf` container-level cache of
"how many leading children are already confirmed empty at-or-before this slot," letting the walk start past
them instead of re-checking each one on every slot. Unchanged from before.

**The actual asymptotic fix — a geometric "ahead of slot" skip in `ChildrenOf`'s ordinary-content loop.** The
cursor and 2b both only ever resolve content *behind* the current slot; neither can say anything useful about
content still *ahead* of it within the same wide `EmitPass` call, because the existing mark
(`RecordEmittedNothingAt`) means "empty from this slot on, **forever**" — sound for content already finished,
nonsensical for content that has not started yet (committing it would permanently hide the content's own,
later, real appearance). The fix does not extend that mark to cover "ahead" at all. Instead, `ChildrenOf`'s
fallthrough loop skips yielding a child outright — no `BuildDraft` call, no mark, no persisted claim — whenever
all of the following hold for it:

- `_currentPassIsFinal` (this `EmitPass` call's own `outgoing` token is null — `LayoutDocument`'s loop will not
  call `Root.PerformLayout` again for this generation). Without this, a still-open pass's own placement is
  provisional and can move again before the document's true final pass runs — the exact case
  `PageBreakTableKeepWithNextIntegrationTests.GapOneThenGapTwo_BothPrechecksFireForSameTable_ComposeWithoutDoubleCounting`
  pins (a table relocated twice by two different keep-with-next prechecks, each landing in its own pass).
- `!container.HasOutOfFlowBoxes` (document-wide: no floats, absolutely-positioned, or fixed content anywhere).
  An in-flow child can still contain a fixed or absolutely-positioned descendant whose own visual position
  does not follow from the child's — skipping the visit would skip ever reaching it. The same reasoning
  `Paint.FragmentPainter`'s own Bounds-based page-visibility pruning already excludes out-of-flow documents
  from.
- `snapshot is null`. A non-null snapshot means the child is being read through a captured instance's own
  geometry (a repeating header's source subtree, re-snapshotted once per page it repeats on; a multi-column
  column's own fill) — its *live* `Location`/`Rectangles` are simply the wrong thing to read.
- `!_capturedInstanceOwnerAncestors.Contains(childBox) && !_capturedInstanceOwners.Contains(childBox)` (new
  set, populated by walking every ancestor of a box the moment it is recorded as a captured-instance owner).
  A normal-flow box can still have one nested arbitrarily deep inside it (a `break-inside:avoid` card wrapping
  a repeating-header table, relocated flush onto the next slot's own top) — an abandoned layout attempt's
  captured instance for that nested owner can still describe an earlier slot the wrapper's own settled
  position no longer overlaps, and `BuildDraft`'s own recursion is what would otherwise discover it.
- `childBox is not CssSpacingBox && !HoldsARowspanContinuation(childBox)` (new helper: does `box.Boxes`
  directly hold a `CssSpacingBox`). A table row that closes a rowspan can legitimately start on a later page
  while the spanning cell's own content — reachable only through that row's `CssSpacingBox` child, which
  `ChildrenOf`'s own top redirects to the cell itself — still belongs to an earlier one. Both halves are
  needed: excluding the row (so its spacing-box child is still reached at all) and excluding the spacing box
  itself (so it is not skipped in turn once yielded as an ordinary child of that row).
- `!childBox.NeverTouchedThisLayout` (defensive, mirroring 2b's own caution — `OwnGeometryTop()` falls back to
  `Location.Y`, which a never-laid-out box has not had written at all this generation).
- `childBox.OwnGeometryTop() >= container.PageTopOf(slot.Index + 1)` — the child's own settled top already
  sits at or past the *next* slot's own top, so it holds nothing in this one.

## Why a per-visit filter, not a persisted mark

The first version of this (this same session, reverted once, rebuilt twice more) tried committing "ahead"
through the existing mark machinery (`RecordEmittedNothingAt`), gated the same way. It caused 412 failures:
`EmittedNothingAtOrBefore`'s own `slotIndex >= _emittedNothingAtSlot` check means a mark committed for "empty
at slot 0" is trusted at *every* slot from 0 onward, forever — exactly wrong for a row genuinely empty at
slot 0 but holding its own real content at slot 1. A per-visit filter that persists nothing has no such
window: a child skipped for one slot is simply re-examined, from scratch, the next time `ChildrenOf` is asked
about it.

## The bugs found building this, in the order they surfaced

1. **A "skip if `NeverTouchedThisLayout`" filter** (checked first, before the geometric one — reasoned to be
   trivially safe since an untouched box holds no positioned content anywhere) broke on out-of-flow content:
   Acid2's fixed bars are reachable through a normal-flow ancestor that looks untouched-and-empty from the
   ancestor's own ordinary ancestry, even though a fixed descendant inside it must appear on every page
   regardless. Reverted outright rather than patched — superseded by the geometric check plus
   `HasOutOfFlowBoxes`, which excludes the whole risk class.
2. **The mark-based "ahead" commit** (described above) — reverted, replaced with the per-visit filter.
3. **`OwnGeometryTop()` on a container aggregated incrementally across passes** (`<tbody>`/table-row-group:
   `CssLayoutEngineTable.SetRowGroupBoxDimensions` recomputes `Location`/`ActualBottom` from only the rows
   placed *so far*, each time it is called) produced a `BoundsEndAtItsContent` divergence on
   `FragmentPruningParityTests.ARepeatingTableHeaderAcrossPages_PrunesWithoutChangingTheTree`. Not actually
   about the row-group's own geometry being wrong at `_currentPassIsFinal` (by then every row has been
   placed) — the real cause was bug 5 below, a `<tr>` read through the header's own captured snapshot.
4. **A relocated `break-inside:avoid` card wrapping a repeating-header table**
   (`RepeatingTableRelayoutTests.ACardHoldingARepeatingHeaderTable_IsRelocatedWithoutCarryingAGap`) — the
   card itself settles flush against the next slot's own top, correctly "ahead" by its own geometry, but an
   abandoned layout attempt left the *table nested inside it* holding a captured instance at the earlier
   slot the card's own final position no longer overlaps. Fixed with `_capturedInstanceOwnerAncestors`.
5. **A child read through a captured instance's own snapshot still consulted its live geometry** — the
   header's own `<tr>`, reached via `capture.DetachedSourceRoot` and then iterated as ordinary content of
   that source root, read `OwnGeometryTop()` off the live box (wherever it last happened to sit) rather than
   the snapshot the draft was actually being built from. Fixed with `snapshot is null`.
6. **A table row closing a rowspan, and the `CssSpacingBox` marking that closure** — the identical shape to
   the original (already-reverted, whole-loop-stopping) early-break's own Bug C, but this time surfacing as
   two separate omissions (the row itself, and the spacing box once yielded as the row's own child) rather
   than one. Fixed with `HoldsARowspanContinuation` and `childBox is not CssSpacingBox`.

Every one of these was caught by the pruning-parity oracle (`PEACHPDF_VERIFY_FRAGMENT_PRUNING=1`), not by
code review — each fix was verified against the full suite before moving to the next.

## Measured effect

Isolated reproducer (one table, repeating `<thead>`, no forced breaks — `HtmlContainerInt.BuildDraftCalls`,
single `EmitPass` covering the whole document in every case):

| rows | pages | before any of Phase 2 | 2a+2b+cursor only | full fix (this change) |
|-----:|------:|----------------------:|-------------------:|------------------------:|
| 60   | 8     | 4,572                  | 1,016               | 428                      |
| 120  | 15    | 18,162                 | 3,366               | 846                      |
| 240  | 30    | 70,070                 | 12,156              | 1,716                    |
| 480  | 60    | (not measured)         | 45,936              | 3,456                    |
| 960  | 120   | (not measured)         | (not measured)      | 6,936                    |

The full fix's own growth ratio across five doublings: 1.98, 2.03, 2.01, 2.01 — clean, consistent O(n), not
the ~4x-per-doubling of the unfixed baseline or the still-superlinear ~3.3–3.8x of 2a/2b/cursor alone. At 240
rows this is a ~41x reduction from the original baseline.

## Evidence

Full suite with the pruning-parity oracle enabled (`PEACHPDF_VERIFY_FRAGMENT_PRUNING=1`): 10,174 passed, 0
failed, 9 skipped — including every test any of the six bugs above broke along the way, and the original
early-break attempt's own casualties (`TableRowspanContinuationTests`, `TableSpannedBandRepetitionTests`,
`RepeatedTableHeaderClipIntegrationTests`, `TableHeaderRepetitionIntegrationTests`, `CssProxyBoxTests`,
`TableOncePerTableTests`, `FragmentEmitterTests.RepeatingTableHeader_ProducesHeaderFragmentsOnEveryPageItRepeatsOn`,
`FragmentPruningParityTests.ARepeatingTableHeaderAcrossPages_PrunesWithoutChangingTheTree`, all now passing).
Solution-wide `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors. Diff coverage against `main`
(`diff-cover` over the `PeachPDF.Tests` cobertura output): 100% (239/239 changed lines).

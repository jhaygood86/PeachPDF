# Pass-rewind lookup keyed on the wrong slot (#384), and a dead keep-with-next retry removed (#352)

## #384 — `RequestPassRewind` couldn't find a pass that stepped over a forced break

`HtmlContainerInt._passEntries` records, per fragmentainer pass, only the slot that pass *opened*
at (`RecordPassEntry`). But a forced break can step a still-open pass's cursor forward without
ending it (`FragmentainerContext.StepOverTo` — see
`.claude/invariants/fragmentation-the-fragmentainer-a-pass-is-filling-is-not-the-one-it-opened.md`),
so a box placed after such a step sits in a slot different from the one its own pass's `_passEntries`
entry names. `RequestPassRewind`/`TryRewindForRunPull` identified "the pass that placed a box" via
`PassEntryFilling(SlotStartingAt(head.Location.Y))` — a lookup keyed on the box's *current slot*, which
either finds nothing (declines a legitimate rewind) or, worse, matches a *different* pass that happens
to have opened at the same slot number, rolling the driver back to the wrong point.

**Fix:** `CssBox` now stamps itself, in `CommitBlockChildOffset` right after `Location` is set, with
which pass placed it (`_placedByPass = container.CurrentPassIndex`) — a fact independent of which slot
the pass eventually reached. `PassEntryFor` prefers this stamp over the slot-keyed lookup, falling back
to the old lookup only when the stamp is absent or stale.

**Staleness is the real work**, and needed two guards, not one — found only because a review agent
caught what the first implementation missed:

1. **Per-invocation generation.** `_placedByPass` is meaningless once `HtmlContainerInt.LayoutDocument`
   runs again (`ShrinkToFit`, the per-page reflow loop) and rebuilds `_passEntries` from nothing — an
   index into the old list says nothing about the new one. Mirrors the existing
   `_emittedNothingGeneration`/`HtmlContainer.LayoutGeneration` pattern a few hundred lines away in the
   same file: `_placedByPassGeneration` is stamped alongside `_placedByPass` and checked first.
2. **Per-index invalidation, not a bare counter.** `TruncatePassEntries` can shorten `_passEntries` and
   let it grow again, so a stale index can land back in-range and silently name a different pass. The
   first attempt at this used one incrementing `int` (`_passTruncationCount`) — sound but unscoped: an
   unrelated truncation anywhere in the document retired *every* box's stamp, degrading every rewind but
   the one just discovered back to the pre-fix lookup. Replaced with
   `Fragmentation.InvalidationHistory` (already built for exactly this shape, for a different field —
   `_emittedNothingRecordedAt`/`RecordEmittedNothingAt`/`EmittedNothingAtOrBefore`), scoped by
   `_passEntries` index: a truncation at index 5 no longer retires a stamp naming index 2.

`InvalidationHistory` gained a `Clear()` method (append-only until now, since its only prior owner,
`FragmentEmitter`, is recreated fresh every `LayoutDocument` call rather than reset in place) so
`HtmlContainerInt`'s own instance can be reset in `LayoutDocument`'s existing per-invocation block
alongside `_passEntries.Clear()` — not load-bearing for correctness once the generation check exists,
but keeps the list from growing unbounded across a document's `ShrinkToFit`/reflow iterations, per
`.claude/invariants/fragmentation-a-latch-that-bounds-a-decision-must-be-reset-per-layout-not-per.md`
(#320).

**Regression test:** no fixture reaching the *observably wrong page placement* was found despite an
extensive combinatorial search (hundreds of `(leadWords, cardWords, pageHeight, forced-break position)`
combinations) — in every case the pre-fix decline or wrong-entry match happened to coincide with the
correct final position, because an earlier in-place translation (`PlaceBlockChild`'s own §5.2 pull) had
already relocated the run correctly before this mechanism was ever consulted. What *does* differ,
confirmed via temporary instrumentation before it was removed: `container.PassRewinds` itself (1 vs 0)
for the same document, pre-fix vs post-fix — the same signal the file's existing sibling test
(`PulledRun_FromAPassThatResumedIntoAParagraph_ReEntersThatPass`) already relies on rather than page
placement. Added `PulledRun_FromAPassThatSteppedOverAForcedBreak_ReEntersThatPass` (asserts
`PassRewinds > 0` across a small swept family, for font-metric robustness, matching the sibling test's
own convention) and a `_KeepsEachHeadingWithItsBlock` companion, using a new
`ResumedParagraphWithLeadingForcedBreakDocument` fixture — the existing `ResumedParagraphDocument` with
a `break-after:page` marker div prepended, so the pass being re-entered has itself stepped over a
forced break before it ever places the run's head.

## #352 — the keep-with-next first-line retry was deleted, not converted

The issue asked to express `CssBox.PerformLayoutEpilogue`'s bespoke `_keepWithNextRetried` block (move
run via `OffsetTop`, clear `_prologueDone`, re-enter `PerformLayoutImp`) as a proper `EarlyBreak`
decision routed through the parent's `TryRestartAt`, matching every other §3.1/§4.3 mover in this file.

The block's own comment (introduced by the commit that landed #539, and deleted along with the block)
already recorded that this exact conversion had been *investigated twice* (#538, then #539) and found
the mechanism **provably dead**: across the whole test suite, `firstLinePage > ownPage` is true 244
times and `keepWithNextRun.Count > 0` on none of them, because every case that could reach it — a run
whose next member's first line lands on the next page — is already intercepted earlier, by the very
break-decision movers #332 built (`EarlyBreak.Discover` + `TryRestartAt` on the §3.1-propagation and
orphans arms in `LayoutBlockChildren`, and `PlaceBlockChild`'s own §5.2 pull), all of which run on the
pass that *declines* to place the box — before this epilogue is ever reached. What's left when none of
them applied is, definitionally, the case with no run to pull.

That comment also cited "`#545` is where that [convert-or-delete] decision is made" — traced via
`git log -S` to the introducing commit and confirmed **wrong**: `#545` tracks an unrelated escaping-
forced-break defect in multicol, discussed earlier in the same commit message; the keep-with-next
paragraph that follows it in that message cites no issue at all. Whoever wrote the in-code comment
misattached the neighboring paragraph's issue number.

Converting the dead path per the issue's literal text would have required new cross-frame signaling (the
decision is discovered inside the child's own epilogue, but the run to move lives in the *parent's*
`Boxes`) that, per the same historical evidence, could not itself be exercised by any known fixture —
new code that cannot be covered, added for a path proven unreachable. Given the choice between converting
anyway (accepting a permanent coverage exception), deleting outright, or deferring, the deletion was
chosen: every case the mechanism could handle is already handled upstream, so removing it is not a
behavior change, only a correctness-neutral simplification. Confirmed via the full suite (unchanged
pass count) and explicitly via `KeepWithNextIntegrationTests.ParagraphFirstLinePushedByWordFlow_
PullsAvoidChainedHeadingAlong` (#352's own named contract test), both passing identically before and
after the deletion.

`DomUtils.GetPrecedingKeepWithNextRun` is unaffected — it has other live callers (`PlaceBlockChild`'s
§5.2 pull, `EarlyBreak.cs`, `ColumnBreakFallsBefore`).

## Evidence

- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`: 10393 passed, 0 failed (was
  9768/9768 before this branch's work began — the delta is this branch's own new tests).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- Diff coverage against `main`: 100% (`CssBox.cs`, `HtmlContainerInt.cs`, `Fragmentation/InvalidationHistory.cs`).
- Two independent review passes (before and after the generation/scoping fix), the first of which is
  exactly what caught the `_passTruncationCount` invariant violation and unscoped-invalidation issue
  described above.

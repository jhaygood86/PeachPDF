# Writable marks during `Finish()`'s stale-slot replay: investigated, found safe but useless

Follow-up to #572/#587's own "not pursued further" note, and closes #583 as a separate, unrelated
cleanup found during the same audit.

## #583: `CssBox.OffsetRectangle` deleted

`CssLineBox.SetBaseLine` set its own `Rectangles[b] = newr` (the gap-adjusted rectangle) *before*
calling `b.OffsetRectangle(this, gap)` — and `AssignRectanglesToBoxes` (the only thing that ever
populates `CssBox.Rectangles`) runs later still, copying that already-adjusted value straight in. So
`OffsetRectangle` was provably dead (its lookup always missed) and had no valid ordering under which it
would be both reachable and correct: called before `AssignRectanglesToBoxes`, today's no-op; called
after, a silent double-application of the gap. Deleted the method and its one call site rather than
adding the `NotifyGeometryChanged` call the issue itself suggested — patching would have preserved a
method that produces wrong geometry if it ever became reachable, just with correct notifications about
the wrong geometry.

## The stale-slot-replay follow-up: safe, and worth recording exactly why it doesn't help

#587 split `FragmentEmitter._pruningSuspended`'s two duties (blocking new pruning marks vs. blocking
*reads* of existing ones) so `Finish()`'s stale-slot replay and `EmitReservedBlankSlots` could at least
read pre-existing marks. Writing new marks during those passes stayed blocked, and #587's own note
flagged this as the likely next win: on the real `dictionary.mhtml` document, ~68 million of the
~79.3 million remaining `BuildDraft` calls were attributed to exactly this restriction.

**The hypothesis tested**: replaying `_stale` in descending slot order (instead of `InvalidateFrom`'s
own ascending accumulation) removes the specific danger the original ascending-order code was written
against — `CssBox.EmittedNothingAtOrBefore` only ever lets a mark recorded while processing slot `S`
suppress a *later* query at a slot `>= S`, so in descending order every stale slot above `S` has
already been fully walked by the time `S` is processed, and nothing left to visit can reach back and
be wrongly suppressed by a mark made at `S`.

This reasoning is correct — implemented it (`EmitSlot`'s `mayWrite`/`mayVerify` split, `Finish()`
visiting `_stale.Reverse()`), and it passed the full suite and `PEACHPDF_VERIFY_FRAGMENT_PRUNING=1`
cleanly. **But instrumented per-slot on the real document, it produced zero measurable improvement**
(same 79,285,091 `BuildDraft` calls as before, wall-clock unchanged within noise). The per-slot trace
explains why precisely:

```
DIAG   replay slot=761 calls=1651      <- first (highest) slot: cheap, benefits from PRE-EXISTING marks
DIAG   replay slot=760 calls=1663
...
DIAG   replay slot=5   calls=255228    <- cost climbs back to a full unpruned walk
DIAG   replay slot=4   calls=255213
DIAG   replay slot=3   calls=255420
DIAG   replay slot=2   calls=255191    <- last (lowest) slot: essentially the whole tree, every time
```

The directional check (`slotIndex >= _emittedNothingAtSlot`) means a mark only ever helps a query at an
*equal or higher* slot than where it was recorded. In a descending sweep, every mark gets written at a
progressively *lower* index than the last — but there is nothing left to visit below it in the same
pass by construction. The reordering that makes writing safe is exactly the reordering that makes the
writes useless: safety and payoff pull in opposite directions here. Ascending order is the only order
in which an early (low) mark could help a later (high) query in the same sweep — which is exactly the
order the original code was unsafe to write in, for a real reason (a box could be wrongly marked
"empty from here on" while its actual content is still ahead, undiscovered, in the same batch).

## What a genuinely effective fix would need

Not pursued here, and flagged for whoever picks this up next: making *ascending* replay safe to write
in requires a stronger completeness argument than "haven't reached higher ground yet in this pass" —
something closer to confirming, before writing any mark, that a box's `_frozen` membership reflects its
*current*, post-invalidation state rather than a historical fragment from before the reopening that
caused it to become stale in the first place. Whether `_frozen` can go stale in exactly that way across
an `InvalidateFrom` call is the open question worth answering first, before attempting another design —
answering it blind, the way this attempt did, costs a full implementation-and-measurement cycle to find
out it doesn't pay off.

## What was deliberately not done

- `EmitReservedBlankSlots` was not given the same treatment: it has a different, boundary-based safety
  argument (categorically past everywhere real content was ever bounded to reach) that isn't backed by
  an invariant anywhere today, and it only ever covers 0-1 slots per document, so the same verification
  burden isn't worth it regardless of the ordering question above.
- The `EmitSlot` parameter split (`mayWrite`/`mayVerify`, decoupled from the single overloaded
  `mayPrune`) was kept even though `Finish()` ends up passing `mayWrite: false` (unchanged from before
  #587) — it disentangles two concerns that were conflated before this investigation started and reads
  more clearly regardless of this attempt's outcome.

## Evidence

- Full `net8.0` suite: 7384 passed, 0 failed, 9 skipped (confirmed across multiple runs; one run hit an
  unrelated, pre-existing timing-coincidence flake in `FontSynthesisIntegrationTests` — see the
  separately-filed follow-up — and one run hit a catastrophic native test-process crash that did not
  reproduce on immediate retry, consistent with this machine's known BIOS/hardware instability rather
  than a code defect).
- Same, with `PEACHPDF_VERIFY_FRAGMENT_PRUNING=1`: 7384 passed, 0 failed, 9 skipped.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- `diff-cover` against `origin/main`: 100% (7 of 7 coverable lines).
- Per-slot `BuildDraft` call counts (above), measured against the real `dictionary.mhtml` with a
  temporary counter and harness (removed before landing, same pattern as #587's own measurement).

Issue #572 can close on this: #583 is resolved, and the remaining performance idea from #587's own note
has been tried, measured, and found not to pay off as designed — with the reason recorded here so it
isn't re-attempted blind.

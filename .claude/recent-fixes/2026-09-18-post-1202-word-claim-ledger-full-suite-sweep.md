# Post-#1202 full-suite sweep of the word-claim ledger: two live findings, two resolved

Follow-up to #1200/#1202 (`.claude/recent-fixes/2026-09-18-issue-1047-fragment-double-claim-fixed.md`).
#1200 diagnosed #1047's mechanism with a DEBUG-only per-slot word-claim ledger
(`HtmlContainerInt.VerifyWordClaims`) and, while diagnosing it, found the same `FragmentEmitter.ClaimsLine`
`FallsPast` tie-break already disagreeing with three other, unrelated fixtures - table rowspan
continuations, a multi-column child kept by the no-progress backstop, and a flex/grid wrapping column -
each flagged as a real but out-of-scope finding. #1202 then fixed #1047's own mechanism (gating the
tie-break with `_currentPassFromSlot`), explicitly leaving those three for a follow-up. This entry is that
follow-up's diagnosis pass.

## What was done

`VerifyWordClaims` gained an env-var default (`PEACHPDF_VERIFY_WORD_CLAIMS=1`), mirroring
`FragmentEmitter.VerifyPruningAgainstFullWalk`'s own idiom, so the ledger could run over the whole
`PeachPDF.Tests` suite (12,507 tests, net8.0) in one pass rather than only the handful of fixtures that
already set it per-instance. That is the only production-code change this entry accounts for - see
`HtmlContainerInt.VerifyWordClaims`'s own updated remarks.

## Result: two of the three original findings no longer reproduce

Every `NoProgressBackstopTests`, `MulticolLayoutIntegrationTests`, `FlexGridFragmentationIntegrationTests`,
`GridLayoutIntegrationTests`, and `ContainerQueryLayoutIntegrationTests` fixture in the full-suite run
passed clean with the ledger on. Whatever #1200 found disagreeing in the multi-column no-progress-backstop
and flex/grid wrapping-column shapes either was resolved by #1202's `_currentPassFromSlot` gate as a side
effect, or was never reliably reproducible through an existing fixture - not confirmed further, since a
full-suite run with the ledger forced on is exactly the thorough check #1200 itself called for, and it
found nothing there.

## Result: table rowspan continuation and one new shape still reproduce - same root cause

Four tests still threw the ledger's double-claim exception:

- `TableRowspanContinuationTests.ASpanningCellWhoseContentFinishesInTheSameBandTheSpanEndsIn_ClosesAtTheRowsOwnBottom`
- `TableRowspanContinuationTests.ASpanningCellWhoseContentFinishesSeveralBandsBeforeTheSpanEnds_ClosesAtItsOwnBand`
- `TableRowspanContinuationTests.ASpanningCellInTheLastColumn_StillClosesAfterMultipleResumptionPasses`
- `EarlyBreakLayoutIntegrationTests.BoxTallerThanTheBand_MovesAtMostOnce` (not one of #1200's original three -
  a new shape found by this sweep, previously believed a merely-imprecise-but-safe residual - see below)

All four trace to the identical gap, confirmed by reading the code rather than by further tracing:
`FragmentEmitter.ClaimsLine`'s `FallsPast` tie-break is only gated by `_currentPassFromSlot` while an
`EmitPass` call is actually on the stack (`_currentPassFromSlot` is set at `EmitPass`'s own top and reset
to `null` in its `finally`). `FragmentEmitter.Finish()`'s own stale-slot replay loop never calls `EmitPass`
- it calls `EmitSlot` directly, once per slot in `_stale`, each call a fully independent single-slot
invocation:

```csharp
foreach (var slot in new List<int>(_stale))
{
    EmitSlot(slot, mayWrite: false, site: "Finish replay");
}
```

So for the whole of that loop (and, in principle, `CatchUpStaleSlotsBehind`/`EmitReservedBlankSlots`, the
other two single-slot-emission callers), the tie-break is unconditional - exactly as it was for everything
before #1202. If an earlier `EmitPass` (or an earlier iteration of this same loop) already froze a line's
words at its own, correct nominal slot, and a *later* slot is separately re-emitted here (because
`InvalidateFrom` marked it stale for an unrelated reason) and the same line still falls past its own band
there, the tie-break grants that later slot a second, conflicting claim - without ever un-freezing the
first slot's claim, since the first slot was never itself added to `_stale`.

The `EarlyBreakLayoutIntegrationTests` case is worth flagging specifically: the #1202 recent-fix note
recorded a box taller than one whole band (so `CanBeLaidOutAgain` correctly refuses re-layout and falls
back to `TranslateForEarlyBreak`'s blind shift) as a *known, accepted* residual - "every word still lands
on exactly one page - this residual is not #1047's own double-claim shape." That conclusion does not hold
in general: `TranslateForEarlyBreak`'s own `InvalidateFrom` call can mark a slot stale that `Finish()`'s
replay then re-claims through the same gap, producing a genuine double-claim, not just an imprecisely
positioned but singly-claimed box. The note's own conclusion should be treated as superseded by this
finding.

## Filed, not fixed

Filed as two issues rather than one, since they are materially different CSS shapes even though the root
gap is identical - a fix might reasonably close one before the other, and a single combined issue would
make that harder to track:

- [#1210](https://github.com/jhaygood86/PeachPDF/issues/1210) - table rowspan continuation
- [#1211](https://github.com/jhaygood86/PeachPDF/issues/1211) - `break-inside:avoid` box taller than one band

Deliberately not fixed in this pass, for the same reason #1200 deferred #1047's own fix to a second PR:
the gate needs to track "already correctly claimed, do not re-grant" across the *whole* word-claim
lifetime of a `Finish()` cycle - every `EmitPass` call plus the stale-slot replay loop - not just one
`EmitPass`'s own `fromSlot`. A slot-index comparison like `_currentPassFromSlot` cannot express that once
two *separate* single-slot emissions are both in play; a correct fix likely needs either a
whole-cycle-scoped "lowest slot nothing later can still legitimately answer for" cursor, or a real
"already frozen in `_emitted` and not stale" lookup keyed by the line itself. Both issues note this
explicitly and ask for a diagnose-then-fix split, mirroring #1200/#1202, rather than a fix attempted
without first confirming the mechanism as rigorously as #1200 did for #1047 - the prior recent-fix note's
own warning about a "reads like a small fix, has a huge blast radius" mistake in this exact area applies
here too.

## Evidence

Full net8.0 suite, `PEACHPDF_VERIFY_WORD_CLAIMS=1`: 12,494 passed, 4 failed (the four listed above), 9
skipped (pre-existing platform skips), 12,507 total, ~22s. Without the env var set (default), the same
suite is unaffected - `VerifyWordClaims` defaults to `false` exactly as before when the variable is unset,
so this sweep's own production change (the env-var default) alters no behavior any existing test observes.

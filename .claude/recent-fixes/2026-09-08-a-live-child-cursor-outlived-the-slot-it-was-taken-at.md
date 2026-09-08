# A live-child cursor outlived the slot it was taken at

`ChildrenOf` starts a container's child walk past a prefix of children each already confirmed
`EmittedNothingAtOrBefore`, caching how many in `CssBox._liveChildStartIndex`. The cache carried the
layout generation but not the **slot** the answer was taken at, and "nothing at or after slot 6" is
not "nothing at or after slot 0". `CatchUpStaleSlotsBehind` re-emits a frozen slot behind the
frontier, read the cursor there, and skipped children whose content belonged to exactly that slot.

`LiveChildStartFor(slot)` now returns the cached index only where `slot` is at or after the slot the
index was derived at, and `RecordLiveChildStart` stores it.

## What measuring it turned up

- **The counts name the defect on their own.** On the document that found this, slot 0 was emitted
  three times: 169 words, 169 words, then 51 after a relocation at slot 6 reopened it. The prose
  container's cursor read 4 out of 146 children at that third emission — its first four children,
  holding 660 characters, were never visited and appeared on no page.
- **It leaves a hole, not a shuffle.** Those four children were laid out and occupy space: page 1
  had 420pt of blank above its first surviving line. Content loss with a visible gap where the
  content was, which is what distinguishes this from a pagination difference.
- **The mark itself was fine.** `BuildDraft`'s own `EmittedNothingAtOrBefore` check never fired at
  slot 0 — instrumented and confirmed, no hit at all. Only the cursor derived from it was wrong,
  which is why the defect survived: the guarded, slot-aware check and the unguarded cache derived
  from it disagree only on a path that runs backwards.
- **Forcing the unpruned walk on the catch-up path also fixes it**, which is how the cursor was
  identified — but that is the expensive full walk `CatchUpStaleSlotsBehind` exists to avoid
  (`Finish`'s own replay is ~145,000 `BuildDraft` calls per stale slot on a large document), so it
  is a diagnosis rather than a fix.
- **The parity oracle could not have caught it.** `PEACHPDF_VERIFY_FRAGMENT_PRUNING` compares the
  pruned and unpruned walks only where `EmitSlot` is given `mayVerify: true`, and the stale-slot
  catch-up is not one of those calls. Noted as an invariant rather than changed here — widening the
  oracle is its own change with its own risk.

## Evidence

`StaleSlotChildCursorTests`. Full suite green on net8.0, 0 new build warnings.

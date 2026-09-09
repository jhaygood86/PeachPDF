# A pruning answer is about one slot and every later one

`EmittedNothingAtOrBefore(S)` says "this subtree's fragments are all behind slot S". That is a claim
about **S and every slot after it** — never about a slot before it, where the same subtree may hold
real content. Every cache derived from that answer has to carry the slot it was taken at, or it
silently answers the wrong question.

`_liveChildStartIndex` did not. It records how many of a box's leading children were each confirmed
`EmittedNothingAtOrBefore`, so a later slot's walk can start past them — and it stored the
generation but not the slot. Emitting an *earlier* slot again is not hypothetical:
`CatchUpStaleSlotsBehind` exists to do exactly that whenever a late relocation reopens a frozen
fragmentainer.

**The measured symptom.** A fifteen-page document whose page 1 was emitted three times — 169 words,
169 words, then **51**. The prose container's cursor stood at 4 when slot 0 was rebuilt, so its
first four children were skipped and their text appeared on no page at all: 660 characters gone,
and a 420pt hole at the top of page 1 where they had been laid out.

Two things follow for the next such cache:

- **Store the slot next to the index.** Trust the cache only where the slot now being built is at or
  after the slot the cache was derived at. Dropping to a lower slot must re-derive.
- **The parity oracle does not cover this.** `VerifyAgainstTheFullWalk` runs only where `EmitSlot` is
  called with `mayVerify: true`, and the catch-up re-emission is not — which is precisely the one
  path that reads a cache at a lower slot than it was written at.

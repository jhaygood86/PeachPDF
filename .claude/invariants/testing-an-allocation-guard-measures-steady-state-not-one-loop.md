# An allocation guard measures steady state, not one loop

_A trap this repo has paid for at least once._

A test asserting that some work "allocates nothing per call" must go through
`TestSupport.AllocationProbe.Bytes`, never a bare `GC.GetAllocatedBytesForCurrentThread()` delta around
a single loop. Six sites across five files had the hand-rolled shape — warm three calls, measure one
batch of 50–10 000, assert under 4 096 bytes — and every one of them was a copy of the same defect.

## The measured symptom

`GsubLigatureMatchAllocationTests.WalkingARunForLigatures_AllocatesNothingPerPosition` failed on
`test (ubuntu-latest)` on a branch that touched no font or shaping code at all, reporting **6 616
bytes** against its 4 096 bound. The `windows-latest` and `macos-latest` legs of the same
`fail-fast: false` matrix passed, and the full suite passed locally in Debug, in Release, and in
Release with coverage collection — the exact CI invocation. A re-run went green on all three.

The number is what identifies it. 6 616 bytes over 10 000 match attempts is **0.66 bytes per attempt**;
the regression the test exists to catch — an empty list plus a boxed enumerator per glyph position —
costs 720 000 bytes, measured by reintroducing it. So the failure was a fixed lump landing inside the
counted window, not per-position allocation returning.

## Why one loop cannot measure this

.NET promotes a method out of tier-0 after 30 calls, and only after a background call-counting delay
that **restarts while other methods are still being called for the first time** — which, in a suite
running test collections in parallel, is continuously. A three-call warm-up therefore leaves the method
at tier-0 and puts promotion somewhere inside the loop being counted. Where it lands depends on the
machine, which is why a CI runner sees it and a dev box does not.

## What the probe does instead

Warm past the promotion threshold (64 calls), then take the **smallest** of several measured batches.
The minimum is the right estimator because of what each kind of allocation does to it: a genuine
per-iteration allocation appears in *every* batch and survives the minimum untouched, while a one-off
transient lands in one batch and is discarded. It cannot hide a regression; it can only discard a
coincidence.

Proven rather than argued: with the `foreach`-over-`IReadOnlyList` boxing and the eager `skippedOffsets`
list put back into `TryMatchLigature`, the hardened test still fails at **720 000 bytes against 4 096**,
176× over. Raising the bound instead would have destroyed exactly that sensitivity — the same trade
[a complexity guard asserting the clock](testing-a-complexity-guard-asserts-a-count-not-the-clock.md)
gets wrong in the other direction.

## Two rules that come with it

- **Per thread, not process-wide.** `GC.GetTotalAllocatedBytes` counts every parallel collection's work
  too. The probe reads the per-thread counter, so **every body passed to it must be synchronous and
  never await** — a continuation can resume on a different thread from the one the counter was read on.
- **Build the delegate before measuring.** Constructing it allocates; invoking an already-constructed
  one does not. The probe's own warm-up loop calls through the same delegate for this reason.

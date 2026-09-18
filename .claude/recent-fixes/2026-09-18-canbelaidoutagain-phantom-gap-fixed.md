# `CanBeLaidOutAgain`'s phantom-gap overflow (issue #1213), left open by #1047/#1202, now fixed

`.claude/recent-fixes/2026-09-18-issue-1047-fragment-double-claim-fixed.md` diagnosed and deliberately
left open a second defect while fixing #1047's own double-claim bug: `CssBox.CanBeLaidOutAgain`'s
destination-fit check (`FitsInFragmentainer`, reading `ActualBottom - Location.Y`) can be inflated by a
"phantom gap" whenever `CssBox.TryRestartAt`'s same-pass keep-with-next restart has already relocated a
box's own first in-flow child forward to a later page's top without moving the box's own `Location.Y` to
match. This was investigated as a standalone follow-up (issue #1213 filed for it) rather than left as
prose, since it is a genuine, reproducible visual defect — not merely an implementation-precision nuance.

## Confirmed still reproducible on `main` after #1202

`TryRestartAt` now calls `StepOverTo` (the #1047 fix), but that only fixes which band the restarted head
measures against — it does nothing about the enclosing box's own stale `Location.Y`. Instrumented
`EarlyBreakLayoutIntegrationTests.Issue1047Document` directly (temporary `File.AppendAllText`, not kept)
across the fixture's own confirmed reproduction band ([84, 96] and [244, 256]): the enclosing `card` box
overflowed its destination page's own content area by up to **16pt — a full line-height** in the 200pt
page / 20pt margin fixture. Every filler height in both bands overflowed by a non-trivial amount (1–16pt);
nothing outside the band did, confirming this is exactly the mechanism the #1047 fix note predicted.

## The fix: `EffectiveContentTop`, a narrowly-scoped correction

Added `CssBox._firstChildRestartedTop` (nullable `double`), set in exactly one place —
`TryRestartAt`, immediately before `Boxes[resumeFrom].ResumeAt(null, restart.Top)` — and only when the
box being restarted (`Boxes[resumeFrom]`) is *this* box's own first in-flow child
(`BreakPropagation.FirstInFlowChild(this)`, `ReferenceEquals`-checked). `FitsInFragmentainer` now reads a
new `EffectiveContentTop` property (`_firstChildRestartedTop ?? Location.Y`) instead of `Location.Y`
directly. Reset alongside `_earlyBreakTaken`/`_earlyBreakRetryTop` in `BeginLayoutPass`, so it never
survives past the pass that set it.

**Why "first in-flow child specifically", not "any child that landed on a later page than `Location.Y`"**:
a broader "child is on a page after `Location.Y`'s page" rule was considered and rejected. It would also
fire for a case with nothing phantom about it at all — a box whose own huge `padding-top` legitimately
pushes its first child onto a later page — and discounting that padding from the fit check would be
straightforwardly wrong (re-laying the box out fresh at the destination reproduces the same padding, so
the "discount" doesn't reflect any real footprint reduction). Gating on "this restart specifically moved
*this box's own* first in-flow child" ties the correction to the exact mechanism that manufactures the
gap (`TryRestartAt`'s `ResumeAt` without a matching move of the caller's own `Location`), so it cannot
fire for ordinary padding/border geometry, which never touches `TryRestartAt` at all. A later in-flow
sibling being restarted (not the first) is also deliberately excluded: something before it is still
real content sitting where this box's own top says content begins, so `Location.Y` is still correct
there and no correction should apply.

A "would a false positive degrade gracefully" check was done for the rejected broader rule too: an
incorrect "yes it fits" from over-crediting real padding would not corrupt output (a subsequent full
re-layout at the wrong-but-attempted destination would simply re-discover the box still doesn't fit and
raise another decision, converging the same way `CanBeLaidOutAgain`'s own doc comment already describes
for a runaway monolithic box — "verified to walk a box down 100,000 pages"). The narrower fix was still
preferred because it has *zero* plausible false-positive surface, not merely a survivable one.

## Evidence

- Re-ran the same instrumentation after the fix across the identical confirmed band: every point now
  reports the box fitting its destination page with room to spare (e.g. `overflow=-60` for every filler
  height in both bands, vs. up to `+16` before).
- Replaced the throwaway instrumentation with a permanent assertion,
  `MultiLineHeadingRelocatedByBreakInsideAvoid_FitsWithinDestinationPage`
  (`EarlyBreakLayoutIntegrationTests.cs`), swept across the same six confirmed-band filler heights the
  sibling word-claim tests already use.
- Full `net8.0` suite: 12,504 passed, 0 failed, 9 pre-existing platform skips (unchanged from before this
  change) — including `EarlyBreakLayoutIntegrationTests`, `BandMembershipToleranceTests`, and
  `MonolithicContentLayoutIntegrationTests` specifically, per this repo's own warning that this
  measurement is shared by every `break-inside:avoid`/monolithic/orphans-widows mover.
- Diff coverage: 100% on the 5 changed/added production lines (field reset, the two `FitsInFragmentainer`/
  `EffectiveContentTop` lines, and the `TryRestartAt` guard + assignment), read directly off the
  `coverlet` Cobertura output line-by-line (the `diff-cover` CLI itself was not available in this
  environment).
- `dotnet build PeachPDF.slnx -t:Rebuild`, both Debug and Release: 0 warnings.

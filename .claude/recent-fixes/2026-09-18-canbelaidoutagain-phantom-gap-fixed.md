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
  (`EarlyBreakLayoutIntegrationTests.cs`), swept across a confirmed-band sample of filler heights.
- Full `net8.0` suite: 12,529+ passed (count drifts upward as unrelated PRs land concurrently on `main`),
  0 failed, 9 pre-existing platform skips — including `EarlyBreakLayoutIntegrationTests`,
  `BandMembershipToleranceTests`, and `MonolithicContentLayoutIntegrationTests` specifically, per this
  repo's own warning that this measurement is shared by every `break-inside:avoid`/monolithic/
  orphans-widows mover.
- Diff coverage: 100% on the 5 changed/added production lines (field reset, the two `FitsInFragmentainer`/
  `EffectiveContentTop` lines, and the `TryRestartAt` guard + assignment), verified with `diff-cover`
  itself (`python -m pip install diff-cover`) against the exact multi-framework Cobertura report set
  `test.yml` produces (net8.0/net10.0/net11.0 + CLI + source-generator tests merged).
- `dotnet build PeachPDF.slnx -t:Rebuild`, both Debug and Release: 0 warnings.

## Two CI-only failures found and fixed after this landed locally, worth knowing before repeating them

**#1: a DEBUG-only test region is invisible to a diff-coverage gate that builds Release.** The first
version of `MultiLineHeadingRelocatedByBreakInsideAvoid_FitsWithinDestinationPage` was declared inside
this file's existing `#if DEBUG` region (alongside the word-claim-ledger tests it was modeled on), even
though it does not use the ledger at all. `test.yml`'s coverage job builds `--configuration Release`,
which undefines `DEBUG` and strips that whole region out of the compiled assembly — so the one branch
that exercises `TryRestartAt` relocating a box's own first in-flow child (`_firstChildRestartedTop`'s
assignment) was never hit there, failing the 90% gate on that single line despite every local
`dotnet test --framework net8.0` (always Debug) run showing 100%. Fix: moved the test, and the
`Issue1047Document` fixture the pinned-font variant is modeled on, outside the guard. **A new test that
exercises production code the diff-coverage gate needs covered must never be placed inside an existing
`#if DEBUG` region without checking whether that region's own tests are what's supplying the coverage —
they silently aren't, in Release.**

**#2: an un-pinned font fixture reproduced on Windows but not on Linux/macOS CI** — exactly the class of
flakiness `feedback_font_metric_changes_break_unpinned_ci_fixtures` (project memory) already warns about,
now confirmed for this exact mechanism. `Issue1047Document`'s heading text wraps against whatever font the
running platform resolves the UA default to; at the filler heights that reproduce the restart on Windows,
Linux/macOS wrapped the same markup differently, so `TryRestartAt` never relocated `card`'s own first
in-flow child there at all — the test still *passed* (nothing to overflow if nothing moved), but the
coverage line stayed cold, reproducing failure #1's exact symptom for a different reason on a second CI
run. Fixed by adding `PinnedFontIssue1047Document` (using the same pinned "Early Break Fixture" bundled
font `ResumedParagraphDocument` already established as this file's idiom for exactly this problem) and
re-sweeping its own reproduction band with temporary `AsyncLocal`-based instrumentation reading back from
`TryRestartAt` (not kept) rather than assuming the Windows-only band would port. `Issue1047Document`
itself was deliberately left un-pinned — it only backs the pre-existing DEBUG-only ledger tests, which run
locally rather than in CI's Release job, so pinning it would have meant re-sweeping and re-documenting
their own already-verified bands for no benefit to them.

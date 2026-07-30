# A reset box's whole subtree may retake its forced break

Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320), area epic
[#498](https://github.com/jhaygood86/PeachPDF/issues/498). Closes
[#434](https://github.com/jhaygood86/PeachPDF/issues/434). Incidentally fixes the direct-child-of-wrapper
shape of [#395](https://github.com/jhaygood86/PeachPDF/issues/395) (its own multicol-scoped pinned test,
not the issue as a whole — see below).

## The load-bearing idea

`CssBox.ResetForRefill` — called by `PassRewind.RollBackTo` for every box a re-entered pass (the driver's
keep-with-next rewind, the columns engine's abandoned fill retry) lays out from scratch — cleared
`_prologueDone` only on the box it was called on, never on that box's descendants. `PlacedByForcedBreak`,
the one-shot "this box's forced break has already been taken" latch, is reset to `false` inside the
prologue (`PerformLayoutPrologue`), so a descendant whose own `_prologueDone` never gets cleared skips
that line and keeps reporting its break as already taken — even though its whole subtree, including that
descendant, is being laid out again from the top. The break is then silently never retaken.

#434's own text already ruled out the obvious fix (recursing `_prologueDone` fully) as too expensive and
too risky: it would re-measure every word and re-run every `string-set`/named-page registration in the
replayed subtree, exactly the class of work #332's registration-leak fix had to make idempotent once
already. The fix taken instead separates the two facts `PlacedByForcedBreak` was already carrying as one
field before today's `#548` (`_forcedBreakTop` retirement) — "has this box's break been taken" is
independent of the position-dependent, once-per-layout parts of the prologue, and clearing it does not
need the descendant's `RectanglesReset`/word-measurement/registration to run again. `ResetForRefill` now
also calls a new `AllowDescendantForcedBreaksToBeRetaken`, a plain recursive walk that clears
`PlacedByForcedBreak` — and nothing else — on every box in the subtree. Safe on its own:
`_isForcedBreak`/`_forcedBreakSide`/`_adjoinsForcedBreakPoint` are settled from style alone and do not go
stale between passes, which is the same fact `#548`'s own commit already established when it split them
out of the old `_forcedBreakTop`.

The other half of #434's own proposed fix — "a rewind to the first pass carries a null token and rolls
nothing back" — turned out to already be fixed, by `#415`'s post-change-review catch (`PassRewind.RollBackTo`'s
`resume is null` branch already resets every one of `fromTheStart`'s children). Confirmed by reading
`PassRewind.cs` directly before writing anything, rather than assumed from the issue's own description of
an earlier code state.

## What was found by running it, not by reading it

**Building the fixture itself was the hard part, and what it disproved is worth recording.** The obvious
construction — wrap a second heading and its card in a `<div>`, give the heading `break-before: page` —
does not reproduce the bug, because css-break-3 §3.1's own first-child propagation hoists `_isForcedBreak`
off the heading and onto the wrapper `<div>` (the heading is the wrapper's first in-flow child), which
puts the break back at the *same* depth `PassRewind.RollBackTo` already resets directly — not the
grandchild depth #434 needs. A second attempt, giving the heading a preceding sibling inside the wrapper
so propagation has no first-child chain to climb, does keep `_isForcedBreak` on the heading itself — but
even then, most parameter combinations reproduce nothing observable, because ordinary "does not fit,
retry on the next page" overflow independently lands the heading at the same next-page top the forced
break would have chosen anyway, masking the bug behind a coincidence. Only sweeping for combinations
where the heading's own page has **deliberate slack** left after its wrapper's leading content (so
ordinary flow would keep it on the same page) turned up genuine failures — confirmed load-bearing by
reverting the fix and re-running: all four chosen combinations then fail at the exact assertion meant to
catch this. Direct instrumentation (a temporary trace of every `ResetForRefill` call and every forced-break
placement attempt, removed before landing) is what found the propagation hoist and the overflow
coincidence; neither was visible from reading the placement code alone.

**The same fix, run against the existing #395 fixture, silently turned a documented gap into passing
output** — `MulticolLayoutIntegrationTests.AForcedPageBreak_BelowTheContainersOwnChild_IsLostEntirely_KnownBoundary`
started failing (its own pinned "still broken" assertion no longer held) the moment the fix landed, purely
because both the driver's run-pull rewind and the columns engine's fill retry call the same
`PassRewind.RollBackTo`/`ResetForRefill`. #395's own issue text already named this as one of its two
proposed fixes ("re-open the prologue for the whole subtree a discarded fill attempt is replaying"), and
its test's own doc comment said closing it "should turn this into the invariant it was drafted as" — which
is exactly what happened, so the test was rewritten as that invariant
(`AForcedPageBreak_BelowTheContainersOwnChild_StartsTheNextPage`) rather than left red or its assertion
quietly flipped. **Not folded into "closing #395"**: the issue's own text also names flex, grid and table
as the same shape for their own measure-then-place engines, and neither this fix nor its testing touched
or verified those — #395 stays open for that remainder, with a pointer left in both the invariant file and
a GitHub comment so a future reader does not have to re-derive which part is settled.

**Real showcase content exercises this, not only synthetic fixtures.** `paged_media_keep_with_next`'s own
`ResumedPassSection` carries `<h1 style="break-before:page">` immediately after a paragraph that itself
straddles a page boundary — precisely the shape #434 describes — and on unfixed `main` that heading landed
crammed under the previous section's content instead of opening a fresh page. Fixed, the showcase gains a
page (4 → 5), rasterized and read side by side in both PDFium and MuPDF to confirm the heading now opens
page 3 cleanly with nothing stranded above it, rather than trusted from coordinates alone.

## What was deliberately not done

- **Flex/grid/table's own "same shape" claim in #395 was not verified.** No existing fixture in this repo
  exercises a forced break below a flex/grid/table item's own child, and building one was out of #434's
  scope. Noted on `#395` directly rather than assumed fixed.
- **`_prologueDone` itself was not made recursive.** See "the load-bearing idea" above — this is the
  narrower, deliberately scoped alternative #434's own issue text asked for.

## Evidence

Full `net8.0` suite green (7249 passing, 9 skipped, 0 failed) and CLI suite green (96 passing).
Zero-warning `dotnet build PeachPDF.slnx -t:Rebuild`. **100% diff coverage** against `origin/main`. Full
78-showcase corpus: 77 byte-identical (normalized for `/CreationDate`, `/ID`, font subset tags), one
(`paged_media_keep_with_next`) deliberately changed and verified by rasterizing the changed pages in both
PDFium and MuPDF. New `PulledRun_ReEnteringAPassThatResumedIntoAParagraph_RetakesAForcedBreakOnAGrandchild`
(4 parameter combinations, each individually verified load-bearing) plus the rewritten
`AForcedPageBreak_BelowTheContainersOwnChild_StartsTheNextPage`.

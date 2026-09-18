# #1047's fragment double-claim: fixed at both defects the diagnosis (#1200) found

Final PR of the post-0.9.18 batch. #1200 diagnosed the mechanism (DEBUG-only word-claim ledger,
`HtmlContainerInt.VerifyWordClaims`) but didn't fix it, and named `CssBox.TryRestartAt`'s missing cursor
step as "the strongest fix candidate." That candidate (Option A) was necessary but **not sufficient** -
this is the one thing worth a future reader knowing before re-deriving it: fixing it alone still left the
ledger throwing. A second, independent defect had to be found and fixed too.

## Option A: `TryRestartAt` steps the fragmentainer cursor (implemented, confirmed necessary)

`CssBox.TryRestartAt`'s same-pass keep-with-next restart repositioned a run head (e.g. a multi-line
heading) to the destination page's top via `Boxes[resumeFrom].ResumeAt(null, restart.Top)`, but never
advanced `HtmlContainer.CurrentFragmentainer`'s own cursor to match. The restarted head re-measured
against the OLD, already-exhausted band, which could make it keep the same too-few lines a second time,
costing an extra fragmentainer pass. Fixed with one line: `HtmlContainer?.CurrentFragmentainer?.StepOverTo(restart.Slot)`
before `ResumeAt` - `EarlyBreak.Slot` already names the exact destination slot `restart.Top` was computed
against, so no new lookup is needed. This mirrors every other mechanism that places content past the
fragmentainer being filled (a forced break, a §5.2 flush, a table row-loop band jump, a flex/grid line
relocation), all of which already call `StepOverTo` for the same reason
(`FragmentainerContext.StepOverTo`'s own remarks).

Confirmed via temporary `File.AppendAllText` tracing (not kept) that this fix alone resolves the "0 lines
kept twice, extra pass" half of the mechanism - the restarted heading now gets its lines on the first
retry. **It did not, on its own, stop the word-claim ledger from throwing.**

## The second defect: `CanBeLaidOutAgain`'s naive extent measurement (found by running Option A, not
## fixed - tracked as a follow-up instead)

With Option A alone, the enclosing `break-inside:avoid` box's own reported height
(`ActualBottom - Location.Y`) still overstates the room a fresh re-layout would need, because the restart
above moves the run's content away from the box's own top without moving the box's own `Location.Y` -
leaving a "phantom gap" baked into the raw span. `CssBox.CanBeLaidOutAgain`'s destination-fit check reads
that raw span directly (`FitsInFragmentainer` on `ActualBottom - Location.Y`), so it can still answer
"does not fit" for a destination the box's *real* content would fit easily, forcing the older
`TranslateForEarlyBreak` fallback (a blind `OffsetTop` shift, not a re-layout) - which can still land a
line past a fragmentainer boundary by more than a rounding tolerance.

This is a real, separate defect (not fixed in this PR - the fix would mean giving `CanBeLaidOutAgain` a
notion of "real content footprint" distinct from `Location.Y`-to-`ActualBottom`, which risks broad
regressions across every `break-inside:avoid`/monolithic/orphans-widows fixture that already depends on
that same measurement, for a repo that has already paid once for exactly this kind of "reads like a small
fix, has a huge blast radius" mistake in this area). Left as a residual imprecision: the translate
fallback still fires more often than the content's real footprint warrants, and can leave a relocated box
overflowing its destination page's own content area by a small amount. Every word still lands on exactly
one page - **this residual is not #1047's own double-claim shape** - so it does not need its own
accepted-gap file (it is not a CSS spec deviation; §5.3 already sanctions relaxing an unsatisfiable
`avoid` by moving the box anyway). Flagged as a follow-up task instead of an issue, since it is an
implementation-precision question, not a behavior gap a document author would notice as spec
non-conformance.

## Option B: `ClaimsLine`'s `FallsPast` tie-break, gated to the sweep's own `fromSlot` (implemented, is
## what actually closes the ledger)

`FragmentEmitter.ClaimsLine`'s tie-break exists for issue #1054 (negative `line-height` letting a line's
ink escape past an already-closed page) - it grants the currently-asked slot a claim when the line's
*nominal* slot (`SlotStartingAt(rect.Top)`) is one the ordinary test can never reach. The tie-break's own
doc comment believed it now had "no live case" reaching it at all, since #1054's own historical second
source (flex/grid measure-then-translate) was closed by #430/#517/#526. #1200's ledger proved that wrong:
the SAME tie-break also fires for a line that the ordinary test **already** claimed correctly earlier in
the same multi-slot `EmitPass` sweep, then falls past its own band for an unrelated reason (this issue's
`TranslateForEarlyBreak` fallback above, plus three further independently-diagnosed shapes out of scope
for this PR - a table rowspan continuation, a multi-column no-progress backstop, a flex/grid wrapping
column) - granting the SAME line a second, conflicting claim on the next slot.

The fix distinguishes the two: added `FragmentEmitter._currentPassFromSlot` (set/cleared around
`EmitPass`'s own body, the same pattern `_currentPassIsFinal`/`_pruningSuspended` already use rather than
threading a new parameter through every `BuildDraft`/`ChildrenOf` call). The tie-break now requires the
line's nominal slot to be **strictly before** the current sweep's own `fromSlot` - genuinely unreachable,
not merely disagreeing with the slot being asked about. #1054's own rescue is always a single-slot sweep
(its nominal slot was closed out by a wholly separate, earlier pass), so this holds trivially for it.
#1047's shape is always a *later* slot in a sweep whose own `fromSlot` already, correctly, claimed the
line via the ordinary test - excluded by the same comparison, since that correct claim's own slot is by
construction `>= fromSlot`. Outside of a multi-slot `EmitPass` sweep (`CatchUpStaleSlotsBehind`,
`Finish`'s replay, `EmitReservedBlankSlots`), `_currentPassFromSlot` is null and the tie-break is
unconditional, exactly as before - those callers each re-emit one isolated slot at a time with no
sweep-relative "already claimed elsewhere" question to ask.

**A narrower first attempt (removing the tie-break entirely) was tried and rejected**: it closed #1047
but broke `LineBoxFragmentMembershipIntegrationTests.NegativeLeadingLineAtAPageBoundary_ClaimsEveryWordOnTheSamePage`
outright (0 claims instead of 1) - direct proof the tie-break is not simply dead code, and that #1054's
rescue and #1047's bonus-claim bug are the same mechanism serving two different purposes. A first version
of the gating condition (`slotIndex == fromSlot`) also worked for every fixture in this suite, but is
weaker than necessary: it would wrongly exclude a legitimate #1054-shaped rescue needed at a slot *after*
`fromSlot` in a multi-slot sweep whose nominal slot is two or more slots behind. `nominalSlot < fromSlot`
is the semantically correct invariant and was substituted before landing.

## Tests

`EarlyBreakLayoutIntegrationTests.cs`: #1200's own repro (`Assert.ThrowsAsync`) flipped to two positive
assertions - `MultiLineHeadingRelocatedByBreakInsideAvoid_ClaimsEveryWordExactlyOnce_Issue1047` (single
point) and its `_AcrossTheConfirmedBand` sibling (the same 6-value filler-height sweep #1200 confirmed
reproduces in), each asserting every word is claimed by exactly one fragmentainer (via the DEBUG-only
ledger, still enabled) and that the heading and its `break-inside:avoid` container land on the same,
single page.

## Evidence

Full net8.0 suite green (12,485 passed, 9 pre-existing platform skips, 0 failures) - including
`EarlyBreakLayoutIntegrationTests`, `StaleSlotChildCursorTests`, `BandMembershipToleranceTests`,
`StraddlingLineClaimTests`, and `LineBoxFragmentMembershipIntegrationTests` run both in isolation and as
part of the full run. Diff coverage on the 9 changed/added production lines: 100%. Solution rebuild
(`dotnet build PeachPDF.slnx -t:Rebuild`), both Debug and Release configurations: 0 warnings - confirming
the fix works whether or not the DEBUG-only ledger it was diagnosed through is compiled in.

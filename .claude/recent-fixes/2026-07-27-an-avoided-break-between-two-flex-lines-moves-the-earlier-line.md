# An avoided break between two flex lines moves the earlier line

_Landed 2026-07-27. [Issue #447](https://github.com/jhaygood86/PeachPDF/issues/447), with
[#450](https://github.com/jhaygood86/PeachPDF/issues/450) and
[#451](https://github.com/jhaygood86/PeachPDF/issues/451)._

Three break-point defects in the one pass that moves a flex line or grid row across a fragmentainer
boundary, all three of them the other half of something
[#441 fixed for forced breaks](2026-07-27-either-side-of-a-flex-or-grid-break-point-may-force-it.md).

**§3.1 avoidance was inert.** The pass asked its lines only whether anything in them *may not be cut*
(`break-inside`, §2 monolithic content), so `break-after: avoid` on one line's items and
`break-before: avoid` on the next line's said nothing at all. The UA print sheet's
`h1-h6 { page-break-after: avoid }` makes that the ordinary case for a heading that is a flex item.

**The load-bearing idea is that avoidance is not another argument to `DeltaFor`.** A forced break is a
statement about where the *later* line goes, and `DeltaFor` answers exactly that, one line at a time, in
a forward walk. An avoided break is a statement about the **earlier** line, and it can only be answered
*after* the later line has landed — whether a boundary really falls at a break point is a question about
where the line below it ended up, and a line the boundary cuts *through* has taken no break at that point
at all. So the walk now keeps `_tops`/`_bottoms`/`_applied` per line and reaches back over lines it has
already placed, which is why it became a small `Walk` class rather than a fold.

The relocation itself is **superseded, not added to**: the later line moved to open the destination
fragmentainer, and once a run travels the run's *head* opens it instead, so the line's own delta is
replaced by the run's rather than accumulated on top of it. Adding them was the first thing tried and it
puts the group a whole line-height too low.

**§4.3's ladder is now shared rather than re-derived.** `EarlyBreak.TravellingRunHead` is the ladder
lifted out of `TravellingRun` as arithmetic over coordinates — it says nothing about what a run *member*
is, which is what lets a chain of flex lines use it when a chain of siblings is what `DomUtils` walks.
Both guards transfer unchanged: the run head must sit strictly below the content top of the fragmentainer
being left, and the run plus the subject must fit the destination band.

**Both engines now hand `LineRelocation` a list of `LineGroup`s and it owns the whole walk.** #441 left one
call site each precisely so this could absorb them; the engines' remaining job is reducing their layout to
lines in block-axis order (and, for grid, keying the earlier side by where an item *ends* —
`BoxesByEndRow`, unchanged).

**#450, the directional side.** `ForcedBreakBetween` now returns the winning `PageSide?` rather than a
bool, because a side cannot be recovered from "yes". Within one side the most demanding value wins (a
directional value subsumes a plain `page`); across the two sides `BreakValues.RequiredSide` applies §3.1's
pair rule, so the two answers are the ones block flow already gives. `DeltaFor` then does #302's two
halves: step the target while `!SlotIsOn`, and reserve the stepped-over slot so the emitter materializes
it — without the reservation the slot holds nothing printable, CSS Paged Media 3 §3.2 drops it, and `recto`
is indistinguishable from `page`. **Exactly one step is ever needed** (parity alternates slot to slot), so
it is an `if`, not a loop — and it *retracts* the reservation where no step is needed, since a pass that
re-decides the same line would otherwise leave a blank page the final layout does not want.

**#451, the flush boundary.** `SlotStartingAt` resolves a top edge flush on a boundary to the *later* slot,
so `PageTopOf(slot + 1) - top` was a whole band for a line whose break had already happened. The guard
clears `forcedBreak` rather than returning 0, which matters: a flush line that *also* straddles then falls
through to the taller-than-a-band guard instead of being moved a page for nothing.

**#450 and #451 are one decision, not two neighbours, and this is the thing #451's own text got backwards.**
Its body reasoned that the slot attribution it fixes is what a directional break's parity arithmetic depends
on, i.e. that #450 waits on #451. The dependency runs the other way: §4.4 satisfies a break that has already
happened, but a *directional* value asks for more than a boundary — it asks that the content **begin** on a
page of the named side — so the flush guard has to consult the winning side, which is exactly the value #450
introduced. Written without it, the guard silently downgrades `recto` to `page` for any line that happens to
land flush: measured at 160pt of filler, `break-before: recto` stays on slot 1, which is page 2, a left page.
`break-before: verso` on the same fixture correctly stays. Both are pinned.

## What was found by running it rather than by reading it

**The forced-break stop in the chain walk is redundant, and no fixture can distinguish it.**
`DomUtils.GetPrecedingKeepWithNextRun` stops a chain at a forced break point, and the line chain copies it
— but neutralizing that clause fails **nothing**, because §4.3's first guard already rejects any member
sitting at or above the content top of the fragmentainer being left, and a surviving forced break *always*
lands its later line exactly on a fragmentainer content top (`DeltaFor` clears the flush case and otherwise
`PageTopOf(target) > top`, so the delta is always positive). It is kept anyway: the equality it relies on
is exact only up to floating-point noise in `top + (PageTopOf(target) - top)`, and one ulp the wrong way
would let a line at a page top travel. Worth knowing before "simplifying" it.

**The gap between the two questions is 1pt wide.** #451 reproduces at exactly one band of filler and
nowhere else: 159pt lands the item at y=180 (correct), 160pt at y=340 (a slot skipped), 161pt at y=340
(correct — its top was genuinely past slot 1's). Both neighbours are pinned, because a guard that fires one
point too early is the same defect mirrored.

**The showcase fixture needed 11 filler paragraphs, not 8.** The first attempt put both lines comfortably
on one page, so the section demonstrated nothing and passed inspection anyway — a showcase that does not
straddle is indistinguishable from a fix that works. Verified the other way round by building `main`'s
library against the *new* harness: on the unfixed build the "Heading line" card sits alone at the foot of
page 8 with its section body on page 9, which is the defect exactly.

**The §4.4 guard and the parity walk have to agree about which slot the line *begins*, and getting that
wrong made `recto` a no-op again in exactly the case the guard was written for.** A flush line does not
merely *reach* its slot, it **begins** it — so where the flush guard finds the side unsatisfied, the slot
that has to stay blank is *that* one, not the next. Starting the walk at `slot + 1` regardless meant parity
was already satisfied one slot on, so nothing was reserved (worse: the reservation was actively *retracted*),
the vacated slot held no printable content, and CSS Paged Media 3 §3.2 dropped it — leaving the line printed
on precisely the page it was moved off. **Measured against block flow on the same fixture**: 160pt of filler
and `break-before: recto` gives 3 pages in block flow (blank p2, content p3, a right page) and gave **2** in
flex, with the content on a left page. The test that missed it asserted the *slot index*, which was already
right — the loss happened downstream in the emitter. Any test for a directional break has to assert
`FragmentTree.Fragmentainers.Count` as well as the slot.

**A blank-slot reservation outlives the pass that made it unless something retracts it.** `DeltaFor` reaches
`SetBlankSlotReservation` only inside its forced-break arm, so every other exit — no break, a line taller
than the band, the §4.4 guard clearing the value, the engines' own early returns — leaves an earlier
attempt's reservation standing, and `ClearBlankSlotReservations` runs once per `LayoutDocument` while the
driver re-enters passes within one. Block flow's equivalent retraction lives in the prologue, which is once
per layout *generation*, so it does not repeat on a re-entered pass and could not have covered this.
`Relocate` now retracts for every line's own key before the walk re-decides. No fixture reaches it — it is
latent, not observed — which is why it is a two-line unconditional retraction rather than a mechanism.

**The cursor #443 introduced is not part of this.** Adding `CurrentFragmentainer?.StepOverTo(target)` to
`DeltaFor` — the relocation does put the pass into a later slot — changes **nothing** measurable: the full
suite is identical with and without it (6683 either way). The staleness is unreachable from here, because
the only reader that cares (`HasRoomAboveInThisFragmentainer`) asks about a box at the very *top* of a
band, and after a line relocation nothing block-flow places is ever there — the relocated line is placed by
the engine, and every following sibling is below it. Left out rather than shipped unverifiable, and filed.

## Evidence

Tests: `FlexGridFragmentationIntegrationTests` +27 cases across both engines, counting theory rows.
Verified load-bearing by neutralizing each part in turn: the §4.4 flush guard → **2 fail**; its side check
→ **1**; the flush parity-walk start → **1**; the parity walk and reservation → **5**; the directional
*value* (`RequiredSide` → `PageSide.Any`) → the same **5**; §3.1's latest-in-flow tie-break within one side
→ **2**; avoidance entirely → **10**; §4.3's ladder (head forced to 0) → **2**, and those two are the
relaxation cases, so the ladder is pinned in both directions rather than only being present.

Full net8.0 suite green (6685), CLI green (96), **100% diff coverage**, zero-warning
`dotnet build PeachPDF.slnx -t:Rebuild`. **68 of 69 showcases byte-identical**; `acid2` differs only in the
generating worktree's path inside a `file://` annotation URI and rasterizes to identical bytes;
`paged_media_monolithic_content` gained the section above. Verified in PDFium.

**The three defects the post-change review turned up were all in the same method**, and only one of them
(the redundant chain stop) was benign — the flush/parity interaction and the stale reservation are both
above. Worth reading `DeltaFor` as a whole rather than as a sequence of guards: `slot`, `flush` and `side`
are one decision about where the line begins, and each guard that treats them separately has been wrong.

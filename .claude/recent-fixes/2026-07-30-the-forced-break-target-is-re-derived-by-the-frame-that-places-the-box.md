# The forced-break target is re-derived by the frame that places the box

Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320). Part of [#515](https://github.com/jhaygood86/PeachPDF/issues/515)
(`#390`'s stage 2). Closes [#539](https://github.com/jhaygood86/PeachPDF/issues/539) (`#515.2`). Filed
[#545](https://github.com/jhaygood86/PeachPDF/issues/545) for what it found and could not fix.

## The load-bearing idea

Where a forced break (css-break-3 §3.1) puts a box is resolved against the **predecessor the frame placed**,
whose bottom edge is exactly the thing that moves between one placement and the next — so it is a per-placement
question, not a once-per-layout one. `CssBox.PerformLayoutPrologue` settled it once into `_forcedBreakTop`
(`breakAnchor.StaticBottom`, `SlotEndingAt(prevBottom) + 1`, §4.4's flush-boundary epsilon, `PageTopOf(slot)`),
which meant the answer had to survive every mechanism that retracts a pass's work. It moves to
`CssBox.ForcedBreakTopFor(child)` on the frame, asked afresh at every placement.

The prologue keeps what is genuinely once-per-layout and cannot go stale underneath the box: `UsedPageName`,
`_shouldRegisterPage`, `_isForcedBreak`, `_adjoinsForcedBreakPoint`, and `_forcedBreakSide` — the last because a
*side* is settled by the two break values at this break point and by nothing geometric, which is also why it
could be hoisted out of the anchor guard the target's removal deleted.

**`PlacedByForcedBreak` turns out to already be the "break has been taken" latch**, and that is what makes the
extraction provably neutral rather than merely plausible. It has exactly three write sites — `false` in the
prologue, `true` in the forced arm, `true` in the escaped-resume branch — which mirror `_forcedBreakTop`'s
null/set/consume lifecycle one for one. So `child._forcedBreakTop is { } forcedTop` and
`!child.PlacedByForcedBreak && ForcedBreakTopFor(child) is { } forcedTop` are the same predicate, and the arm's
own `_forcedBreakTop = null` consume becomes redundant with the `PlacedByForcedBreak = true` on the next line.
Two facts, one field, documented as such.

Also converted, all behaviour-neutral read-throughs: §5.2's margin-crossing test and its keep-with-next pull
(`SlotEndingAt`/`BandOfSlot`/`FallsPast`/`BandOfSlot(prevSlot + 1).Height` → one `BlockConstraint`), and
`LayoutBlockChildren`'s orphans arm target (`PageTopOf(ResumeSlotIndex)` → `AtSlot(...).AbsoluteBandTop`). The
directional blank-slot walk became `StepPastSlotsOnTheWrongSide`, shared rather than inline.

## What was found by running it, not by reading it

**The retirement `#539` was written around does not hold, and the reason is a defect rather than a design.** The
issue's thesis was that re-deriving the target makes `_escapedForcedBreakPending`/`_escapedForcedBreakBlankSlot`
— the pair that carries an escaping forced break's facts from the pass that decides it to the pass that places
it — unnecessary. Implemented, it turned
`DirectionalPageBreakIntegrationTests.DirectionalBreakInsideMulticol_DegradesToAColumnBreak_KnownBoundary` from
two pages into three with the middle one blank. Tracing both sides showed why:

- The first attempt's gate (`!PlacedByForcedBreak && ForcedBreakTopFor(child) is not null`) is too broad. A box
  inside a multi-column container is resumed at the next column's top for reasons that have nothing to do with
  its own `break-before`, and "carries a forced break and is being resumed at a target" is not the same
  statement as "this target is that break's". Only the record can tell them apart, and it already does —
  `BlockBreakToken.EscapesNestedFragmentainer`.
- Sourcing it from the record instead **still** failed the same test, and that is the real finding. Both fields
  are cleared in `BeginLayoutPass` when the **layout generation** changes; the record is not, because the driver
  re-feeds it on the reflow layout that follows. So the same escaping break is resumed in both generations, the
  first re-asserts and the second deliberately asserts nothing — and the second is the one that produces the
  document. Traced directly: `resume pending=True blank=1` then `resume pending=False blank=1`, same
  `resumedTop=540`. Anything re-derived answers on both, which re-reserves a page the first generation's own
  prologue had already retracted.

The blank page is very likely what §3.1 actually asks for, so this is a bug worth fixing — but fixing it is
changing output, and `#515`'s PR5 is the only one in the sequence allowed to. Filed as
[#545](https://github.com/jhaygood86/PeachPDF/issues/545) with the trace, and noted on the fields themselves so
the next attempt does not re-derive the same dead end. That test's own comment is also wrong as written ("the
parity step and its reservation are therefore not taken") — the step *is* taken and lands on the right-hand
side; only the reservation goes missing.

**The keep-with-next first-line retry is dead for a structural reason, not a missing fixture — measured a second
time, and this time with a mechanism.** `#538` found `firstLinePage > ownPage` true 244 times across the suite
with `keepWithNextRun.Count > 0` on none of them, and deferred both the conversion and the fixture here. Built
the fixture: it cannot be built. Instrumenting the guard across the whole suite showed the dominant reachable
shape (198 of the 244) is a `<p>` after a `<p>` with fragmenting suppressed, and adding an `<h2>` to make the
run non-empty makes the case disappear — the heading is pulled to the next page's top *before* the epilogue is
reached, so the box's own top and its first line end up on the same page. That is general, not incidental:
every mover that would relocate a box whose first line lands on the next page already pulls the run
(`EarlyBreak.Discover` + `TryRestartAt` on `LayoutBlockChildren`'s §3.1-propagation and orphans arms,
`PlaceBlockChild`'s own §5.2 pull), and all of them run on the pass that *declines* to place the box — which
returns from `PerformLayoutImp` before the epilogue at all. What reaches this block is the case where none of
them applied, which is the case with no run.

**`CollapsedMarginBefore` is not pure**, which mattered for the abandoned retirement and is worth writing down.
It writes `CollapsedMarginTop` and *consumes* `_groupTopMarginOverride`. It is safe to call twice only because
`CollapsedMarginTop` has no read site anywhere in the repo and the unconditional call above both branches has
already spent the override — not because the method is idempotent.

**`BlockConstraint` needed two new members rather than reuse of the two obvious ones.** `Straddles` compares
with a bare `>` and `For` uses `PageIndexOf`'s top-edge convention; §5.2's test is a *bottom* edge asked with
`HtmlContainerInt.FallsPast`'s `PageBoundaryEpsilon`. Substituting either way moves a box a page, so `FallsPast`
and `EndingAt` were added and documented as the deliberate pair to `Straddles`/`For`. `BlockConstraintTests` now
asserts the two conventions disagreeing on the same coordinate, so a future simplification that merges them
fails loudly.

## What was deliberately not done

- **`_escapedForcedBreakPending`/`_escapedForcedBreakBlankSlot` were kept.** See above — retiring them changes
  output, which this PR may not do. Both the fields and `#545` now carry the measurement and the shape a fix
  would take, so the next attempt starts from the finding rather than from the issue's original claim.
- **The keep-with-next first-line retry was left unconverted**, for the second time and on stronger evidence
  than the first. Converting it would have added uncovered lines to a block nothing reaches; deleting it is a
  separate decision (proving it dead is not proving it unnecessary) and belongs with `#545`. The reasoning is
  now recorded at the block itself rather than only in a note that expires.
- **`LayoutBlockChildren`'s `MeasureIsSharedBetween(SlotStartingAt(childBox.Location.Y), ...)` was not
  converted.** `BlockConstraint.For` would have silently swapped `SlotStartingAt` for `PageIndexOf` — a
  behaviour change wearing a refactor's clothes, and the same epsilon trap as above. Only the `orphanTarget`
  read, which is a genuine band read, went through the constraint.
- **`EarlyBreak.Discover` was left alone**, as `#538` decided and this PR's own diff gave no reason to revisit.

## Evidence

Full `net8.0` suite green (**7115 passing**, 9 skipped, 0 failed — 7093 baseline plus 22 new) and CLI suite green
(96 passing). Zero-warning `dotnet build PeachPDF.slnx -t:Rebuild`. **100% diff coverage** against `origin/main`
(69 lines, 0 missing). **Byte-identical across the full 77-showcase corpus** vs `origin/main`, normalized for
`/CreationDate`, `/ID`, font subset tags, annotation `/NM`/`/M` and PDFsharp's plaintext creation-date/time
header lines — which is the real proof this is the neutral refactor it is supposed to be, given how much of the
break machinery it moves. New `ForcedBreakTargetIsTheFramesTests` (11) asks the frame for the target *after*
layout — the assertion a latched field could not pass — and pins §4.4's flush-boundary epsilon, the
consecutive-forced-break case it must not swallow, the preserved-margin case, §3.1 propagation hoisting the
break off a first in-flow child, the named-page transition that propagation does *not* hoist (the one thing that
exercises the anchor climb), the relative-offset cases, and both null answers. All five tripwire classes named
on `#539` pass.

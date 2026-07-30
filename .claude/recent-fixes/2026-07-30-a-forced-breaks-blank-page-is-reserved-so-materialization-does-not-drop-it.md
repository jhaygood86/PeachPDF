# A forced break's blank page is reserved, so materialization does not drop it

Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320), area epic
[#498](https://github.com/jhaygood86/PeachPDF/issues/498). Closes
[#147](https://github.com/jhaygood86/PeachPDF/issues/147).

## The load-bearing idea

#147 described two separate defects behind the same fixture (`A|B|C`, `B` empty, both `A` and `B`
carrying `page-break-after: always`) — a layout-position bug (the break between `B` and `C` collapsed
through `B` to `A`'s bottom) and a materialization bug (even once positioned correctly, `B`'s own
content-empty page could still be dropped by CSS Paged Media 3 §3.2's content-empty-page skip). Reading
the current code against the issue found the **first half already fixed**, as an unlabelled side effect
of the unrelated `#390`/`#515` "re-derive a forced break's target at every placement" work
(`CssBox.ForcedBreakTopFor`/`PlacedByForcedBreak`, landed the same day as this fix): `FoldMarginsPrecedingChild`
already treats a `PlacedByForcedBreak` box as a real position anchor even when it is otherwise
margin-collapse-through, so `B` no longer gets collapsed through. The two tests #147 pinned
(`PerPageGeometryLayoutIntegrationTests.ConsecutiveForcedBreaks_StillProduceIntentionalBlankSlot`,
`.ForcedBreakAfterCollapseThroughSiblingAtBoundary_TargetsSlotAfterIt`) already passed on `main` — their
own docstrings describe the old bug in the past tense.

What remained was the **second half**: `HtmlContainerInt.SetBlankSlotReservation` — the mechanism that
tells `FragmentEmitter.Finish` to materialize a content-empty slot anyway (`hasPrintableContent ||
container.IsReservedBlankSlot(slot.Index)`) — was called from exactly two of the three places a forced
break can place a box (`StepPastSlotsOnTheWrongSide`'s directional stepping, and the escaped-multicol-resume
branch), never from the **plain** forced-break landing arm in `CssBox.ResolveBlockChildOffset`. A plain
`page-break-after: always` that lands an otherwise content-empty box alone on a slot could still have
that slot silently dropped from the PDF. The fix is one call, mirroring the other two call sites exactly:
reserve the box's own landing slot right after `StepPastSlotsOnTheWrongSide` settles `top`, guarded on
`reservedBlankSlot is null` so it never overwrites a directional break's own stepped-over-slot reservation
(the underlying dictionary holds one slot per owning box).

## What was found by running it, not by reading it

The two pinned tests only assert **layout coordinates** (`Location.Y`), which is a different, earlier
stage than materialization — they would pass even with the gap fully open, since `FragmentEmitter.Finish`
runs once, after all layout, and never feeds back into box positions. The actual proof needed a
fragment-tree-level assertion: a new test
(`EmptyBoxAloneBetweenTwoForcedBreaks_SlotIsMaterializedAsABlankPage`) asserting
`container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex)` is `[0, 1, 2]`, not `[0, 2]` — this is
the one that actually fails on unfixed `main` (confirmed by reverting the fix and re-running it).

## What was deliberately not done

The escaped-forced-break-through-a-nested-fragmentainer path (a plain forced break degrading past a
multicol measurement pass) was left unreserved for the box's own landing slot — only the directional
stepped-over slots are carried across that specific retraction/reassert boundary
(`_escapedForcedBreakBlankSlot`). Reaching it would need a second field mirroring that one, and no
fixture in this repo exercises "a plain forced break escapes a nested fragmentainer AND the box it lands
is itself content-empty" — narrower than #147's own scope, and left rather than guessed at.

## Evidence

Full `net8.0` suite green (7249 passing, 9 skipped, 0 failed) and CLI suite green (96 passing).
Zero-warning `dotnet build PeachPDF.slnx -t:Rebuild`. **100% diff coverage** against `origin/main`. Full
78-showcase corpus byte-identical (normalized for `/CreationDate`, `/ID`, and font subset tags) — no
existing showcase exercises this exact shape.

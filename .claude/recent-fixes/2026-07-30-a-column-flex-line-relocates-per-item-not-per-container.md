# A column-direction flex line relocates per item, not per whole container

Tracker: [#504](https://github.com/jhaygood86/PeachPDF/issues/504). Closes [#455](https://github.com/jhaygood86/PeachPDF/issues/455).

## The load-bearing idea

`LineRelocation.Relocate` walks one ordered list of `LineGroup`s with one running displacement scalar —
correct for `flex-direction: row`/`row-reverse`, whose real break points are between *lines* stacked in
the block axis, and for grid rows, which stay the block-axis unit even under `grid-auto-flow: column`.
For `column`/`column-reverse`, `CssLayoutEngineFlex.BuildLineGroups` used to hand it exactly **one**
group for the whole container — every item of every line flattened together, with only the container's
first in-flow child able to speak for any break point at all. That made every `break-before`/
`break-after` between two items of a column line inert, and folded `break-inside: avoid`/monolithic
content on any single item into one container-wide "may not be cut", moving every item of every line
together.

The fix does not generalize `LineRelocation.Walk` into a multi-chain engine. It calls
`LineRelocation.Relocate` **once per column line** instead of once for the whole container — a new
`BuildColumnItemGroups` builds one line's items into single-item `LineGroup`s in block-axis order,
mirroring `BuildLineGroups`'s row-direction shape one level down (item-to-item instead of
line-to-line), including the same `wrap-reverse`-style flow-vs-block-axis transform (`_isReverse` here,
`_isWrapReverse` there) and the same "only the flow-first entry speaks for the point above the
container" rule. `RelocateLinesAcrossFragmentainers` takes the **max** of the per-line shifts to grow
the container's own height, since column lines run in parallel rather than accumulating onto one
another. Each `Relocate` call gets its own fresh `Walk` state and keys its own blank-slot reservations
to its own boxes, so N independent calls cannot collide; `FragmentainerContext.StepOverTo` is monotonic,
so calling it from N per-line passes just leaves the cursor at whichever line reached furthest — no new
mechanism needed there either.

This uniformly generalizes **every** column-direction container, not just wrapping ones (the issue's
own title says "wrapping", but a non-wrapping single-line container goes through the identical per-item
chain and had the identical "everything moves together" bug, now fixed the same way).

## What was found by running it, not by reading it

**The design's biggest open question — whether two side-by-side lines should stay level with each
other, or relocate fully independently — resolved itself the moment real numbers were run.** The
accepted-gap file's own prose ("a line holding something that may not be cut still moves, and every
line moves with it, so they stay level") reads, out of context, like a requirement to preserve. It
is not: it was describing the *accidental* consequence of there being only one group for the whole
container, not a property anyone asked for independently of that. The issue's own design notes ask
the opposite ("moving an item in one of them must not disturb the other"), and the four new
integration tests confirm that answer is the one that actually composes: two lines forced to
*different* numbers of fragmentainers by their own, unrelated break values (`TwoIndependentColumnLines_EachRelocateByTheirOwnBreakValue`)
land exactly where each one's own value asks, with no coupling between them.

**Two pre-existing tests were pinning the exact coarse-grained behavior the issue reports as the bug**
(`InAWrappingColumnContainer_ALineThatMayNotBeCut_MovesEveryLineWithIt`,
`InAColumnContainer_AForcedBreakBeforeANonFirstItem_MovesNothing`) — both needed their names and
assertions rewritten to the new, correct outcome rather than left as regressions to chase. No other
existing test in the 7,356-test suite needed a single number changed, including the column-reverse and
per-line content-fragmentation coverage from #526/#517, which this change does not touch (relocation
is a geometric pre-pass that already ran before content commit; nothing about that ordering changed).

## What was deliberately not done

- **Grid was not touched.** `grid-auto-flow: column` still has rows as its sole block-axis unit —
  confirmed by reading `CssLayoutEngineGrid.BuildRowGroups`'s own doc comment, which states this
  directly and independently of this change.
- **The "lines stay level" behavior was not preserved.** See above — it was never a real requirement,
  and preserving it would have meant *not* fixing the bug this issue is about.

## Evidence

Full `net8.0` suite: 7,356 passing, 9 skipped, 0 failed. The full `Flex`/`Grid`/`Fragmentation` filter
(823 tests) and `FlexGridFragmentationIntegrationTests` specifically (86 tests, including 4 new ones
targeting this fix directly) both green. Zero-warning build not yet re-verified at the time of writing
this file — see the PR's own CI run for the final confirmation.

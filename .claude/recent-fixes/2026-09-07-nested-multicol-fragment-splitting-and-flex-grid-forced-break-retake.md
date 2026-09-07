# A multi-column container nested inside another one splits per inner column; a flex/grid item's own forced break survives its measurement pass

Two independent fixes landed together (issues #369 and #395), both residuals of earlier fragment-model
work, plus one bug the post-change review pass found in the #369 fix itself before it shipped.

## #369 — nested multi-column splitting

`FragmentEmitter._nested` keys a box's captured per-column geometry by `(CssBox, Slot)` alone.
`FragmentEmitter.ChildrenOf`'s old gate (`nested is null && ...`) consulted a box's own captures only when
that box was not itself already inside a nested fragmentainer — so an inner multi-column container's own
columns were never split once reached through an outer one; its children were read flat off `CssBox.Boxes`
instead, describing only whichever outer column the inner container was filled in *last*.

Fix: `NestedFragmentainer` (the per-capture record) now carries `Self` (the `FragmentainerContext` this
capture was filled under) and `ParentContext` (what was active immediately before that). `ChildrenOf`
always attempts the lookup now, filtering candidates by `ParentContext == nested.Value.Self` when already
nested, so it can tell which outer column's fill a given inner capture belongs to instead of only
consulting one level. Threaded through `CssLayoutEngineColumns.FillColumns`'s single call site into
`RecordNestedFragmentainer`.

**A second bug, found only by the post-change review pass, not by the tests above.** `FragmentKey`
(`(CssBox, CssProxyBox?, Instance)`) disambiguates simultaneous fragments of one box within one slot —
Instance names which column. But `Instance` is assigned by `ChildrenOf`'s own per-call loop (`i + 1`),
which restarts at 1 on *every* call — so once the fix above let a box's own captures be consulted while
nested, two *different* outer columns' own visits to the same inner box's `ChildrenOf` each restart at
Instance 1, and their children collide on identical `FragmentKey`s. `_rectangles[key]`/`_spans[key]`
accumulate across calls sharing a key, so two unrelated same-page fragments' decoration rectangles would
merge under one entry — invisible for a box whose own lines are all it owns (the overwhelmingly common
case: `SliceGeometriesOf` treats those independently regardless of dictionary size), but wrong for an
inline run split across a differently-owned decoration box, where the merged rectangle set genuinely
changes the computed slice geometry.

Fixed at the root rather than patched at each call site: `FragmentKey` gained a fourth field,
`EnclosingContext` (`nested?.Self` at the point a box's own draft is built), defaulting to `null` so every
existing 3-argument construction (the document root, the page-grid resumption-chain keys that never carry
an owner or a real instance either) is unaffected. `FragmentainerContext` has no custom `Equals`, so the
new field's default record-struct equality is exactly the reference identity the fix needs.

## #395 — flex/grid item's own forced break spent by its measurement pass

The direct-child multicol shape of this defect class was already fixed (#434:
`CssBox.AllowDescendantForcedBreaksToBeRetaken` recursively clears `PlacedByForcedBreak` through a
subtree on refill). Flex and grid never called it at all: `CssLayoutEngineFlex.MeasureItem` runs a *real*
layout pass to measure an item, which can set `PlacedByForcedBreak = true` on a forced break somewhere in
the item's own subtree during measurement — and `ItemContentCommit.CommitLayout` (the item's real, final
layout) never reset it before re-entering, so the break was read as already taken and silently never
retaken.

Fix: `ItemContentCommit.CommitLayout`'s fresh-commit branch (`resume is null`) now calls
`box.AllowDescendantForcedBreaksToBeRetaken()` before laying the item out for real — the same reset the
multi-column engine's own refill transition already needed for the identical reason. The method's
visibility changed from `private` to `internal` for this new caller.

## Evidence

Both new tests verified fail-before/pass-after against the pre-fix code (`git stash`/manual hunk revert,
per this repo's testing-rigor convention), not just written and left green:
`FragmentEmitterTests.InnerMulticolSpanningSeveralOuterColumns_SplitsPerInnerColumnNotJustPerOuterOne` and
`FlexGridFragmentationIntegrationTests`' new `Flex_ForcedBreakBelowTheItemsOwnChild_TakesEffect`/
`Grid_ForcedBreakBelowTheItemsOwnChild_TakesEffect`. Full suite (net8.0, 9922 tests) green, solution-wide
`dotnet build -t:Rebuild` clean, diff coverage 100% on changed lines. An 8-angle review pass ran against
the full diff after implementation; the `FragmentKey` collision above is the one finding it surfaced that
changed the shipped code — the rest (a documented, deliberately-unwidened `ClearNestedFragmentainers`,
`AllowDescendantForcedBreaksToBeRetaken`'s lack of its own idempotency guard, `ReferenceEquals` depending
on `FragmentainerContext` staying a reference type) were judged acceptable as-is or too narrow to justify
further scope in this change; see `.claude/invariants/` if a future change needs to revisit them.

**Deliberately not widened:** `ClearNestedFragmentainers`/`ClearNestedFragmentainersFrom` still wipe (or
truncate) a whole `(CssBox, Slot)` entry regardless of which enclosing capture recorded it. A first attempt
threaded the same `ParentContext`-scoped filtering into the clear path too, reasoning that an unconditional
wipe could delete a sibling outer column's still-valid captures — but this broke real, existing tests
(`AColumnContainer_InsideAnotherEngine_EmitsEveryWordExactlyOnce`,
`TwoColumnContainers_InSeparateFlexItemsOnOneLine_BothEmitEveryWordExactlyOnce`): a flex item's own
measurement-then-commit cycle can lay a *top-level* multi-column container out more than once under
different `CurrentFragmentainer` states, and the narrower filter left the measurement pass's own stale
captures behind for the commit pass to double-count. Reverted; the unconditional wipe is correct for every
case this fix's own tests reach. A nested-in-nested container needing a `column-fill: balance` retry may
still be exposed to the narrower version of this problem — not proven, not reproduced, left as a gap for a
future change to find with evidence rather than fixed speculatively here.

# A hoisted stacking participant's ancestor overflow clip now comes from the fragment tree, not the live box tree

Closes #345.

## What was wrong

`RenderUtils.PushAncestorOverflowClips` re-applies an `overflow: hidden` ancestor's clip when painting a
box that `StackingOrder.Flatten` hoisted past one or more plain ancestors for stacking-context z-order
purposes (it paints via the claiming stacking context's own paint loop, bypassing those ancestors' own
nested paint calls, so their clipping never gets picked up "for free"). It resolved each ancestor's clip
rectangle from the ancestor's *live* `CssBox.Bounds`, offset by the participant's own fragment's `OriginY`.
That's wrong for an ancestor shown at more than one place in the document — a hoisted participant inside a
repeated table `<thead>`/`<tfoot>`, whose shared source subtree's live boxes carry only whichever page
positioned them last, exactly the class of bug `FragmentEmitter.OverflowClipOf`/`ClipOf` already fixed for
the *nearest*-ancestor case (#325).

## The fix

`StackingOrder.SearchForHoistableDescendants` already walks the *fragment* tree (`fragment.Children`) and
has the right `childFragment` in hand at the exact point it recorded `childBox` into `ancestorPath` — it
was discarding the fragment and keeping only the live box. Changed `StackingParticipant.ClipAncestors` and
`ancestorPath` from `IReadOnlyList<CssBox>`/`List<CssBox>` to `IReadOnlyList<BoxFragment>`/`List<BoxFragment>`,
storing `childFragment` instead. `RenderUtils.TryPushOverflowClip`/`PushAncestorOverflowClips` now read the
clip rectangle from `ancestor.Rect` (already fragmentainer-local, since both the participant's own fragment
and its ancestor fragments come from the same page's fragment tree) instead of `overflowBox.Bounds`, which
also removes the need for the `originY` translation parameter entirely — there was nothing to map, the two
were already in the same coordinate space once both come from fragments. Style-only facts (`Overflow.Value`,
border widths, `IsRounded`, `ComputeInnerRadii`) still read off `ancestor.Box` live, mirroring how
`FragmentEmitter.OverflowClipOf` already separates position facts (fragment) from style facts (box).

## What was found by running it, not by reading it

Building a natural end-to-end repro (a stacking-hoisted participant — `position:relative;z-index` or
`opacity<1` — inside a repeated table header, clipped by an `overflow:hidden` ancestor) turned up a
separate, pre-existing, more severe bug that made the scenario unreachable: `HtmlContainerInt.ComputeFlowFlags`
computes the document-wide `HasStackingHoistCandidates` flag by walking `box.Boxes` from `Root`, but
`CssProxyBox` (the per-page stand-in for a repeated `<thead>`/`<tfoot>`) doesn't expose its `SourceBox`'s
children through `.Boxes` — so a stacking context that exists *only* inside a repeated header/footer's
detached subtree is invisible to that scan, `HasStackingHoistCandidates` comes out false, and
`StackingOrder.Flatten` (which gates its entire hoisting search on that one boolean) never finds or paints
the box at all, anywhere in the document. This is strictly worse than a mis-resolved clip and is *why* #345
itself was "exotic enough that no fixture in the suite reaches it" — the fix here is correct and verified
independently of that bug (see Evidence below), but the natural integration-level repro is blocked on it.
Flagged as a separate follow-up rather than folded into this change, since it's a different mechanism
(`ComputeFlowFlags`/candidate detection, not clip resolution) and unrelated to what this fix touches.

## What was deliberately not done

No new geometry/build-time snapshot machinery was added — unlike `OverflowClipOf` (which resolves an
arbitrary-distance containing-block-chain ancestor at *build* time via a `BoxGeometrySnapshot`, because that
ancestor isn't necessarily reachable as an already-materialized fragment when the builder runs),
`SearchForHoistableDescendants` already walks a fully-materialized per-page fragment tree at *paint* time,
so the exact ancestor fragment for this page's own pass is already in hand — no snapshot, no build-time
bookkeeping, just stop discarding what was already there.

## Evidence

- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`: 9900 passed, 0 failed, 9 skipped
  (pre-existing platform-gated skips, unrelated).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors.
- Diff coverage (`diff-cover` against `main`): 100% on the three changed production files.
- New tests in `src/PeachPDF.Tests/Html/Core/Utils/PushAncestorOverflowClipsTests.cs`:
  - `PushesClip_FromAncestorsOwnFragmentRect_NotFromItsLiveBoxBounds` — proves `TryPushOverflowClip` reads
    the ancestor's fragment rect, not its live bounds, by handing it a fragment whose `Rect` deliberately
    disagrees with the live box's current position.
  - `Flatten_SameAncestorBoxShownAtDifferentFragmentRects_CapturesEachTreesOwnInstance` — proves
    `StackingOrder.Flatten`/`SearchForHoistableDescendants` itself (not just `RenderUtils` in isolation)
    captures each fragment tree's own ancestor instance when the same live `CssBox` stands in for two
    different fragment-tree positions, the actual repeated-header shape.
  - `PushesNoClip_WhenAncestorIsNotOverflowHidden` — the early-return path still holds under the new
    `BoxFragment` parameter type.
  - All three fail to compile against the pre-fix signature (`PushAncestorOverflowClips(RGraphics,
    IReadOnlyList<CssBox>, double)`), confirming the API change is load-bearing for what they test.

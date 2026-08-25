# Flex cross-axis `align-items: stretch` now honors `box-sizing: border-box` (#832, #837)

Closes the gaps `.claude/accepted-gaps/flex-cross-axis-stretch-ignores-box-sizing-border-box.md` and
`.claude/accepted-gaps/flex-measure-item-hypothetical-size-ignores-box-sizing-border-box.md`, both
carved out of issue #815's fix (#815 itself the flex analog of #811's Grid fix) - the identical bug,
recurring in two more `CssLayoutEngineFlex` call sites (`ComputeCrossOffsets`'s stretch branch,
`MeasureItem`'s hypothetical-size construction) neither #815 nor #811 named.

## Load-bearing idea

`ComputeCrossOffsets`'s `AlignItem.Stretch`/`AlignItem.Normal` branch (both the row-direction
height-stretch arm and the column-direction width-stretch arm) re-lays a stretched item out at its
line's cross size by temporarily setting `box.Width`/`Height` to a computed value, then restoring the
original string afterward - what persists is the box's resulting layout geometry from that call, not the
string. Both arms computed the cross-axis target by subtracting the item's raw padding+border from
`targetCross` - correct only for `content-box`, where `CssBox.ActualBoxSizeIncludedWidth`/
`ActualBoxSizeIncludedHeight` equals that same padding+border. For `border-box` it's 0, so `box.Width`/
`Height` needs to hold the *outer* target directly. Fixed with a new `CrossBoxSizeIncluded` helper -
the cross-axis counterpart of the pre-existing `MainBoxSizeIncluded` (added by #815), returning
whichever of `ActualBoxSizeIncludedWidth`/`Height` sits on the axis opposite `_mainAxisIsPhysicalX`'s
main-axis choice.

**A second instance of the identical bug, one line below each fixed one, in the same branch**: each arm
also locks the item's *main*-axis size for the same re-layout call (`item.Box.Width`/`Height =
FinalMainSize - MainPaddingBorder(item.Box)`, "so GetBoxWidth/Height can't fall back to container
fill/shrink-to-content"). `FinalMainSize` is the flex algorithm's own already-final, box-sizing-aware
outer main size (from `ResizeItem`, itself fixed by #815) - so this lock needed `MainBoxSizeIncluded`,
not raw `MainPaddingBorder`, for exactly the same reason. This one was not named by #832's own
description (which only calls out the cross-axis `crossContent` computation) but is the same defect
class in code the fix was already touching, so it's fixed in the same change rather than filed
separately.

**A third, separate instance in `MeasureItem` (#837)**, folded into this same change after being filed
as a follow-up during this fix's own review: the explicit-`flex-basis`/`width`/`height` branch built
`hypothetical` (the item's outer main size - both `FlexItem.HypotheticalMainSize`, read by
`CollectLines`' wrap-line-breaking decision, and the basis for the temporary "layout at hypothetical
size" measurement a few lines later) by unconditionally adding the item's raw padding+border to the
parsed CSS length. Per the flex-basis spec
(https://www.w3.org/TR/css-flexbox-1/#flex-basis-property), a `<length>` flex-basis resolves the same
way `width`/`height` would, so it respects `box-sizing` exactly like #815/#832 already made width/height
do elsewhere in this file - for `border-box`, the parsed value already *is* the outer size, so adding
raw padding+border on top double-counts it, inflating `hypothetical` and, with it, the item's perceived
size for wrap-line-breaking purposes. Fixed by using `MainBoxSizeIncluded(box)` in place of
`MainPaddingBorder(box)` at both the three `hypothetical`-construction sites and the later
`cssContentSize` back-conversion that consumes it (the same helper `ComputeCrossOffsets`'s own fix
introduced), matching the identical pattern.

## What running it (not just reading it) confirmed

- A code-review pass (run against this diff before it landed) caught the main-axis-lock instance by
  temporarily asserting the item's main-axis size in the new cross-axis test and running it - the
  cross-axis fix alone left `Width` (row test) / `Height` (column test) still wrong by exactly the
  padding+border amount (44pt), the same shape as the cross-axis bug this change set out to fix. Toggling
  each main-axis-lock line between `MainPaddingBorder`/`MainBoxSizeIncluded` independently reproduced
  56pt (broken) vs 100pt (fixed) on both axes before committing to the fix.
- The row-direction cross-axis test failed at exactly the predicted 44pt shrinkage (100 expected vs 56
  actual) against the pre-fix source.
- The column-direction arm needed a specifically-constructed repro: an ordinary block item's `width:auto`
  already fills its containing block via plain block auto-width layout *before* this method ever runs
  (block auto-width resolution is itself box-sizing-invariant), so `currentCross` and `targetCross`
  already agree and the stretch branch's re-layout is skipped as a no-op regardless of the bug - this
  made an early wrap-based repro attempt pass even against unfixed source. A replaced element (`<img>`)
  breaks that: `width: auto` on a replaced element resolves to its own small intrinsic size instead of
  filling, so the item genuinely needs a stretch, which is what actually exercises the buggy branch.
- `MeasureItem`'s own `hypothetical` construction is read by two different consumers with two different
  outcomes worth separating: the *temporary* "layout at hypothetical size" re-layout (used only to
  measure the item's cross-axis dimension) turns out to self-cancel for the explicit-value branches,
  since both the addition (constructing `hypothetical`) and the later subtraction (`cssContentSize`)
  moved by the identical raw-padding-border amount pre-fix - so that specific temporary measurement was
  never actually wrong. The *returned* `FlexItem.HypotheticalMainSize` is a different story: it is
  never put through that second subtraction, so its own inflation by the addition alone survives
  untouched into `CollectLines`' wrap decision - a real, externally observable bug despite the adjacent
  temporary-measurement path being a false lead. Confirmed by instrumenting `MeasureItem` directly:
  the "auto width/basis" content-measured branch returns *before* reaching the shared
  `hypothetical`/`cssContentSize` code at all (it has its own early `return` a few lines up), so it was
  never reachable through that path in the first place - the opposite of what the original code audit
  guessed, and only found by tracing actual execution rather than re-reading the branch structure.
- A first attempt at a #837 regression test (comparing a border-box item's own measured cross-axis
  height against a content-box item's, expecting them to differ pre-fix) passed unexpectedly even
  against pre-fix source, for two compounding reasons uncovered by instrumenting the method directly:
  (1) the content-measured branch's early return meant the test's `width:auto` items never went through
  the buggy code at all, and (2) even after switching to explicit-value items, `align-items: stretch`'s
  own (now-already-fixed) re-layout in `ComputeCrossOffsets` re-derives the cross-axis size from the
  correct final width regardless of what `MeasureItem` measured, masking the very bug being tested.
  The eventual working repro instead targets `CollectLines`' wrap decision directly - two border-box
  items whose combined *correct* outer width exactly fits a wrap container, but whose pre-fix *inflated*
  hypothetical does not, so the fix is what keeps them on one line instead of wrapping.

## Deliberately not done

- Did not touch the "min-width constrains content width" computation a few lines below the fixed
  `hypothetical` construction (`minOuter = ParseLength(box.MinWidth, ...) + padding + border`), which
  has the same unconditional-raw-padding-border shape - not confirmed as a live bug, and out of the
  scope #837 actually named (the `hypothetical`/`cssContentSize` round trip specifically).

## Evidence

- New regression tests (`FlexboxIntegrationTests.cs`): `StretchedBorderBoxItem_WithPadding_FillsLineCrossSize`
  (row direction, asserts both the fixed cross-axis Height and the fixed main-axis Width),
  `StretchedBorderBoxColumnItem_WithPadding_FillsLineCrossSize` (column direction, `<img>`-based repro,
  asserts both the fixed cross-axis Width and the fixed main-axis Height), and
  `BorderBoxExplicitFlexBasis_DoesNotWrapPrematurely` (#837 - two `flex:0 0 100pt` border-box items whose
  combined 200pt exactly fits a 200pt wrap container must stay on one line). All three confirmed to fail
  against pre-fix source (the first two by exactly 44pt on every assertion; the third by the second item
  wrapping to its own line) and pass post-fix.
- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` - full suite green (9243 passed,
  9 pre-existing platform-gated skips).
- `dotnet build PeachPDF.slnx -t:Rebuild` - 0 warnings.

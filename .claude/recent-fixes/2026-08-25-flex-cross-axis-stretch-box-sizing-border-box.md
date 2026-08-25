# Flex cross-axis `align-items: stretch` now honors `box-sizing: border-box` (#832)

Closes the gap `.claude/accepted-gaps/flex-cross-axis-stretch-ignores-box-sizing-border-box.md` carved
out of issue #815's fix (#815 itself the flex analog of #811's Grid fix) - the identical bug, in
`CssLayoutEngineFlex.ComputeCrossOffsets`'s stretch branch rather than `ResizeItem`'s.

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
separately - unlike the `MeasureItem` instance below, which is genuinely separate code.

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
- Found, but deliberately left unfixed (separate, less certain in scope): `MeasureItem`'s own "layout at
  hypothetical size" step has a structurally similar unconditional-raw-padding-border subtraction, but
  tracing it showed the explicit-`flex-basis`/width/height branch likely self-cancels (an equal and
  opposite box-sizing-blind addition happens a few lines earlier when constructing `hypothetical`), while
  the content-measured `naturalMain`/`maxContent` branches do not have that compensating addition and
  look like a genuine instance of the same bug. Distinguishing these with confidence needs its own
  investigation and its own regression test, so it's filed as
  [#837](https://github.com/jhaygood86/PeachPDF/issues/837) and recorded in
  `.claude/accepted-gaps/flex-measure-item-hypothetical-size-ignores-box-sizing-border-box.md` rather than
  folded into this fix.

## Deliberately not done

- Did not touch `MeasureItem` (see above) - a distinct method, uncertain-enough impact that folding it in
  would have diluted this fix's own confirmed, reproduced coverage.

## Evidence

- New regression tests (`FlexboxIntegrationTests.cs`): `StretchedBorderBoxItem_WithPadding_FillsLineCrossSize`
  (row direction, asserts both the fixed cross-axis Height and the fixed main-axis Width),
  `StretchedBorderBoxColumnItem_WithPadding_FillsLineCrossSize` (column direction, `<img>`-based repro,
  asserts both the fixed cross-axis Width and the fixed main-axis Height) - both confirmed to fail against
  pre-fix source (56pt/256pt, each exactly 44pt short) on every assertion, independently, then pass
  post-fix.
- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` - full suite green (9242 passed,
  9 pre-existing platform-gated skips).
- `dotnet build PeachPDF.slnx -t:Rebuild` - 0 warnings.

# Flex item stretch/shrink-to-fit now honors `box-sizing: border-box` (#815)

Closes the gap `.claude/accepted-gaps/flex-stretch-shrink-to-fit-sizing-ignores-box-sizing-border-box.md`
carved out of issue #811's Grid fix (#816) - the identical bug, in flex's own placement code rather than
Grid's.

## Load-bearing idea

`CssLayoutEngineFlex.ResizeItem` (main-axis growth/shrink, both `row` and `column`) and
`ShrinkColumnItemToContentWidth` (cross-axis shrink-to-fit under `flex-direction: column` with a
non-`stretch` `align-items`/`align-self`) each compute an item's target *outer* size, then had to turn
that into whatever string `box.Width`/`box.Height` needs to hold so the subsequent `PerformLayoutBlockified`
call re-derives the same outer size. Both did that by unconditionally subtracting the item's own raw
padding+border - correct only for `content-box`, where `CssBox.ActualBoxSizeIncludedWidth`/
`ActualBoxSizeIncludedHeight` (this engine's box-sizing contract, `CssBox.StyleProperties.cs`) equals that
same padding+border. For `border-box`, `ActualBoxSizeIncludedWidth`/`Height` is 0 - `box.Width`/`Height`
needs to hold the *outer* size directly - so subtracting the raw padding+border left every affected
border-box item exactly that padding+border too small. Fixed by subtracting
`ActualBoxSizeIncludedWidth`/`Height` (via a new per-axis `MainBoxSizeIncluded` helper mirroring the
existing `MainPaddingBorder`) instead of raw padding+border, at the three assignment sites this touches:
`ResizeItem`'s single `Width`/`Height` write, and `ShrinkColumnItemToContentWidth`'s `Width` (cross axis,
always physical X in that method) and `Height` (main axis, via the new helper) writes. Left the two
methods' own min/max-width clamps untouched (they add raw padding+border to an already-content-space
`CssValueParser.ParseLength` result to get an outer clamp bound) - mirroring the Grid fix's precedent of
leaving that adjacent, structurally different concern alone.

## What running it (not just reading it) confirmed

- Reasoning through the exact numbers in the new tests before running them predicted a 44pt shrinkage
  (20pt padding × 2 + 2pt border × 2) on every affected assertion; running the new tests against the
  pre-fix source reproduced that exactly - `150` expected vs `106` actual on the two main-axis tests
  (`GrowingBorderBoxItem_WithPadding_FillsAllottedMainAxisSpace`,
  `GrowingItem_BorderBoxAndContentBox_SameOuterWidth`), confirming the fix's target line, not just its
  compiling.
- A plain `flex-grow:1` item with no explicit `width`/`flex-basis` hits a separate, already-correct code
  path (`hypothetical = 0` when `FlexGrow > 0`, in the item's own hypothetical-size measurement) that
  makes the final outer target independent of box-sizing *before* `ResizeItem` even runs - which is what
  makes `GrowingItem_BorderBoxAndContentBox_SameOuterWidth`'s content-box/border-box equality assertion a
  clean, font-metric-independent test rather than one that has to duplicate `GetFitContentWidth`'s own
  text-measurement logic to predict an expected value.
- Found, but deliberately left unfixed (separate from issue #815's named scope, and not something either
  the accepted-gap file or #815 itself named): `ComputeCrossOffsets`'s own cross-axis *stretch* re-layout
  (the `AlignItem.Stretch`/`Normal` branch, both physical-X and physical-Y arms) has the identical
  content-space-only assumption on its own `Height`/`Width` assignment - a stretched (not shrink-to-fit)
  border-box item with a definite cross size on the container would still under-size. Filed as
  [#832](https://github.com/jhaygood86/PeachPDF/issues/832) and recorded in
  `.claude/accepted-gaps/flex-cross-axis-stretch-ignores-box-sizing-border-box.md`, matching this repo's
  convention of keeping a fix scoped to what its issue actually names while still tracking a confirmed
  spec deviation.

## Deliberately not done

- Did not touch `ItemContentCommit.CommitLayout` (the shared final-layout-pass box-sizing fix) - #811's
  Grid fix already covers it for both engines, and the task explicitly ruled it back out of scope here.
- Did not touch the flex-basis-to-hypothetical-size computation (`CssLayoutEngineFlex`'s measurement code
  around the `flexBasis.IsValue` branch), which has the same raw-padding-border pattern for a
  `box-sizing: border-box` item with an explicit `flex-basis` - not named by issue #815, and the new tests
  were deliberately built to avoid depending on it (using auto-width `flex-grow` instead of `flex-basis`)
  so this fix's own coverage doesn't accidentally rely on that adjacent code being correct.

## Evidence

- New regression suite (`FlexboxIntegrationTests.cs`): `GrowingBorderBoxItem_WithPadding_FillsAllottedMainAxisSpace`
  (main-axis grow, absolute assertion), `ShrinkToFitColumnItem_BorderBox_SameOuterWidthAsContentBox`
  (column-direction shrink-to-fit, content-box/border-box footprint equality),
  `GrowingItem_BorderBoxAndContentBox_SameOuterWidth` (main-axis grow, footprint equality) - all 3
  confirmed to fail against the pre-fix source by exactly the predicted 44pt, then confirmed to pass
  against the fix.
- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` - full suite green (9226 passed, 9
  pre-existing platform-gated skips).
- `dotnet test --collect:"XPlat Code Coverage" --settings PeachPDF.Tests/coverlet.runsettings --results-directory coverage`
  + `diff-cover` against `main` - 100% diff coverage (4/4 changed lines).
- `dotnet build PeachPDF.slnx -t:Rebuild` - 0 warnings.

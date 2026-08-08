# `@page` margin and `NoEms` em/rem basis: PixelsPerPoint correction was inverted

Closes [#631](https://github.com/jhaygood86/PeachPDF/issues/631).

## The bug

`CssBox.GetEmHeight()`/`GetRemHeight()` return a value in the PDF adapter's device-scaled
font-measurement space, not true CSS points: `PdfSharpAdapter.CreateFontInt` divides a requested
font size by `PixelsPerPoint` once to reach that space. Two call sites treated the result as if it
were already true points and then divided by `PixelsPerPoint` *again*, compounding into a value wrong
by `PixelsPerPoint²`:

1. `DomParser.ApplyPageStylesOnce`'s `@page` margin em/rem basis (`emPt`/`PageLengthContext`'s rem
   argument) - used whenever a base `@page` margin is declared in `em`/`rem` and no `@page { font-size
   }` overrides the em basis (the rem basis is unaffected by that override and always hits this path).
2. `CssBox.NoEms` - the eager em-to-pt conversion generated code runs on `text-indent`/`word-spacing`/
   `letter-spacing`'s raw authored text before cascade.

Both are invisible at the library's default `PixelsPerInch` of 72 (`PixelsPerPoint == 1`, where
multiplying and dividing are indistinguishable), and most of the existing test suite's own harnesses
pin `PixelsPerPoint` to 1.0 explicitly - which is exactly why this shipped unnoticed. A non-default
`PixelsPerInch`, or the `ShrinkToFit`/`ScaleToPageSize` features (which legitimately move
`PixelsPerPoint` away from 1.0 for ordinary content), both trigger it.

## The fix

Multiply by `pixelsPerPoint` instead of dividing, undoing `CreateFontInt`'s division - the identical
correction already established for the same "device-scaled font space → true CSS points" conversion in
`DerivedStyle.ActualFont`'s `parentSize`/`remSize` inputs and `CssBox.ResolveFontSizeValueComputation`
(the font-size PR, [#632](https://github.com/jhaygood86/PeachPDF/pull/632), that originally surfaced
this issue). `DomParser.ApplyPageStylesOnce`'s `htmlContainer.PageSize.Width / pixelsPerPoint` line was
*not* touched - `PageSize` is pre-multiplied by `PixelsPerPoint` under an unrelated, page-geometry-specific
convention (see `HtmlContainerInt.MarginTop`'s own doc comment), so it scales in the opposite direction
and dividing there was already correct.

An existing test, `PageMarginUnitConsistencyIntegrationTests.FirstPageEmMarginOverride_ScalesByPixelsPerPoint_ExactlyOnce`,
computed its own expected value with the same wrong division direction (`container.Root!.GetEmHeight() /
ppp`), so it was itself asserting the bug as correct for the per-page-rule code path
(`PageRuleResolver.ResolvePageMargins`, which consumes the same `PageLengthContext.EmPt` `ApplyPageStylesOnce`
now computes correctly). Corrected to multiply, matching the sibling test
`FirstPageEmMarginOverride_DrivesBandGeometry`'s already-correct expected value (`2 * 11pt = 22pt`), which
is indistinguishable from the old formula only at the default `PixelsPerPoint` of 1.

## Evidence

New file `PixelsPerPointEmResolutionIntegrationTests.cs`: two tests for the `@page` margin em/rem basis
(one isolating the em branch via the UA-default root font-size with no `@page { font-size }` override,
one isolating the rem branch by setting a base-rule `font-size` that decouples it from the em basis) and
three for `NoEms` (`text-indent`, `letter-spacing`, `word-spacing`), all at a non-default `PixelsPerInch`/
`PixelsPerPoint`. All 5 verified to fail on pre-fix code (via `git stash` of the two source files alone)
with exactly the `PixelsPerPoint`-squared-off values the issue predicts, and pass post-fix. Full suite
(`dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`): 8192 passed, 0 failed (one
`ContainerQueryLayoutIntegrationTests` failure seen on one run is the already-documented pre-existing
cross-test flake, unrelated to this change - reproduces on a clean `main` checkout). `diff-cover` against
`main`: 100% diff coverage. `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.

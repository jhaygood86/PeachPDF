# Four per-page-geometry issues (#144, #145, #200, #201) were already fixed, just never closed

While scoping a batch of 10 open issues in the per-page horizontal reflow follow-up trail
(#142/#143's aftermath), found that #144, #145, #200, and #201 had all already been fixed by the
earlier #197-#199 generalization work, but the GitHub issues were never closed. No production code
changed here - this entry records the verification and the one new regression test that was
genuinely missing.

## What was verified, and where

- **#145** (ICB height should track `:first`'s own overridden band, not the base band):
  `CssLayoutEngine.GetBoxHeight` already floors ICB height via
  `box.HtmlContainer.PageGeometry.GetPage(0).BandHeight` when `box == box.ContainingBlock`. Already
  tested by `PercentageHeightPerPageResolutionIntegrationTests.RootIcbPercentageHeight_TracksFirstPagesOwnMarginOverride_NotTheBaseConfiguredSize`.
- **#201** (ICB width should track `:first`'s own overridden band): `HtmlContainerInt.IcbWidthSeed`
  already reads `PageGeometry.GetPage(0).BandWidth`. Already tested by
  `PerPageHorizontalReflowLayoutIntegrationTests.FixedPositionBox_PercentageWidth_ResolvesAgainstItsOwnPagesBand`,
  `AbsolutelyPositionedBox_PercentageWidth_ResolvesAgainstEachPagesOwnMeasure`, and
  `AbsolutelyPositionedBox_LeftAndRightInsets_FillsTheFirstPagesOwnIcbWidth` (the height half of the
  same underlying fix is covered by #145's test above - the two issues are, in effect, the width and
  height halves of one fix).
- **#200** (a plain wrapper `<div>` nested below the main column should reflow per-page):
  `CssLayoutEngine.IsOrdinaryUnconstrainedBlock`/`IsUnconstrainedMainColumn` already generalized past
  a root/`<html>`/`<body>` tag-name check to any plain, auto-width, `max-width`-free block. Already
  tested by `StraddlingBlockInlineExtentLayoutIntegrationTests.NestedDivInsideAnotherDiv_NowReflowsPerPage_MatchingIssue200sFix`.
- **#144** (`@page` `size` on a selector-carrying rule other than a named page - e.g. `:first` alone -
  should reach the generated PDF page's own dimensions): `PageGeometryTable.Compute` and
  `PdfGenerator.AddPdfPages` already resolve `size` per slot for any rule kind, and this was already
  proven at the internal layout-geometry level by
  `StraddlingBlockInlineExtentLayoutIntegrationTests.StraddlingDiv_MixedPageSizeDocument_ResizesToEachPagesOwnMeasure`.
  What was genuinely missing: a test proving this reaches the actual generated `PdfPage.Width/Height`
  (not just internal fragment geometry) for a `:first`-only override with **no named page involved** -
  the existing `PdfGeneratorMixedPageSizeTests` only covered a named-page `size` override. Added
  `FirstPageSizeOverride_WithNoNamedPage_ProducesADifferentlySizedFirstPdfPage`.

## What running it (not just reading it) confirmed

- A first draft of the new #144 test relied on a 900pt-tall paragraph naturally overflowing a 500pt
  first page to force a second, differently-sized page - this produced only 1 page under the real
  `PdfGenerator.GeneratePdf` pipeline, unlike an equivalent-looking fixture in the lighter
  `HtmlContainerInt`-harness layout tests, which does split as expected. Rather than chase the
  pipeline-vs-harness discrepancy, switched to the same deterministic `page-break-before: always`
  pattern every other full-pipeline `PdfGenerator` test in this suite already uses (see
  `FixedPositionPaginationIntegrationTests`) - forced breaks are this repo's established convention
  for full end-to-end `PdfGenerator` tests precisely because natural-overflow pagination timing isn't
  the thing being tested there and shouldn't be a source of flakiness.

## Evidence

- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~PdfGeneratorMixedPageSizeTests|FullyQualifiedName~PerPageHorizontalReflowLayoutIntegrationTests|FullyQualifiedName~StraddlingBlockInlineExtentLayoutIntegrationTests|FullyQualifiedName~PercentageHeightPerPageResolutionIntegrationTests"` - 42 passed, 0 failed.

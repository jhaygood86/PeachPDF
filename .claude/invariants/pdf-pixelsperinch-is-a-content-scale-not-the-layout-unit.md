# PdfGenerateConfig.PixelsPerInch is a content scale; the layout unit stays 1 point

`RenderContext.PixelsPerPoint` (`LayoutUnitsPerPoint`) is the engine's layout unit and is deliberately physical-invariant: absolute lengths, fonts, page
geometry and the canvas all multiply and divide by it, so changing it changes nothing a reader sees. A knob that was meant to *shrink* content therefore did
nothing when it was wired to it - `PixelsPerInch`, `ShrinkToFit`, `ScaleToPageSize` and `MinContentWidth` all produced byte-identical output for months
(six identical PDFs for the six settings on the issue's document).

What shrinks content is `HtmlDocumentOptions.PageScale`: the sheet, margins and `@page` size/margin/border/padding are multiplied, the content is laid out
on that larger sheet in ordinary units, and the host draws the page at `1 / PageScale` (`PdfComposerPage.SetContentScale`: a world transform, plus the same
scale in every link, destination, widget, annotation and bookmark rectangle the writer places). `PdfGenerator` always sets `PixelsPerPoint = 1` and derives
the scale from `PixelsPerInch / 96`; do not wire a new "scale" setting to the layout unit again.

Traps found while doing it:
- **`@page` margins and size are scaled where they are parsed** (`DomParser.ApplyPageStylesOnce`, `PageRuleResolver.ResolvePage*`), not by comparing the
  container's margins with the configured ones afterwards: a configured 10pt margin at scale 3 is 30, which equals an `@page` margin of 30pt, so the
  comparison skipped the scaling (`PageScaleTests.APageRulesSizeAndMarginsAreMultipliedToo` pins it).
- **A test that reads a content stream at a non-default scale reads layout units**, before the page's leading `cm`; multiply by it (or the test passes for
  the wrong reason). Engine tests that need a non-default *layout unit* pass `pixelsPerPoint:` to `PdfGeneratorLayoutHarness.LayoutAsync`, not
  `PdfGenerateConfig.PixelsPerInch`.
- **`PdfCanvas.TransformScale` includes the page's content scale**, or a rasterized effect's bitmap would have `RasterizationDpi / scale` pixels per inch of
  paper instead of `RasterizationDpi`.
- **`PdfGenerator.LoadAtScaleAsync` is the one place the fit decision lives**; `PdfGeneratorLayoutHarness.LayoutWithRescaleAsync` calls it rather than
  mirroring it.

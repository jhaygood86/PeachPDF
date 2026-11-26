# Content scale (PixelsPerInch, ShrinkToFit, ScaleToPageSize, MinContentWidth) and the ClipReport of words past the page edge

Load-bearing idea: shrinking is "lay out on a bigger sheet, draw at 1 / scale" - see the invariant
[pdf-pixelsperinch-is-a-content-scale-not-the-layout-unit](../invariants/pdf-pixelsperinch-is-a-content-scale-not-the-layout-unit.md). The engine part is
`HtmlDocumentOptions.PageScale` (sheet, margins and `@page` geometry multiplied, per-page rules too, in `PageGeometryTable`); the writer part is
`PdfComposerPage.SetContentScale`; `PdfGenerator.LoadAtScaleAsync` decides the scale (`PixelsPerInch / 96`, then the fit options measured against the content
area at scale 1, with a 1e-6 hair so content that fills the width exactly does not wrap).

Found by running it: reproducing the issue's document with the six settings gave six identical PDFs (the knobs changed only the layout unit, which is
physical-invariant). After the fix `/F0 12 Tf` under `0.6667 0 0 0.6667 0 0 cm` is 8pt at 144, and the 1200px table fits. The page's margins came out as
`margin / scale` until `PageGeometryTable` scaled the rules' margins (the rule's `margin: 20mm` was unscaled while the container's was scaled); both renderers
(PDFium, MuPDF) show the page with its 20mm margin, the links over their text, the widget in its place and a bookmark whose `/FitH` top is the heading's.

ClipReport: `PaintWordSequence` culled a word whose clip intersection was empty before `RecordIfClipped` ran, and `PaintFragment` skipped a whole fragment
outside the clip, so words past the edge were never reported. `RecordIfPastPageEnd` now reports a word wholly past the end of the **page's** content area
(`PageClipOverride ?? PageBoxRect`, not the current clip: a word hidden by the author's `overflow: hidden` stays unreported - the existing
`OneWordDeliberately_BecauseAMultiWordRunIsADifferentFinding` pins that), on a line the page reaches (a vertical cull is a page break's, #113), not before the
start of the page (`left: -9999px` is a hiding technique), and not in a vertical writing mode.

Not done: bleed and printer's marks under a scale ([accepted gap](../accepted-gaps/page-scale-leaves-bleed-and-marks-unscaled.md)); a `--pixels-per-inch` /
`--shrink-to-fit` option on the CLI.

Evidence: PeachBrackets.Engine.Tests (20,520), PeachPDF.Tests, PeachBrackets.Tests, PeachDrawing.PDF.Tests and PeachBrackets.Scripting.Tests all green on net10.0.

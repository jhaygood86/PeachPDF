# PDF/UA-1: box backgrounds and shadows are artifacts (#2353)

Backgrounds were drawn outside any marked-content sequence, so veraPDF failed ISO 14289-1 7.1-3 once per painted box.
`FragmentPainter` now opens an artifact around a box's background (and track backgrounds, box shadows) and
`PagePainter` around the canvas/dark-canvas fill, via `OpenDecorationArtifact` (skips when tagging is suppressed or
already inside marked content, which cannot nest).

**Trap:** open the artifact only when something will be painted (`HasBackground`, `HasTrackBackgroundsBeneathCell`,
`HasBoxShadow`). Wrapping unconditionally emits an empty BDC/EMC per box and broke the `TaggedPdfPaintOrderTests` call-order
assertions. Evidence: `PdfUARequirementsTests.PaintedBackgroundsAndShadows_AreArtifacts_NotUntaggedContent`; PeachPDF.Tests and Engine.Tests pass.

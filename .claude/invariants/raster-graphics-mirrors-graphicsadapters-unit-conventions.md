# `RasterCanvas` must apply `GraphicsAdapter`'s exact unit conventions, method by method, when driven by PeachPDF

`RasterCanvas` (`PeachDrawing`, moved from PeachPDF's own `RasterGraphics` - see
[[peachdrawing-public-api-must-not-mirror-a-competitor]]) is not PeachPDF-only any more: its own native
contract, set by the `pixelsPerPoint` value passed to its constructor, is "one user-space unit is
`pixelsPerPoint` device pixels." A standalone caller (`RasterRenderContext.CreateCanvas`) always constructs
it with `pixelsPerPoint = 1`, so for that caller user-space units *are* device pixels directly - there is no
PDF point, no `PixelsPerPoint` concept, nothing CSS-layout-specific about it.

**When PeachPDF drives a `RasterCanvas` as its raster-fallback surface** (`GraphicsAdapter.BeginRasterSurface`,
via `PeachDrawing`'s own internal `RasterSurfaceFactory.Create`), it is constructed with PeachPDF's *real*
`PixelsPerPoint` instead of `1`, and the same paint code (`FragmentPainter` and everything it calls) drives
both `GraphicsAdapter` and this `RasterCanvas` interchangeably through the shared `Canvas` base. Any
difference in how the two backends interpret a coordinate at that point shows up as content in the wrong
place - and only on the raster path, which is easy to miss because it is the rarer one. The conventions,
all established by `GraphicsAdapter` and mirrored by `RasterCanvas` for exactly this reason:

- **Layout units, divided by `PixelsPerPoint` to reach user space (points):** `DrawRectangle`, `DrawLine`, `DrawPolygon`,
  `DrawImage`, `DrawString` (and its `letterSpacing`), `PushClip(Rect)`, and the translation part of `PushTransform`.
- **Already user space, used as they arrive:** every coordinate of a `GraphicsPath` (callers such as
  `RenderUtils.GetRoundRect` divide it themselves), pen widths, dash lengths, gradient brush geometry (the adapter divides it
  when the brush is created), and the *linear* part of a `PushTransform` matrix.
- **Baseline:** `DrawString` receives the run's top-left; the baseline is
  `point.Y / PixelsPerPoint + lineSpace * Typeface.Metrics.CellAscent / Typeface.Metrics.LineSpacing`, not
  `Font.Ascent` (which is rounded - see the comment in `GraphicsAdapter.GetInkCrossings`).

`RasterCanvasTests.PushTransform_DividesOnlyTheTranslationByPixelsPerPoint` and
`LayoutUnits_AreDividedByPixelsPerPoint_ThenScaledToPixels` (both still in `PeachPDF.Tests/Raster/`, not
`PeachDrawing.Tests` - they construct `RasterCanvas` through `GraphicsAdapter`'s own PDF-driven
`PixelsPerPoint`, so they belong with the PeachPDF-integration tests, not the portable-algorithm ones) pin
the first two groups. A method added to `Canvas` needs an answer for which group each of its coordinates
belongs to before it is implemented in `RasterCanvas` - and, since `Canvas`/the convention now spans two
packages, that answer has to hold for *both* `pixelsPerPoint = 1` (standalone) and PeachPDF's real value
(nested fallback surface), not just the PDF-driven case this file originally described.

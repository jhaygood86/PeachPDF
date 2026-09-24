# `RasterGraphics` must apply `GraphicsAdapter`'s exact unit conventions, method by method

The same paint code drives both backends, so any difference in how they interpret a coordinate shows up as content in the
wrong place — and only in the raster path, which is easy to miss because it is the rarer one. The conventions, all
established by `GraphicsAdapter`:

- **Layout units, divided by `PixelsPerPoint` to reach user space (points):** `DrawRectangle`, `DrawLine`, `DrawPolygon`,
  `DrawImage`, `DrawString` (and its `letterSpacing`), `PushClip(RRect)`, and the translation part of `PushTransform`.
- **Already user space, used as they arrive:** every coordinate of an `RGraphicsPath` (callers such as
  `RenderUtils.GetRoundRect` divide it themselves), pen widths, dash lengths, gradient brush geometry (the adapter divides it
  when the brush is created), and the *linear* part of a `PushTransform` matrix.
- **Baseline:** `DrawString` receives the run's top-left; the baseline is `y + font.GetHeight() * CellAscent / CellSpace`,
  not `RFont.Ascent` (which is rounded — see the comment in `GraphicsAdapter.GetInkCrossings`).

`RasterGraphicsTests.PushTransform_DividesOnlyTheTranslationByPixelsPerPoint` and
`LayoutUnits_AreDividedByPixelsPerPoint_ThenScaledToPixels` pin the first two groups. A method added to `RGraphics` needs an
answer for which group each of its coordinates belongs to before it is implemented here.

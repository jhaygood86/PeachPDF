# A brush drawn on a raster surface is read in the canvas's layout units

**Symptom:** with `ShrinkToFit` (or any page scaled so `PixelsPerPoint` is not 1), every element rendered through the raster
backend (`filter: blur()`, colour filters, blurred shadows, `backdrop-filter`) showed its `linear-gradient`/`radial-gradient`
background shifted: the `css_filter_raster` showcase's filtered swatches lost the blue corner of a 135deg gradient, while the
unfiltered swatch beside them was correct. About 17% fewer blue pixels on the page.

**Cause:** the #1509 extraction made a `Brush` plain data in the canvas's own (layout) units. `GraphicsAdapter.ToXBrush`
divides by `PixelsPerPoint` when it builds the PDF brush, but `RasterCanvas.CreatePaint` handed the brush to
`PaintSource.From` with a device-to-user (points) matrix, so a gradient was positioned as if a layout unit were a point. Found by
bisecting the showcase with the harness's `ShrinkToFit = true` config; the CLI (no shrink) renders it correctly, which is why a
CLI-only bisect said every commit was good.

**Fix:** `CreatePaint` scales the device-to-user matrix by `_pixelsPerPoint`, so the brush is read in the units it is expressed in.
`ScaledCanvasBrushTests` (PeachDrawing.Tests) draw linear and radial gradients on a half-point-per-unit canvas and fail without it.

**Evidence:** `css_filter_raster` blue-pixel count 13661 (v0.9.20), 11295 (before), 13661 (after); of the 199 showcases only
that one changed. Tile brushes go through the same matrix.

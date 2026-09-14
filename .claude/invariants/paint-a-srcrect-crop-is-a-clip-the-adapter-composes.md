# A `DrawImage` srcRect is honored by the adapter, not by the PDF renderer

`RGraphics.DrawImage(image, destRect, srcRect)` promises to draw only part of an image. PDF cannot
express that: there is no operator for drawing a sub-rectangle of an image or Form XObject. The only
implementation is the one `GraphicsAdapter.DrawImage` composes — clip to `destRect`, then place the
**whole** image at the scale/offset that lands `srcRect` on it (`ComputeCroppedPlacement`).

Consequences a future change must not undo:

- **`XGraphicsPdfRenderer.DrawImage(image, destRect, srcRect, unit)` ignores `srcRect` and always will.**
  It shipped with a `// TODO: incomplete - srcRect not used`, which is how `border-image` came to paint
  the entire source into each of its nine regions while passing every test. Never route a crop through
  it, and never "fix" it there.
- **`srcRect` is in the image's own natural units** — device pixels for a raster, points for an
  `XForm` tile — i.e. whatever `RImage.Width`/`Height` report for that image. Mixing in layout units
  silently mis-crops.
- **A whole-image `srcRect` must stay on the un-cropped path.** Every background layer passes
  `(0, 0, image.Width, image.Height)`; changing which `XGraphics` overload that reaches changes the
  emitted operators (the two-argument form appends a vestigial `100 Tz`) and breaks tests that pin the
  `cm`/`Do` sequence.
- **Clipping does not stop sampling across the cut.** A smoothing renderer reads half a source pixel
  past the clip, so a cropped draw at a large scale factor bleeds the neighbouring region in — a
  quarter of the border width, measured, for a 12px texture on a 12pt border. Cropped slice painting
  turns `RImage.Interpolate` off for the duration.

A test that only asserts destination rectangles cannot see any of this. `TestRecordingGraphics`'
`DrawImageCall` records `SrcRect` and the `Interpolate` flag as of the call for exactly that reason.

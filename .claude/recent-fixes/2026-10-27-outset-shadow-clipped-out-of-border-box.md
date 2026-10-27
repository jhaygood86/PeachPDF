# Outset box-shadow is clipped out of the border box (#1831)

`PaintBoxShadows` now pushes an even-odd path clip (huge rect + the border-edge rounded rect) around outset layers, so a box with no or a translucent background no longer shows the shadow through its interior (CSS Backgrounds 3 §7.1.1).

- **Do not use `Canvas.PushClipExclude`**: it is a no-op in `PdfCanvas` and `RasterCanvas` and pushes nothing, so a paired `PopClip` unbalances the PDF graphics state ("Cannot restore without preceding save"). A `PushClip(GraphicsPath)` with `FillMode.EvenOdd` works on PDF, raster and SVG.
- Verified by rendering the issue's repro through PDFium: interior (255,255,255), ring (255,127,127).
- `drop-shadow()` is untouched (browsers do not clip it out).

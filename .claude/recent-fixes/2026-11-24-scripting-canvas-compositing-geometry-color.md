# PeachBrackets.Scripting: canvas compositing, patterns, color spaces and the geometry interfaces

Phase G of the WPT gap plan (the part that needs no new `PeachDrawing.Core` API). html-canvas 779 -> 1002 subtests.

- **Porter-Duff modes and partial `clearRect` are a raster fallback inside `CanvasBitmap`**, not Core API: `Record` routes a state whose composite is
  one of the nine to `RecordComposite`, which renders the drawing so far (`RenderPixels`), renders the new shape alone (a temporary `CanvasBitmap` with the
  composite reset to source-over, so alpha, shadow, clip and transform apply as usual), combines the two per pixel with the standard factors, and replaces
  `_ops` with one `DrawImage` of the result. The clip is honored as a mask (the clips painted alone), so pixels outside it keep what they had. Partial
  `clearRect` is `destination-out` with an opaque shape. A full-canvas clear stays the cheap `ClearDrawing`.
- **Patterns**: `FillWithPattern` pushes the path as a clip and draws the tile image over the path's bounds (capped at 40,000 tiles); `createPattern(canvas)`
  snapshots the canvas pixels (it returned null before, so the fill silently stayed black).
- **Color spaces are converted at the ImageData boundary**: the canvas keeps sRGB; `getImageData(..., {colorSpace})` converts out, `putImageData` converts
  in from the data's `colorSpace`. Real wide-gamut storage (a Display P3 image keeping colors outside sRGB) is not done: those WPT cases still fail.
  `getContext` reads and validates `alpha`, `desynchronized`, `willReadFrequently`, `colorSpace`, `colorType`, `toneMapping` (`ContextSettings`).
- **`GeometryScripts.cs`**: `DOMMatrix`/`DOMMatrixReadOnly` (column-major 4x4 with the `a`-`f` aliases, `is2D` tracking, CSS transform parsing), `DOMPoint`, `DOMRect`.
  **Trap:** the read-only base constructors are also called with `.call(this, ...)` by the mutable subclasses, so they test `this instanceof`, not `new.target`.
  `getTransform` and `getBoundingClientRect` return real instances (the layout-read tests failed when the rect was built without `new`).
- Not done: `alpha: false` opaque background, float16, `filter`, text spacing properties, ImageBitmap color-space options, workers/`transferControlToOffscreen`,
  `requestPaint`/draw-element-image (a different proposal), video sources.

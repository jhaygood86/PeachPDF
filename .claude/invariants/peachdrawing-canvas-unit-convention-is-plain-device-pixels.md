# `RasterCanvas`'s own contract is plain device pixels; PDF-point semantics are a constructor parameter, not a hardcoded assumption

`RasterCanvas` has no built-in notion of a PDF point, a CSS layout unit, or any host document's own
coordinate system - it is a generic 2D raster `Canvas` (`PeachDrawing.Core.Canvas`) usable with
zero PeachPDF reference at all. Its actual per-instance unit convention is entirely determined by the
`pixelsPerPoint` value passed to its constructor: "one user-space unit is `pixelsPerPoint` device pixels."

- **`RasterRenderContext.CreateCanvas(widthPx, heightPx, dpi)`** (the standalone entry point) always
  constructs with `pixelsPerPoint = 1` and `RasterSurface.PixelsPerUnitX/Y = 1` - so for a standalone
  caller, one user-space unit *is* one device pixel, directly, with no PDF/CSS concept anywhere in the
  path. This is the "least surprising default" a `Canvas` implementer or a standalone consumer should
  expect and can rely on.
- **`GraphicsAdapter.BeginRasterSurface`** (PeachPDF's own nested-fallback-surface seam - see
  [[raster-graphics-mirrors-graphicsadapters-unit-conventions]]) constructs a `RasterCanvas` through
  `PeachDrawing`'s public `RasterSurfaceFactory.Create` with PeachPDF's *real* `PixelsPerPoint`, so that
  one `RasterCanvas` instance mirrors `GraphicsAdapter`'s own PDF-point convention exactly, for exactly as
  long as it takes `FragmentPainter` to paint into it.

**The rule this file exists to pin**: nothing in `RasterCanvas`/`RasterSurface`/`RasterSurfaceFactory`
(`PeachDrawing`, all of it usable standalone) may assume `pixelsPerPoint` is any particular value, read a
PDF-specific constant, or otherwise special-case "when PeachPDF is the caller." The seam is exactly one
constructor parameter; if a future change to the raster-fallback path needs to pass PeachPDF-specific
context into `RasterCanvas` any other way (a second parameter, a callback, a PeachPDF-owned type reference),
that is a sign the "usable with zero PeachPDF reference" contract is about to be broken, and the fix belongs
on the `GraphicsAdapter`/`RasterSurfaceFactory` side of the boundary (still `pixelsPerPoint`-only into
`RasterCanvas` itself), not inside the package meant to stand alone.

An earlier draft of this design assumed the opposite shape - that PeachPDF's own glue code would push a
`Matrix3x2` unit-scale transform before handing control to `FragmentPainter`, keeping `RasterCanvas` itself
permanently pinned to "1 unit = 1 pixel." That turned out to be unnecessary: `RasterSurfaceFactory.Create`
already needs `pixelsPerPoint` for its own DPI-to-pixel-grid math (see
[[raster-a-bitmap-is-placed-at-its-own-snapped-rectangle]]), so threading the same value into the
`RasterCanvas` constructor it builds is strictly simpler than adding a second coordinate-system boundary
around it - and it is plumbing PeachDrawing.Core's `Canvas.BeginRasterSurface` hook
already carries, not new public surface. A standalone consumer never sees `pixelsPerPoint` at all; it only
exists on `RasterCanvas`'s constructor, which `RasterRenderContext.CreateCanvas` calls on the standalone
consumer's behalf.

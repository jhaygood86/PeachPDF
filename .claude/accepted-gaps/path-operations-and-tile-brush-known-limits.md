# Known limits of `PathOperations.Combine` and `TileBrush`

Found by review and deliberately not built; none is a regression.

- **A single cubic that loops across itself is not cut at its own crossing** (`PathCombiner` cuts curves against *other* curves
  only, and skips a curve's neighbours in the same subpath). A shape that crosses itself where two different curves meet is
  handled; a lone looping cubic is not. Cutting a curve against itself needs a self-intersection test (the cubic's loop
  parameters), which nothing in PeachPDF needs yet.
- **Long overlaps are approximated.** Two curves that run along each other for a stretch report intersections at every
  subdivision level; the search stops after 64 per pair. The result stays small and correct to the engine's precision (a circle
  united with a differently-split copy of itself is tested), but is not minimal. The intersection loop is O(n²) in curves; there
  is no spatial index, so very large inputs (thousands of segments) are slow.
- **The raster `TilePaint` does not pre-filter a strong reduction.** `BitmapPaint` box-averages by an integer factor before
  sampling; a tile brush whose cell is drawn far smaller than its bitmap will alias, and `Bicubic` is treated as bilinear there.
- **`pixelated` / `crisp-edges` in a PDF are a request, not a guarantee.** They write `/Interpolate false`; a viewer is free to
  smooth anyway (Chrome's viewer honours it for images, others may not).
- **`image-rendering` accepts only the CSS Images 3 keywords.** The legacy `optimizeSpeed`/`optimizeQuality` and
  `-webkit-optimize-contrast` are not recognised. An invalid value resolves to `auto` (the same `OrDefault` pattern `object-fit`
  uses) instead of being dropped and falling back to an earlier declaration.

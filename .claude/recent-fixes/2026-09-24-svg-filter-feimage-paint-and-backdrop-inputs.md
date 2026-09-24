# SVG filters: `feImage`, `FillPaint`/`StrokePaint`, and a real `BackgroundImage`

Builds on [the raster backend](2026-09-24-raster-backend-svg-filters-backdrop-flatten-perspective-bitmap-glyphs.md); closes the two gaps it left in #1036.

**The load-bearing ideas.**
- **An input that is painted, not computed, is a nested raster scope.** `SvgRasterFilterEvaluator` resolves `FillPaint`, `StrokePaint`,
  `BackgroundImage`, `BackgroundAlpha` and `feImage` lazily like `SourceAlpha`, each at most once per evaluation. What paints them comes
  through an abstract `SvgFilterInputs` (one allocation per filter render, implemented inside `SvgRenderer` so the evaluator keeps no
  renderer dependency). The nested scope's surface *becomes* the result (tracked, released with the rest) - no copy. A solid `FillPaint`
  never opens a scope: `FilterOps.Fill` on the blank output.
- **`BackgroundImage` is a repaint, not stored state.** Same idea as backdrop-filter: nothing keeps what was painted, so it is rebuilt on
  demand. Two layers into one bitmap: the page (a mirror `FragmentPainter` stopped after the `<svg>` box's background and borders, before its
  content: `_stopBeforeContent`, hooked in `ReplacedFragmentPainter`) and the SVG's own earlier content (`SvgRenderer` re-walks the root
  children with `SvgBackdropContext.StopElement`). `RGraphics.SvgBackdrop` carries the context only on the graphics that paints the
  document's root elements; a group's offscreen tile has none, which is what makes an isolating group's backdrop start empty.
- **It needs the accumulated transform.** `RGraphics.CurrentTransform` (in `GraphicsAdapter` and `RasterGraphics`; a raster region is seeded
  from its requester) is the composition of the pushed `RMatrix` values verbatim. Page content is drawn into the region with the inverse of it
  pushed; root-space SVG content with `root.Then(inverse)`. Composition is closed under the adapters' "linear part is not divided by
  PixelsPerPoint, translation is" convention, so nothing needs unit fixing.
- **The clip has to be handed over in layout space.** Paint culls fragments against `g.GetClip()`. A region bitmap's clip is in its own user
  space, so with the inverse pushed the mirror compared layout rectangles against the wrong space and never reached the SVG (the stop was never
  hit and the "backdrop" came back empty). `PaintBackdrop` pushes the region's layout-space bounding box as a clip first. Found by printing
  `visible`/`stopped` in the mirror; the symptom was a transparent result, not an error.
- **A document that reads the backdrop cannot use the shared form.** `RenderCachedInto` falls through to a direct `RenderInto` for
  `SvgDocument.ReadsBackdrop`.

**Trap.** `BackgroundImage` covers only the filter region, so `feOffset` past the region reads transparency. Two of my first tests asserted
otherwise and "failed" until the geometry was corrected; the code was right.

**Not done.** Isolation groups inside the SVG (`opacity`/`mask`/`filter`/`clip-path` ancestors) and a shared `<use>` target - see
[the gap](../accepted-gaps/svg-filter-backdrop-isolation-groups.md) (#1323). A stand-alone SVG image used through `<img>` gets only its own
layer, by design.

**Evidence.** Full net8.0 suite (see PR). New tests: `SvgFilterInputsTests` (pixels through `SvgRenderer` into a `RasterGraphics`: paint
inputs, `feImage` image/subregion/meet/element/translate/unresolvable/self-reference, backdrop before/after ordering, opacity group, viewBox,
a nested filter inside the repaint), `SvgBackdropPaintTests` (a real HTML page: page over paper, isolating SVG, SVG background,
transformed and isolating ancestors, `<img>` SVG), `FilterOpsAlphaFillTests` (scalar vs vector equality), `RMatrixCompositionTests`.
The `svg_filter_inputs` showcase was rasterized with PDFium and MuPDF; both agree (blurred stripes and text through the pane, silhouette of
the earlier circles only).

**Kernel timings** (Release, tiered compilation off, 2000 x 2000 px, AVX2 machine): `ZeroColor` scalar 1.47 ms, `Vector128` 0.68 ms;
solid `Fill` per-byte 3.33 ms, packed `uint` fill 0.42 ms (7.9x). The end-to-end cost of a backdrop filter is one page repaint into a
region-sized bitmap per filter (capped at three nested).

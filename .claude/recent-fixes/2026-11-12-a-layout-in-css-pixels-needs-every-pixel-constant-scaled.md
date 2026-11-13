# Laying out in CSS pixels exposed pixel constants that ignored the layout unit; a raster host can now lay out in CSS pixels

Found by the WPT runner. The runner first painted at 1 layout unit = 1 point and pushed a 4/3 transform. That works, but the engine does
**no pixel snapping under a transform** (`DecorationPixelSnapping.Snap` returns the rectangle unchanged), so two boxes laid out at the same
fractional edge are drawn with the same fringe, and a box drawn on top of another (the reftests' "red under green") lets the one below bleed
through at the edge. The same is true for any host that scales the canvas instead of running layout in CSS pixels.

## What changed
- `RasterRenderContext.PixelsPerPoint` (default 1, public API; mirrors `Win2dRenderContext`, `SkiaRenderContext`, `CoreGraphicsRenderContext`)
  overrides `LayoutUnitsPerPoint`, and `CreateCanvas` hands the canvas the same value. Set it to 96/72 and layout runs in CSS pixels; the canvas
  has no transform, so snapping applies and lands on device pixels. Declaring a 4/3 canvas *without* telling the context (an earlier experiment)
  is wrong in a different way - layout stays in points while the snapping grid becomes 1pt - and scored worse; the two must agree.
- **`CssValueParser.SnapBorderWidth` ignored the layout unit.** It snapped to a fixed 0.75-unit grid, so with layout in CSS pixels a `5px`
  border became `floor(5 / 0.75) * 0.75` = 4.5 units wide and every border-bearing test differed from a reference drawn with backgrounds.
  It now takes the context's `LayoutUnitsPerPoint`. This is a real defect for every host that lays out in CSS pixels (the WinUI view does,
  at 96/72) and asks for `SnapBorderWidthsToCssPixels`. It was the single largest cluster: css2-flow 872 -> 1285 passes, css2-positioning
  144 -> 340, css-backgrounds 147 -> 277.
- **Text baselines snap too** (`DecorationPixelSnapping.SnapBaseline`, in `FragmentPainter.DrawWordGlyphs`, under the same
  `SnapBoxDecorationsToCssPixels` option and the same no-transform condition): the baseline (`top + ascent`) goes to a whole CSS pixel
  vertically. Without it the same text laid out at 10.2px and at 10.4px (a flex item centered at a half pixel against a plain block) painted
  with different anti-aliasing rows - 109 flexbox tests differed by one row of "max 15" pixels. css-flexbox 413 -> 515.

- **Fonts must be built in points and report layout units.** `RasterCanvas` follows `PdfCanvas`'s convention (`Font.Size` is points; the canvas divides
  layout units by `PixelsPerPoint`), so a plain `TypefaceFont` created at the layout size drew text 1.33x too large and wider than its laid-out box
  at 96/72 - invisible to reftests, which draw test and reference alike. `RasterRenderContext` now builds a `RasterFont` (the `SkiaFont` shape: a
  `TypefaceFont` of `size / PixelsPerPoint` whose metrics are rounded and scaled to layout units) whenever `PixelsPerPoint` is not 1, and a plain
  `TypefaceFont` as before otherwise, so existing standalone callers see no change. `PixelSnappingTests` pins the glyph height.
- **Pieces of a bidi-split box snap on every edge** (`SliceGeometry.SnapsEveryEdge`): the snapping leaves an edge that is "a cut across a break" where it
  is, which for the open end of a piece (it ends at other text, not at the box's next fragment) left a 0.66-coverage fringe the reference's separate
  spans do not have.

- **List marker shapes snap** with the option (`MarkerFragmentPainter.PaintShape`): css2-generated 197 -> 223.
- **Floors were re-seeded** at 95% of this run (`wpt-floors.json`, all 16 modules). Two went *down* - css-lists 26 -> 23 and css2-generated
  225 -> 211 - because a few tests that passed under the transform-based painting (everything at 4/3 fractional positions, no snapping, text 1.33x)
  stopped passing; the other 14 rose, by ~1,100 tests in total. The lowering is deliberate, not a regression to hunt: those tests did not pass for
  the right reason.
## Still wrong with PixelsPerPoint != 1 (found, not fixed here)
Other `Length.PointsPerPx` uses ignore `LayoutUnitsPerPoint` (grep for it): `BackgroundImageDrawHandler.pxFactor`, `BorderImageDrawHandler`
(`numberUnit`), `ReplacedContentRenderer`, `MarginBoxRenderer`, `FragmentEmitter` line ~2582, the sub/superscript shifts in `CssLayoutEngine`. The
tests that passed with the transform and fail now (border-image, vector backgrounds, SVG `cover`, multicol gaps, ...) point at them.

## Evidence
Full WPT run, 16 modules, Windows: +1,544 passes, -382 against the transform-based run; Engine.Tests 19,829, PeachPDF.Tests 1,978,
PeachBrackets.Tests 1,208, PeachDrawing.Tests 401 green on net10.0.

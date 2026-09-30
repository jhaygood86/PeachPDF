# SVG approximations closed (issue 193)

- **Rotated/skewed radial `gradientTransform`**: `RadialGradientBrush`/`GetRadialGradientBrush` gained an optional `Matrix3x2? transform`
  that carries the circle (center/focus/radii, authored *untransformed*) into paint space. `SvgRenderer` only takes this route when the
  matrix has `M12`/`M21` (translate/scale-only still pre-applies, keeping old output byte-identical). PDF: `PdfShading.SetupRadialMultiStop`
  writes `/Coords [fx fy 0 0 0 1]` (focal expressed in unit-circle space, clamped inside it) and `EllipsePatternMatrix` = unit circle → radii →
  transform → `WorldToView`, taken from three mapped points so any flip is handled. The alpha soft-mask form must use the *same* matrix and
  coords or a translucent-stop gradient mis-registers with its mask. The old `isEllipse` test cannot decide this case (a rotated circle has
  `rx_v == ry_v`). Raster: `RadialPaint` composes the inverse into device→local.
- For `objectBoundingBox` units the transform acts in 0-1 box space, so the user-space matrix is `B⁻¹·G·B` (`GradientTransformInUserSpace`).
  The pre-existing translate/scale path still applies `G` after bbox mapping (wrong for non-square boxes) - left alone to keep output stable.
- **Arc marker tangents**: `SvgMarkerGeometry` uses `EllipticalArc.TryGetCenterParameterization` and the ellipse derivative at each end,
  signed by the sweep (see the diametric-arc invariant); degenerate arcs fall back to the chord.
- **Filled `<polyline>`**: fill path is a closed copy, stroke/markers keep the open path. PDF output is unchanged (`f` closes implicitly) - only
  a backend that doesn't (raster) shows the difference, so the test spies on `GraphicsPath.Flatten(...).Closed`, not PDF text.
- **`<use>` of `<use>` of a container** now gets the isolated opacity group. A `<use>` of a single leaf with fill+stroke still uses the
  per-shape multiply on purpose.

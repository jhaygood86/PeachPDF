# A non-invertible transform paints nothing instead of throwing (#1693)

`transform: scale(0)`, `matrix(0,0,0,0,0,0)`, a rank-1 `matrix()`, or an edge-on `rotateX(90deg)` made
`XMatrix.Invert` throw `NotInvertible` out of `PdfGraphicsState.RealizeCtm`, which `PaintFragment` wrapped
as `HtmlRenderException` - no PDF at all for one degenerate element (an animation keyframe that scales to 0
is the usual source, see [CSS animations as a still frame](2026-10-09-css-animations-as-a-still-frame.md)).

- **Load-bearing idea:** css-transforms-1 says such an element has no visible area and is not rendered, its
  layout box unchanged. `FragmentPainter.PaintFragment` therefore skips the box *and its subtree* (it simply
  neither pushes the matrix nor paints) - the guard is `FragmentPainter.IsInvertible`.
- **Every `PushTransform` in the painter needed it, not just the main one:** the "affine once the parent's
  perspective is in" push, the no-raster fallback that pushes the affine linearisation, and the two
  `Linearise(...)` pushes that supply invisible selectable text over a warped bitmap
  (`FragmentPainter.Projective.cs`, `FragmentPainter.Preserve3D.cs`). An edge-on plane under perspective gives
  a singular linearisation even when the homography itself is fine. They all go through
  `FragmentPainter.TryPushTransform` (and `PaintUnderTransform` for the two that paint the element), so a new
  push site has one obvious thing to call; the element's own `transform` push keeps its inline check because
  it also decides to skip the subtree.
- **Realistic input does not reach the edge-on text guards:** `rotateX(90deg)` is numerically not quite
  singular and an edge-on plane's bitmap bounds are empty, so `PaintProjective` returns before the text push.
  Those guards are defensive, so the two text-supply loops share `SupplyWarpedText` and the guard is
  tested by handing it a collapsed `Homography` directly (`PaintUnderTransform` likewise, with a zero scale).
  Before that refactor diff coverage sat at exactly 90%, with those branches unreachable from HTML.
- **The determinant is computed in double** with a `1e-12` cut-off, deliberately a little looser than the
  writer's (`DoubleUtil.IsZero`, < 10 x double epsilon): the float determinant of `scale(1e-20)` underflows to
  0 anyway, and a matrix this lets through must never be one the writer throws on.
- **The painter guard is per element, but the writer inverts the *cumulative* CTM:** two nested
  `scale(0.00001)` are each invertible and their product is not. `PdfGraphicsState.RealizeCtm` therefore only
  inverts `InverseEffectiveCtm` when `HasInverse` (it is read just by `WorldToView`, and nothing is visible
  under a collapsed CTM). A test with `scale(1e-8)` does *not* exercise this - that already fails the painter's
  own guard - which is how an earlier version of the test passed without the writer change.
- `IsInvertible` also rejects a non-finite translation, which would otherwise reach the content stream as a
  `NaN`/`Infinity` `cm` operand.
- **Evidence:** the new `TransformIntegrationTests` cases fail 8/9 without the change (spy `Canvas`: no
  `PushTransform`, no fill, no string; plus end-to-end `GeneratePdf`) and pass with it; `scale(0.5)` control
  still paints.

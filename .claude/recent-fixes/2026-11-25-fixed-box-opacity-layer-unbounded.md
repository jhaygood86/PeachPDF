# position: fixed + opacity/transform: layer sized to the unbounded initial clip (#2457)

`FragmentPainter.PaintFragment` calls `Canvas.SuspendClipping()` for a fixed box, which pops to the canvas's
initial clip. `PdfCanvas` seeds that as `Rect(0, 0, double.MaxValue, double.MaxValue)`. `PaintWithOpacity` then
called the default `Canvas.BeginLayer` with no `Bounds`, which sizes the layer `(0, 0, clip.Right, clip.Bottom)`:
a Form XObject with a `/BBox` of `double.MaxValue` (veraPDF: ISO 19005-3 6.1.13, integer out of range) that nothing
paints into, so the watermark vanished.

Fix: when the canvas clip is that unbounded one (`UnboundedClipThreshold`), `PaintWithOpacity` passes
`LayerOptions.Bounds` = the subtree extent inflated by 2. Absolute boxes were never affected (they keep the finite page clip).

**Trap:** any new layer opened under a fixed box must not rely on `GetClip()` for its size.
Tests: `FixedPositionTransformIntegrationTests` (fail without the fix).

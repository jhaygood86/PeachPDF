# Gradient stops interpolate premultiplied by alpha, and an lch gray takes the other stop's hue

Fixes #1837 and the `lch` half of #1773.

- **Load-bearing idea:** premultiply in the engine (`ColorSpaceConverter.Interpolate`, `LerpPremultiplied`), not in either backend. The PDF backend builds an RGB shading plus a separate alpha soft-mask shading and lerps
  them straight, which is why `red -> transparent` went through gray; `PeachDrawing`'s raster `PaintSource` already premultiplied, so changing the engine makes the two agree.
- **sRGB was an early return.** `ApplyColorSpaceInterpolation`/`ApplyConicColorSpaceInterpolation` returned the stops untouched for `srgb`, so no sampling happened at all. A segment whose two stops differ in alpha is now sampled
  (15 samples, as the other spaces). Equal alpha stays two stops (an opaque gradient's PDF is unchanged), and a hard stop (`red 50%, transparent 50%`) is not sampled: the samples would sit at one position and put
  duplicate `/Bounds` into the stitching function.
- **Measured:** `linear-gradient(to right, #f00, transparent)` at 0/25/50/75/100% of 200px on white is `(255,0,0) (255,62,62) (255,125,125) (255,189,189) (253,253,253)` in MuPDF and within 1 in PDFium; Chrome is
  `(255,7,7) (255,65,65) (255,128,128) (255,192,192) (255,249,249)`. Before: `(249,5,5) (207,62,62) (191,128,128)...`.
- **Trap:** `TwoStopsNotAtEdgesWithAlpha_PadsBothColorAndAlphaStitchingFunctions` pinned `/Bounds [0.2 0.8]` for a two-stop translucent gradient; it now sees the sampled bounds between 0.2 and 0.8.
- **Not done:** SVG `<linearGradient>` in sRGB with stops of differing `stop-opacity` is still straight-lerped (only the `linearRGB` path goes through `Interpolate`). `oklch`/`hsl`/`hwb` gray hues: see the accepted gap.

# Win2D text rendering modes: hinted, weight-corrected and LCD text without DirectWrite

`Win2dRenderContext.TextRendering` (`Auto` default, `Outline`, `Grayscale`, `Lcd`) draws `PeachDrawing`-engine runs as a coverage mask instead of a
filled outline. `Auto` (the default, see the migration note) picks per run: LCD over a host's opaque session when ClearType is on, grayscale
otherwise and for exports, outline for large or transformed text.

## Why text looked different from DirectWrite
Measured, not assumed (DirectWrite vs outline fill, Segoe UI 10-20px): the outline fill is unhinted and blended as linear coverage, while
Direct2D's text renderer fits stems vertically and maps coverage to alpha with a lightness-dependent gamma/contrast. Light text on a dark
background carried ~16% less ink than DirectWrite at 96 dpi (ratio 0.84; dark on light was ~1.0 in total ink but still differed per pixel).
Layout metrics were identical for Segoe UI.

## What the load-bearing idea was
- Rasterize the run with the shared `RasterCanvas` (hinting from the FreeType-port engine, already public) into a coverage mask at 1 or 3 samples
  per pixel, then map coverage with a fitted correction and draw the bitmap on whole device pixels. The hinted baseline snap is what makes it
  crisp; it only works when the bitmap is placed in device pixels (identity transform, `session.Dpi / 96`).
- Constants (`Win2dTextBlend`) were fitted by coordinate descent against Direct2D output over 8 colour/background pairs: mean error vs
  DirectWrite fell about 50% (e.g. 3.7 to 1.8). LCD constants were fitted against real Direct2D ClearType on an opaque target.
- LCD needs the backdrop: the session must be `Flush`ed before it is read or it reads zeros; the opaque patch is drawn back with
  `CanvasBlend.Copy`. A composer reads its own bitmap; a host's session (an `HtmlView`'s virtual control) is read through Win2D's native interop, see
  `.claude/invariants/win2d-reading-a-sessions-pixels-goes-through-its-native-target.md`. Clips do not stop a read.

## Measured and rejected
- A straight port of FreeType's LCD filters: FIR5 default scored 1.331, light/Harmony 1.237, the fitted 3-tap 1.221 (mean error vs ClearType). The
  filter hardly matters beyond having one (no filter: 2.298). Not worth porting; the rasterizer cost dominates.
- Snapping each glyph's x to a fixed subpixel step (to imitate DirectWrite positioning) made the match worse at every step except 1/8 pixel
  (1.281 vs 1.303 unsnapped, within noise); dropped.

## Traps
- Cost: ~2.5-3.8 ms per 60-char run vs ~0.5 ms outline. A glyph-mask cache is the obvious next step.
- A run is cropped to the composer's bitmap before rasterizing, so huge or off-page text does not allocate; over a host's session there is no
  such bound and a run over 6M samples falls back to the outline.
- `RasterSurface.Buffer` is internal; read the mask through `RasterCanvas.ToPixelBuffer()`.

Evidence: Win2D suite 183/183 (25 new in `TextRenderingTests`, incl. lifecycle counters balanced and clip/transparent fallbacks).

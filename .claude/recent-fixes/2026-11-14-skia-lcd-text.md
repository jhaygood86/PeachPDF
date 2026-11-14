# PeachDrawing.Skia draws text as hinted masks, with ClearType-style LCD text in the Avalonia view

User ask: "text should be ClearType if ClearType is enabled" in `PeachBrackets.Avalonia.HtmlView`.

## What is load-bearing

- **A port of the Win2D approach, not Skia's fonts.** `SkiaTextMask`, `SkiaTextMaskCache`, `SkiaGlyphMaskCache` and `SkiaTextBlend` are ports of the `Win2d*` originals
  (same hinted masks from the shared `RasterCanvas`, 3 samples per pixel for LCD, the same fitted weight-correction constants and FIR filter, so the same text looks
  the same through both backends). Not shared as source: the originals are coupled to `Win2dRenderContext`, and Win2D cannot be built beside the Avalonia tests.
  `Win2d*` is untouched. Changing a fitted constant means changing both.
- **LCD is composited without reading the backdrop, with two opaque images**: `keep` = 255 - per-channel coverage drawn with `SKBlendMode.Modulate`, then `add` =
  ink x per-channel coverage drawn with `SKBlendMode.Plus`. That is `dst*(1-a_c) + ink_c*a_c` per channel. Both are coefficient blend modes, so a CPU raster canvas and a
  GPU (Ganesh) canvas do the same thing; no `SKRuntimeEffect`/blender needed and no readback. Verified on the CPU by pixel tests and on this machine's GPU by looking.
- **The mask is drawn at an identity matrix on whole device pixels** (`SetMatrix(Identity)` inside a Save/Restore, nearest sampling); the clip stays, so a run cut by a
  clip is cut. The mask is placed from `SKCanvas.TotalMatrix` (translation + one uniform scale; `ScaleX != ScaleY`, a skew or perspective is an outline run), so the
  view's `Scale(zoom*dpi)` works at non-integer scales and hints for ppem = size x scale.
- **LCD needs an opaque backdrop and the canvas cannot know it**: `SkiaCanvas.OpaqueBackdrop` is set by the host (the view sets it from the page background's alpha; the
  composer from its `Background`). Layer content canvases, recorded tiles (a picture replayed under another transform has no pixel grid), blend modes and non-opaque
  backdrops fall back (Grayscale for the first and last, Outline for tiles and blends). Grayscale is a plain premultiplied SrcOver, valid over anything.
- **`Auto` follows the system through an injectable seam** (`ISystemTextSettings`, internal, per context so tests do not share global state): Windows `SystemParametersInfo`,
  Linux `gsettings` (`font-antialiasing` = `rgba`, `font-rgba-order` = `bgr`) with a 1 s process timeout, read once per process on a pool thread started when the first
  context is made (the first text drawn waits at most 500 ms, then grayscale), macOS and the rest grayscale. A composer never gets LCD from `Auto`.
- **Default `Auto`, no migration note.** `PeachDrawing.Skia` has not shipped in a release (absent from `v0.10.0`), so there is no existing consumer whose outline text
  would change, and Win2D's precedent is `Auto`. The Avalonia view's property is also `Auto`.
- **The view never reads an `AvaloniaObject` on the render thread**: `TextRendering` is recorded in `PaintState` in `Render()` and applied to the context inside
  `PaintFrameLocked` under `_documentLock`.

## Found by running it

- The first cut let `Auto`'s system answer force LCD even for an explicit `Grayscale` (the system check was not guarded by `requested == Auto`). A pixel test of
  `Grayscale` with a "subpixel" fake system caught it.
- In the headless view tests the real render pass races the test's own captured frame: a stale frame recorded before the property changed resets the context's
  `TextRendering` to the old value between the test's paint and its assertion. Assert on the painted pixels, not on `RenderContext.TextRendering`.
- A zoom test must pass a focus (`SetZoom(1.5, new Point(0, 0))`): the default zooms about the viewport center and scrolls the first line away (ink = 0).
- `LibraryImport` needs `AllowUnsafeBlocks` in the Skia project (SYSLIB1062).

## Evidence

- Skia tests: 192 pass (19 new). Avalonia tests: 281 pass (6 new).
- Real window on this machine's GPU (`gpu=True` in a temporary render log, no exception in any frame): ClearType on (smoothing=1, type=ClearType, RGB). `Lcd` and `Auto` show
  orange left and blue right edges on dark stems, `Grayscale` is neutral and crisp, `Outline` is thinner and softer; 60 real wheel notches (62 distinct frames) kept
  colored fringes in every frame with no blank frame; a 56% zoom also draws fringed text. Crops: see the session scratchpad (vis_zoom_compare.png).
- Rejected: Skia native fonts (`NativeSkiaFont`; no hinting control, not the user's decision), `SKRuntimeEffect` blenders (needs a uniform per color and a second
  code path to prove on the GPU), reading back the surface (impossible on a GPU lease).

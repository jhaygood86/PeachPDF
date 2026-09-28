# Raster anti-aliasing toggle

`ScanlineRasterizer` (`src/PeachPDF/Raster/ScanlineRasterizer.cs`) was always anti-aliased — no way to get
hard 0/255 edges out of the raster backend. `PdfGenerateConfig.RasterAntiAliasing` (default `true`) adds
that as a single, graphics-wide switch: shape fills, strokes, images and raster-hinted text all go through
it, not a text-only knob.

## What the load-bearing idea was

- **One coverage algorithm, one quantization point.** The rasterizer's exact-horizontal-span-coverage ×
  16-subscanline accumulation is completely unchanged; only the very last step — turning the accumulated
  integer coverage into the alpha byte handed to `ICoverageSink.Span` — now branches on a new `antiAlias`
  parameter (default `true`, reproducing the original single-line rounding expression byte for byte).
  `false` thresholds at the 50% point instead (`total * 2 >= TotalPerPixel ? 255 : 0`). This matches the
  repo's "don't write two independent implementations of the same grammar" convention: no second, parallel
  binary rasterizer was built.
- **Threaded like `TextHinting`/`RasterizationDpi`.** `PdfGenerateConfig.RasterAntiAliasing` →
  `PdfGenerator.ApplyRasterizationSettings` → `RAdapter.RasterAntiAliasing` → read at every
  `ScanlineRasterizer.Fill` call site inside `RasterGraphics`/`RasterGraphics.Erase.cs`/`ClipState.Intersect`
  as `_adapter.RasterAntiAliasing`. Clip-mask generation (`ClipState.Intersect`, used by a non-rectangular
  `PushClip`) goes through the same toggle, so a `border-radius`/path clip's edge is hard-thresholded too
  when the setting is off — not just direct fills. `RasterGraphics.SetAntiAliasSmoothingMode()` (the
  existing per-call AA-override hook the vector PDF backend uses for rounded borders) stays a documented
  no-op: the raster backend has exactly one AA setting for the whole render, so there is nothing for a
  per-call override to switch.
- **CLI mirrors the existing boolean-flag convention.** `--no-raster-antialiasing`, following
  `--no-compress`/`--no-default-style`/etc., not a new pattern.

## What running it showed

- The default-`true` path is provably unchanged: `RasterAntiAliasingTests.UnsetConfig_ProducesTheExactSameBytesAsExplicitlyTurningAntiAliasingOn`
  renders a filtered shape-plus-text page with the config left untouched and with `RasterAntiAliasing = true`
  set explicitly, and asserts the two PDFs are byte-identical. `TurningAntiAliasingOff_ActuallyChangesTheGeneratedPdf`
  proves the setting is not a no-op end to end (not just at the isolated-rasterizer level).
- Rasterized the `raster_antialiasing_on`/`raster_antialiasing_off` showcase (a circle, a rotated square and
  small text, forced through `filter: grayscale(1)` at 72 dpi) with both PDFium and MuPDF: both renderers
  agree — the "on" page has smooth, anti-aliased curves and diagonal edges, the "off" page has visibly
  stair-stepped ones, in both cases the same in each renderer.

## The competitor-bug retest (the reason this was built)

This session had investigated a competitor .NET imaging library's reported bug: their `Antialias = false`
option deformed small text (Tahoma 8pt) into broken/malformed letterforms compared to GDI+, at small size.
PeachPDF's raster text had looked clean under its own (until now, always-on) AA — not a fair comparison,
since the bug specifically needs AA disabled, a mode PeachPDF never had before this change.

Retested properly with the real repro conditions: Tahoma 8pt loaded from `C:\Windows\Fonts\tahoma.ttf`
(local-only — not embedded/shipped anywhere in the repo) via `@font-face`, ~128×64 CSS px,
`RasterizationDpi = 100`, forced into the raster path with `filter: grayscale(1)`,
`RasterAntiAliasing = false`, across all three `TextHinting` values. The embedded bitmap was extracted at
its native 134×67 pixels with PyMuPDF's `extract_image` (never re-rendered at another DPI, which would let
the PDF viewer's own interpolation smooth over exactly the artifact being looked for) and viewed nearest-
neighbor-upscaled.

**Result: no resemblance to the competitor's reported deformity, in any hinting mode.** All three renders
(`None`, `Standard`, `Monochrome`) show ordinary aliased/jaggy small text — stair-stepped diagonal strokes,
square-looking dots on `i`/`j` — but every letterform stays fully connected, structurally intact and
legible; nothing is broken apart, crossed, or missing a chunk. A large (48pt), unfiltered vector control
render of the same `@font-face` confirms the font actually loaded as real Tahoma (its distinctive
single-story `a`, `g`, `j` shapes match), ruling out a silent fallback-font explanation for "it looks fine."
PeachPDF's `ScanlineRasterizer`'s coverage math (exact horizontal span × 16 vertical subscanlines,
thresholded at 50%) evidently doesn't have whatever defect the competitor's disabled-AA path has; this
looks specific to their implementation, not something disabling AA does in general.

## Deliberately not done

No attempt was made to identify *why* the competitor's implementation breaks down — that library's source
was not read, and this fix is unrelated to it beyond having prompted a fair, apples-to-apples retest.

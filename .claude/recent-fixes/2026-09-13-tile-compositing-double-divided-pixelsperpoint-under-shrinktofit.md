# Tile-compositing content was scaled by PixelsPerPoint twice under ShrinkToFit/ScaleToPageSize

## Symptom

With `PdfGenerateConfig.ShrinkToFit = true` (or `ScaleToPageSize`, or any non-72 `PixelsPerInch`)
*and* at least one box painted through the offscreen-tile path (`opacity < 1`, and now also
`filter`/`mix-blend-mode`, SVG `<pattern>`/`<mask>`/`<filter>`, or a sliced/gradient background
layer), that box rendered visibly too small and displaced toward the page origin, while a plain
sibling box (painted directly, no tile) rendered at its correct size/position. The size mismatch
between the two made the correctly-sized box look "wrong" at a glance (it now overlapped its
shrunken neighbor). Confirmed identically on PDFium and MuPDF, so it was a genuine PDF-geometry
defect, not a renderer quirk.

This was *not* new to the filter/mix-blend-mode work landing alongside it — `opacity < 1` already
went through the exact same `CreateTile`/`PaintWithOpacity` path on `main`, unchanged by that
diff. The filter/blend-mode feature just made the tile path dramatically more common (any
`filter`/`mix-blend-mode` declaration now uses it, not only rare `opacity < 1`), which is what
actually surfaced this latent defect.

## Root cause

The engine lays everything out in an "inflated" layout-unit space (`PixelsPerPoint` times real PDF
points - see `Length.NeedsPixelsPerPointCatchUp`'s doc comment, issues #814/#826), and every
`GraphicsAdapter` draw call divides by `PixelsPerPoint` exactly once, via `Utils.Convert`, when
writing real PDF coordinates.

`GraphicsAdapter.CreateTile(width, height)` builds an `XForm` whose `/BBox` was sized directly
from its caller's `width`/`height` - which arrive in that same inflated space (every caller sizes
a tile from a layout rect: `FragmentPainter.PaintWithOpacity`'s `clip.Right`/`clip.Bottom`,
`CssImagePainter`'s resolved background-layer size, `SvgFilterEvaluator`'s filter region, etc.) -
without dividing by `PixelsPerPoint`. But content painted *inside* the tile, through that same
`GraphicsAdapter`'s own draw calls, *was* already correctly divided down to real points. So the
form's own `/BBox` ended up in inflated (oversized, when `PixelsPerPoint != 1`) units while its
content was already in real-point units.

`XGraphicsPdfRenderer.DrawImage`'s placement scale is `destRect.Width / image.PointWidth`.
`destRect` (also inflated-space, e.g. `PaintWithOpacity`'s `tileRect`) was correctly divided by
`PixelsPerPoint` by the *composite* call's own `Utils.Convert`. But `image.PointWidth` read back
the tile's oversized `/BBox`, so the scale came out as `(real/PixelsPerPoint) / real = 1/PixelsPerPoint`
instead of `1` - the whole form (BBox *and* the content already sitting at correct real-point
coordinates inside it) got divided by `PixelsPerPoint` a second time when placed.

## Fix

`GraphicsAdapter.CreateTile` now divides `width`/`height` by `PixelsPerPoint` before constructing
the `XForm`'s `XSize`, so the form's own `/BBox` is already in the same real-point space its
content ends up in - matching every other `GraphicsAdapter` method's contract for its own
parameters. `PaintWithOpacity` always sizes a tile to exactly the same rect it later composites
with, so after the fix that placement `cm` is always identity, regardless of `PixelsPerPoint`.

## Evidence

- A hand-built repro matching the exact reported HTML (4-card table row, one `filter: none` card
  alongside three real-filter cards, under `ShrinkToFit = true`) reproduced the corruption
  identically on PDFium and MuPDF; the fix produces pixel-identical, correctly-aligned output on
  both.
- `PeachPDF.Tests.Integration.MixBlendModeAndFilterPaintIntegrationTests.ShrinkToFitRescale_FilteredBoxTilePlacement_StaysIdentityScale`
  forces a real rescale via `PdfGenerateConfig.MinContentWidth` and asserts the placement `cm`
  immediately before the filtered box's `Do` is an identity scale - fails with `0.4625` (the
  `1/PixelsPerPoint` bug value) against the unfixed code, passes after.
- Full `PeachPDF.Tests` suite (net8.0): 11274 passed, 0 failed, 9 skipped (platform-gated).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors.
- Regenerated all 119 `PeachPDF.TestHarness` showcases; `css_filter` and `mix_blend_mode` visually
  confirmed correct (PDFium + MuPDF). `svg_filter_graph`'s star-position report does *not* share
  this root cause: that showcase's content never overflows the page, so `ShrinkToFit` never
  triggers a rescale and `PixelsPerPoint` stays `1` throughout - pixel-diffing the showcase's
  render before/after this fix showed zero difference. The shadow's visible offset there is the
  fixture's own `feOffset dx="8" dy="10"`, not a defect.

## Trap for a future change

Any new `RGraphics` method that creates or sizes a tile/form must apply the *same* single
`PixelsPerPoint` division as every other `GraphicsAdapter` draw call - a size or rect that skips
it (or that some other layer divides a second time) will only misrender once `PixelsPerPoint != 1`
(`ShrinkToFit`/`ScaleToPageSize`/non-72 `PixelsPerInch`), which most manual testing never
exercises at default settings.

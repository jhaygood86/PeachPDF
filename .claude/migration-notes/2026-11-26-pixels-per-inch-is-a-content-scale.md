# PixelsPerInch scales the content; the fit options work; ClipReport lists words past the page edge

Before: `PdfGenerateConfig.PixelsPerInch` (default 72) only changed the engine's internal layout unit, and since lengths and text were kept physically fixed at
any layout unit (the PixelsPerInch absolute-length fix), **no value changed the output**. `ShrinkToFit`, `ScaleToPageSize` and `MinContentWidth` worked by
changing that unit too, so they did nothing either: content wider than the page was cut off at the right edge and every word past it was left out of the
file. `ClipReport` listed only the word the edge cut through.

Now:
- `PixelsPerInch` is how many CSS pixels fit an inch of the page, and **its default is 96** (CSS's own size: a pixel is 0.75pt, `16px` text is 12pt, as before).
  `72` makes a pixel one point (`16px` text is 16pt), `144` half a point (8pt). The page size and margins do not change; content is laid out on a sheet
  `PixelsPerInch / 96` times the size and each page drawn at the inverse scale. A caller that set `PixelsPerInch = 96` explicitly sees no change. A caller that
  set `72` (the old default, spelled out) now gets content a third larger than before - remove the setting. A value that is not positive and finite throws.
- `ShrinkToFit`, `ScaleToPageSize` and `MinContentWidth` scale the content down until it fits across the page; nothing is cut off.
- `ClipReport` also lists every word wholly past the end of the page's content area (right edge for left-to-right text), with a `VisibleWidth` of 0.
  Words wholly outside an `overflow` clip the document asked for are still not reported.
- `RenderContext.PixelsPerPoint` (the layout unit, used by hosts of `HtmlDocument` and the raster/Skia/Win2D views) is unchanged and still keeps lengths
  physically fixed; `PdfGenerator` now always uses 1.

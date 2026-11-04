# A canvas's clip bound is the last rectangle pushed, not the intersection of those pushed

`Canvas.GetClip()` (what `FragmentPainter` culls against: `IsAnyRectVisible(fragment, g.GetClip())`) returns the top of `_clipStack`, and every backend's `PushClip(Rect)` pushes the rectangle it was given as it is (`RasterCanvas`, `PdfCanvas`, `SkiaCanvas`, `SvgCanvas`, `Win2dCanvas`). The real clip does intersect (GPU layers, PDF `W n`); the tracked bound used for culling does not.

**Symptom this caused.** `HtmlDocument.PaintPageRegionAsync` pushed the region as a clip and then `FragmentPainter.Paint` pushed the page-wide content clip, which replaced it as the bound. Nothing outside the region was culled: painting a 1200x504 strip of the 75,000-pixel long page called `DrawString` for every word of the page (1.35 million calls in a 26-strip scroll benchmark), and the clip threw almost all of it away. Strips cost 108-140 ms; with the fix 2-12 ms warm.

**Rule.** Do not rely on a clip pushed before the painter runs to narrow what it paints. Narrow the painter's own page clip instead: `PaintPageRegionAsync` sets `HtmlContainerInt.PaintRegion` and `PagePainter` intersects its page clip with it (plus a 48-unit bleed for shadows and outlines that overhang). `Engine.Tests/RegionPaintCullingTests` counts the words a strip draws (30-150 of 1,800; 1,800 without the fix) - a pixel comparison cannot catch this, because the clip makes the output identical either way.

**Not done.** Making `PushClip` intersect in every backend would fix it for every caller, but transformed content clips in a different coordinate space than the bound, so it needs its own change and a look at the PDF output. Pages with per-page margin overrides (a shifted content origin) are not narrowed.

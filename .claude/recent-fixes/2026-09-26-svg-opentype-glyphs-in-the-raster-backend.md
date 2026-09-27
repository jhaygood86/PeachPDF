# SVG-in-OpenType glyphs are drawn by the raster backend

Before, `RasterGraphics.DrawString` drew an SVG-table glyph as its plain outline in the text colour (a flat square for the test font), so a
`filter`, a shadow or a PDF/A-flattened region showed something other than the page's own glyph.

**The load-bearing idea: no new drawing code.** `SvgGlyphPainter` only ever needed an `RGraphics` and an adapter; it had been typed to
`GraphicsAdapter` for no reason other than being written there first. It now takes `(RGraphics host, RAdapter adapter)` and the raster
graphics builds one lazily and calls the same `TryPaint` the PDF text renderer does. `SvgRenderer.RenderCachedInto` already falls back to a
direct `RenderInto` when a graphics has no `FormCacheOwner`, which is the raster one, so the glyph is painted into the surface with the
ordinary SVG path (the one SVG filters already use), at whatever pixel pitch the region has.

**Placement is the PDF path's, unit for unit.** `DrawString` already holds the glyph origin in points (pen position plus the GPOS offset,
baseline from the cell ascent); the painter multiplies by `PixelsPerPoint` to get the layout-unit canvas rectangle, exactly as
`DrawBitmapGlyph` does. That is the `raster-graphics-mirrors-graphicsadapters-unit-conventions` rule, and the reason no separate raster
canvas maths exists. Clip and transform come for free: `RenderInto` pushes the canvas clip and the viewBox transform on the raster graphics.

**Order in the glyph loop:** bitmap glyph, then SVG glyph (only when the glyph has no COLR paint or layers, as in `ColorGlyphPainter`), then
the hinted or scaled outline. An SVG glyph is not part of the batched outline fill, so faux bold, faux italic and hinting do not apply to it
(they do not on the PDF path either).

**Caching.** The built `SvgDocument`s are kept per PDF document on the PDF path and per adapter on the raster path (a raster graphics knows
no PDF document): one build per (typeface, glyph, palette, colour, overrides) however many regions draw it. The `ConditionalWeakTable` is
keyed by the adapter, which lives as long as one generator, so nothing outlives it.

**Evidence.** `RasterSvgGlyphTests`: colours, placement on the baseline, palette and override, `context-fill`, the unusable-document
fallback, a clip, a transform, and an embedded drop-shadow bitmap that must contain the document's red and blue. Six of the ten fail when the
SVG branch is disabled (the four that pass are the ones whose expectation is the same for an outline: the fallback, the context-fill colour
and the two structural ones). The showcase `svg_opentype_glyphs` gained a filtered cell, rasterized with PDFium and MuPDF.

**Deliberately not done here:** distinct `context-fill`/`context-stroke` and the extent-derived canvas are the next two changes; the glyph is
still drawn on the fixed canvas in the raster path too.

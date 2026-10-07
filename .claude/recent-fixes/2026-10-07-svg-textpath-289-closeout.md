# Closing out #289: textPath stretch, user-space paint, text bbox, selectable outlines

**Load-bearing idea:** a textPath glyph that needs geometry (gradient/pattern/stroke/stretch) is now an outline built in *user space* (`PaintPathGlyph` in `SvgRenderer.cs`): rigid = outline transformed by the glyph frame, stretch = `WarpOutline` (flatten, cut edges to ~1/32 em, map each point through `WarpPathPoint`). That one change fixes the per-glyph-frame paint server problem, because no per-glyph transform is pushed around the paint any more. Layout is shared by painting and bounds (`LayoutTextPath` → `TextPathLayout`/`PlacedPathGlyph`, `TextPathBounds`).

**Found by running it, not reading it:** `TestGraphicsPath` (the test double) overrode `LineTo`/`AddMove` without calling the base, so `Flatten()` returned nothing and a warped outline was empty — the double now calls the base like the real backends. `TestRecordingGraphics` keeps `InvisibleStringCalls` apart from `DrawStringCalls`.

**Selectability:** outlined text calls `PaintInvisibleText` (Canvas.InvisibleText, render mode 3). I first skipped it for `IsOffscreenTile`, which silently dropped it for every inline `<svg>` (the page paints SVG into a form XObject, which is a tile) — so there is deliberately no tile guard.

**Raster backend for bitmap fonts:** `PaintBitmapGlyphs` draws the run black into one raster surface (coverage), paints the paint server into a second, scales it per pixel by the coverage, and `DrawRaster`s it; a stroke uses the coverage grown/shrunk by half the stroke width. Surfaces are premultiplied RGBA8 on the same grid.

**Stale items in the issue:** CFF outlines already worked (Type2CharstringInterpreter), and `PathMeasure`/`GraphicsPath.Flatten` flatten adaptively already.

**Not done:** a `<g>`/`<use>` of text still has no bbox for `objectBoundingBox` clip/mask (see the accepted-gap file).

**Evidence:** full net8.0 suite green; `svg_textpath_stretch` and `svg_text_advanced` showcases rasterized in PDFium and MuPDF and agree; text extraction returns the glyphs.

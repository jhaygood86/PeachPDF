# Paragraph drawing, shared color-glyph painter, PeachPDF loses InternalsVisibleTo, Abstractions -> Core

Landed together because each needed the previous one.

**What changed**

- `PeachDrawing.Abstractions` was renamed `PeachDrawing.Core` (unpublished, so no redirect cost); it now holds concrete shared logic, not only contracts.
- PeachPDF and PeachPDF.Tests no longer have `InternalsVisibleTo` access to `PeachDrawing.Core` or `PeachDrawing` (only `PeachDrawing` itself keeps one into Core, for `RasterSurface.Buffer`/`FontsHandler`). Everything PeachPDF reached into became documented public API: `RasterRegion` (typed replacement for the `object?`-returning `BeginRasterSurface` + `RasterSurfaceScope`), `Canvas.TransformScale/CurrentTransform/TileCacheOwner (was FormCacheOwner)/PrefersRasterGroups/FlattensTransparency/InvisibleText`, `RenderContext` raster knobs, `ISvgGlyphPainter`, `IntRect`, `PixelMath`, and the filter/warp classes in `PeachDrawing`. The two slots only PeachPDF used moved into PeachPDF: `SvgBackdropSlot` (was `Canvas.SvgBackdrop`, a `ConditionalWeakTable`) and `ITransparencyProbeSource` (was `Canvas.CreateTransparencyProbe`, an `object?` cast).
- `canvas.DrawParagraph(layout, origin, colour)` / `DrawGlyphRun` paint a `PeachDrawing.Text` layout on any `Canvas` (`CanvasParagraphExtensions`).
- COLR/CPAL color glyphs now paint on `RasterCanvas` (`DrawString` and `DrawGlyphs`). docs/peachdrawing.md claimed this before it was true.

**Load-bearing idea**: the COLR walk exists once, in `PeachDrawing.Core.ColorGlyphs.ColorGlyphPainter`, and emits resolved fills to an `IColorGlyphTarget`. `CanvasColorGlyphTarget` (any Canvas) and PeachPDF's private `PdfTarget` (XGraphics, plus the measure pass) are the two targets. Byte-identity of the PDF output was checked by rendering every glyph of ColorTestV0/V1 (layers, gradients, sweep, composite, transforms, opacity, rotation) before and after with timestamps and the random font-subset tag masked: identical.

**Found by running it**

- A `ConicGradientBrush` needs ascending stop angles; the walker's angles descend (COLR counts counter-clockwise, the brush clockwise), and the raster `ConicPaint` rendered the whole glyph in the last stop's colour. `CanvasColorGlyphTarget.SweepBrush` reverses the stops when they descend. The PDF brush tolerated the descending order, which is why the bug only showed on raster.
- The old PDF code mapped COLR composite mode 26 to the blend-mode name `"PaintColor"` (an over-eager rename of `Color`); it now goes through `PaintBlendMode.Color`. No fixture exercises mode 26.

**Not done**: `DrawGlyphs` on the PDF `GraphicsAdapter` does not paint color glyphs (PeachPDF paints them through its own `ColorGlyphPainter` in `DrawString`); a Canvas other than `RasterCanvas` must handle color glyphs in its own `DrawGlyphs` (it can use `CanvasColorGlyphTarget`).

**Evidence**: PeachPDF.Tests (Release, net8.0), PeachDrawing.Tests, PeachDrawing.Core.Tests all green; `ColorGlyphRasterTests` fail if glyphs fall back to the text-colour outline.

# COLR `PLUS` and cone-gradient `repeat`/`reflect` through the raster backend (#287)

**Idea.** PDF cannot add (no additive `/BM`) and its shadings only pad, so a glyph needing either is drawn on a
`RasterCanvas` and embedded as an image. The decision is `ColorGlyphPainter.RequiresRasterFidelity` (a scan of the paint
graph); the targets say what they can do through `IColorGlyphTarget.SupportsAdditiveComposite` /
`SupportsPeriodicConeGradients` (default false, so every vector target and every third-party `Canvas` keeps the old
source-over/pad behaviour with no change). `PaintBlendMode.Plus` is the new additive mode; `XGraphicsPdfRenderer.SetBlendMode`
maps it to `Normal` so a stray one never writes an invalid `/BM`.

**Where it plugs in.** Not a new path: `ColorGlyphPainter.Forms.cs` `RenderGlyphForm` already renders each color glyph once
into a Form XObject at the canonical 100-unit em, cached per (typeface, glyph, palette, color). The raster picture is drawn
*into that form* (`TryPaintGlyphRasterized`, 3 px per canonical unit = a 300 px em), so sharing, placement and the invisible
text layer are untouched. `XGraphics.RasterContext` (set by `GraphicsAdapter`) supplies the `RenderContext` the canvas needs;
no `RasterRenderContext` is built, because its constructor enumerates every installed font.

**Traps.**
- A repeating raster brush's period is its *stops' own extent*, while a COLR gradient's period is [0, 1]: stops that do not
  start at 0 / end at 1 must be padded first (`PadStopsToUnit`) or the period is wrong.
- `reflect` has no brush spread mode; it is a `repeat` of the ramp run forward then backward over twice the circles' span
  (the circles at t = 2 lie on the same family: center `c0 + 2(c1 - c0)`, radius `r0 + 2(r1 - r0)`, skipped when that radius
  would be negative).
- Plus on premultiplied pixels is a clamped sum including alpha, so it bypasses `BlendModes`' unpremultiply math.
- Verified by rasterizing the `color_emoji` showcase with PDFium and MuPDF: both agree (white overlap, ringed cone).

**Not done.** `CBDT` 1/2/5-9 and `sbix` tiff/mask: see the accepted gap. Real Apache-2.0 bitmap fonts (`SimonCozens*.otf`) are
bundled only as regression tests for the formats already supported.

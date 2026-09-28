# Raster color glyphs: no synthetic italic skew, and painted in glyph order ahead of the run's plain glyphs

`RasterCanvas` paints a COLR/CPAL (like a bitmap or SVG) glyph the moment it reaches it, while the run's plain outline glyphs are batched and filled once at the end. Two consequences, both shared with the existing bitmap and SVG glyph branches and with the PDF backend:

- A `font-style: italic` request that needs synthetic slant skews plain glyphs but not color glyphs in the same run.
- In a mixed run, an overlapping plain glyph (tight kerning, negative letter-spacing) paints over a color glyph regardless of glyph order.

Left alone because color fonts virtually always ship their own italic or none at all, mixed runs overlapping is rare, and fixing the ordering means interleaving batched fills with immediate draws for all three glyph kinds at once. Revisit if a real document shows either.

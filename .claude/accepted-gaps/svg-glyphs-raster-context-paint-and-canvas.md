# SVG-in-OpenType glyphs: the raster backend, context paint and the canvas

The PDF path draws a glyph's SVG document (`SvgGlyphPainter`, `SvgGlyphDocument`). Tracked in
[#1419](https://github.com/jhaygood86/PeachPDF/issues/1419), it leaves out:

- **The raster backend** (`RasterGraphics.Text`): an SVG glyph is its plain outline in the text colour there, as COLR glyphs already are.
  Filters, shadows and PDF/A flattening are where that shows.
- **Distinct `context-fill` and `context-stroke`**: both are rewritten to `currentColor` (`SvgGlyphDocument.MakeContextPaintTheTextColour`),
  which is the text's fill colour. The SVG engine has no context-paint notion, and the common uses (a glyph that follows the text colour) work.
- **A fixed canvas** around the glyph origin (`SvgGlyphDocument.Canvas*Ems`): one em left, 1.5 above, two right, half an em below; the
  document's own viewBox is replaced because OpenType SVG puts the glyph origin at (0, 0), not at a corner.
- Animation, and any external resource (only `data:` images resolve: the document is untrusted).

Traps worth knowing: a `fill="var(--color0, red)"` presentation attribute does not resolve `var()` in a cascade, so such attributes are
moved into `style` (`MoveVariableAttributesToStyle`); and the custom-property cascade has to run even when the document has no
stylesheet of its own, because the palette variables sit on the root's `style` attribute (`ImageLoadHandler` only cascades when there is
CSS, which is why a first version drew every palette variable black).

Two decisions from the review of the first version, kept on purpose:

- **`IsColorFont` is true for a CFF font that has an SVG table**, so all of its text is drawn as vector fills (`FillGlyphOutline`), which for a
  glyph without a document goes through the Type 2 interpreter. A glyph whose charstring uses an operator the interpreter does not support
  then draws nothing, where the embedded CFF would have rendered it. COLR-over-CFF is excluded for that reason; SVG fonts are mostly CFF, so
  excluding them would exclude the feature.
- **One bad record makes the whole `SVG ` table ignored** (`SvgGlyphSource.TryCreate`), as does a record out of order: lookup relies on sorted
  ranges, and a font whose table is damaged is not worth guessing at.

What bounds a hostile font: a document inflates to at most 4 MiB, the cache of inflated documents is bounded and keyed by where a document is
(records may share one blob), a document nests at most 48 deep and expands (through `use`) to at most 20,000 elements, only palette entries
the document names are defined, and nested `data:` SVG images are parsed without DTDs.

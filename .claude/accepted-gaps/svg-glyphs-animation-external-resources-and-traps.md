# SVG-in-OpenType glyphs: animation, external resources, and what to know before touching them

A glyph's SVG document is drawn by `SvgGlyphPainter` (on the PDF graphics and on the raster one) from `SvgGlyphDocument`. On purpose it leaves out:

- **Animation** (a PDF has no timeline; OpenType SVG asks for a static rendering, which is what this is).
- **Any external resource** (only `data:` images resolve: the document comes from a font file and is untrusted).
- **A filter's painted region, a marker's content and text** in the extent that sizes the glyph's canvas (`SvgInkExtent`): the canvas is the
  em-based least canvas grown to hold the shapes, so a glyph whose filter region reaches beyond it is clipped there as before. The canvas
  is also never more than `SvgGlyphDocument.MaxCanvasReachEms` (8 ems) from the glyph origin in any direction.
- **The ambient viewport is the canvas, not the em square.** OpenType SVG makes the initial viewport the em square; here it is the canvas, so a
  percentage length (resolved against the least canvas while the tree is built), a `symbol` used with no width and height, and a filter's
  default region are relative to the canvas, which is larger than the em square and, once grown for a glyph, larger than the least canvas. Real
  glyph documents give sizes in font units, so it has not mattered.

The document's canvas: OpenType SVG puts the glyph origin at (0, 0) in font units with y down, and does not clip to the viewport, so the
document's own viewBox is replaced (by the least canvas while the tree is built, so percentages resolve, then by `CanvasFor`).

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

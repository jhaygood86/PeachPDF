# PDF importer (PeachDrawing.PDF `Import/`) and OverlayFile/UnderlayFile

**What landed:** a reader for existing PDFs that paints a page onto any `Canvas` (`PdfPageSource`), and
`IDocumentBuilder.OverlayFile`/`UnderlayFile` on top of it. Delivered in slices: reader (lexer, object parser, xref
tables/streams/object streams, rebuild by scan, page tree, general filters), content-stream interpreter (paths, clips,
graphics state, colour, ExtGState alpha, form XObjects, page rotation), text from embedded sfnt fonts, then the public
surface. Images, shadings, patterns, soft masks, blend modes, Type 1/CFF/Type 3 fonts and stroked text render modes are
reported by `PdfPageSource.Paint` and not drawn yet (images landed in a follow-up: raw samples are unpacked in `PdfImageXObject` and handed to the canvas as PNG, JPEG passes through, and a smoothly-resampled upscale means pixel tests need a tolerance) - these are the next slices, not accepted gaps.

**Load-bearing ideas**
- The importer **re-draws** a page onto a `Canvas` rather than copying objects, so one implementation serves `PdfCanvas`,
  `RasterCanvas` and any third-party canvas. The engine's own object model (`PdfDictionary`, `PdfReference`...) is reused for
  parsing; `PdfReference.Value` loads lazily through `PdfDocument.ImportLoader`. Indirect objects that are plain numbers
  (`/Length 5 0 R`) cannot live in `PdfReference.Value` (it is a `PdfObject`), so everything goes through
  `PdfSourceDocument.Resolve`, never `Elements.GetInteger`.
- **ToUnicode round-trips** because `GlyphPlacement` carries optional `Text` and `PdfCanvas.DrawGlyphs` feeds it to
  `CharacterMapInfo`. Do not draw imported text by re-shaping a string: glyph indexes and text are the importer's to keep.
- Text is drawn by pushing `scale(Th, -1) * Tm` and laying glyphs out upright in canvas space: the page is imported under a
  y-flip (PDF is y-up), and the extra flip puts the glyphs the right way up again. Advances are in pre-`Th` units because the
  pushed matrix applies the horizontal scale.
- **Embedded subsets lack `name`, `OS/2`, `post` and `cmap`**, and the font loader needs all four (the first symptom is
  "Font file does not contain a name table", then NREs further in, in `TypefaceMetrics`). `SfntRepair` synthesizes them in the
  importer; the text engine stays strict. An earlier attempt at making `PeachDrawing.Text` tolerate each in turn was reverted:
  `os2` alone is dereferenced in dozens of places.
- **Patterns are brushes in the current space:** a pattern space is the default space of the stream that uses it, so the
  interpreter tracks the CTM and each stream's base; a shading brush is built with `patternMatrix * streamBase * inverse(CTM)`
  (axial endpoints are transformed; a radial brush takes the matrix). Without that inverse a gradient drawn under a `cm` lands in
  the wrong place - `ShadingPattern_StaysInPatternSpace...` pins it. First showcase round trip: 78 of 429 pages over a 2% pixel
  difference with nothing imported but paths/text/images; patterns took it to 32.
- **Soft masks are groups:** `gs SMask` redirects drawing into a `CreateTile` of the page's size; when the state that set it ends
  (`Q`, `/None` in the same state, or `Finish()` for the base state) the mask form is drawn into a second tile and the two are
  composited with `DrawImageMasked` (luminosity) or `DrawImageAlphaMasked` (alpha) under the inverse of the then-current transform,
  since tiles are in the canvas' own units. Each push records the canvas it went onto, because pushes made while a group is open
  belong to the tile. A `/None` nested deeper than its group is ignored until the group's own state ends. Showcase round trip:
  32 -> 12 pages over a 2% difference, 185 -> 196 clean files.
- File-layer target ranges can use `z`, so a document with layers lays out **all** its pages before drawing the first
  (`PdfGenerator.AddPages`); a document with no layers keeps the one-page-at-a-time path unchanged.

**Evidence:** `PeachDrawing.PDF.Tests/Import/*` (reader, painter pixel tests on a raster canvas, composer-text round trip
comparing ToUnicode) and `PeachPDF.Tests/Integration/FileLayerIntegrationTests` (the finished document is re-imported and
rasterized to check which layer landed on which page and on which side of the content).

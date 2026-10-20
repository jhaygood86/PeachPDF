# PDF importer (PeachDrawing.PDF `Import/`) and OverlayFile/UnderlayFile

**What landed:** a reader for existing PDFs that paints a page onto any `Canvas` (`PdfPageSource`), and
`IDocumentBuilder.OverlayFile`/`UnderlayFile` on top of it. Delivered in slices: reader (lexer, object parser, xref
tables/streams/object streams, rebuild by scan, page tree, general filters), content-stream interpreter (paths, clips,
graphics state, colour, ExtGState alpha, form XObjects, page rotation), text from embedded sfnt fonts, then the public
surface. Images, shadings, patterns, soft masks, blend modes, Type 1/CFF/Type 3 fonts and stroked text render modes are
reported by `PdfPageSource.Paint` and not drawn yet - these are the next slices, not accepted gaps.

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
- File-layer target ranges can use `z`, so a document with layers lays out **all** its pages before drawing the first
  (`PdfGenerator.AddPages`); a document with no layers keeps the one-page-at-a-time path unchanged.

**Evidence:** `PeachDrawing.PDF.Tests/Import/*` (reader, painter pixel tests on a raster canvas, composer-text round trip
comparing ToUnicode) and `PeachPDF.Tests/Integration/FileLayerIntegrationTests` (the finished document is re-imported and
rasterized to check which layer landed on which page and on which side of the content).

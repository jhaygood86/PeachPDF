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
- **Stroked and clipping text** is built from `Typeface.TryGetOutline`: outlines are laid out in text space, carried into user
  space with the run matrix and drawn there (so line width and dashes are user-space), while the glyphs are still drawn - invisibly
  when not filled - to keep the text map. Clip modes accumulate into one path applied at `ET`.
- **Mesh shadings are rasterized:** a canvas has no Gouraud primitive, so `PdfMeshShading` decodes the vertices/patches (types 4-7),
  tessellates patches into 14x14 triangles with bilinear corner colours (Coons interior points per PDF 8.7.4.5.7), fills triangles into
  an RGBA bitmap over the mesh bounding box at 2.5 px per canvas unit, and the interpreter draws it as an image under the shading
  transform, clipped to the filled path for a pattern. Triangle edges get a slack of 0.02 px so neighbours leave no hairline gaps.
- **Encryption** (`PdfDecryptor`): the Standard handler with the empty user password, AES-128/256 only (R4-R6) - the rule is that the
  importer carries no cryptography .NET does not provide, so RC4 files are refused. Revision 4 cannot be checked against /U (that needs
  RC4), so a wrong empty password is detected by AES padding that cannot be right, after reading the first page's content.
  Objects are decrypted as `LoadObject` parses them (strings in place, streams replaced) - so the file key must exist before the first
  object is read, and a rebuild, which scans objects first, clears what it loaded once the key is known. Not decrypted: xref streams,
  the Encrypt dictionary, objects inside an object stream (the stream is), `/Crypt` Identity streams, metadata when the handler leaves it
  clear. Tests use fixtures written by MuPDF, an independent implementation - and `PdfEncryptionOptions.ReadOnly`'s FIRST argument is
  the user password, so `ReadOnly("owner")` is a file that needs a password.
- **Type 3 fonts** run each glyph's content stream under `fontMatrix * size * translate(x, rise) * scale(Th) * Tm`; `d1` locks the
  colour operators (`State.ColorLocked`, popped with the state) so a shape glyph takes the text fill colour, `d0` leaves them free.
  Widths are glyph space times the font matrix's horizontal scale. Type 3 glyphs have no text map yet.
- **CFF fonts** (`Type1C`, `CIDFontType0C`, CID-keyed CFF inside OpenType): the font engine reads CFF outlines only inside an OpenType
  file, so `SfntRepair.FromCff` wraps a bare CFF as OTTO (CFF + head/hhea/maxp/hmtx/cmap, then the name/OS/2/post repair). `CffFont`
  reads names (charset + string index, SIDs below 391 from `PdfGlyphNames.StandardStrings`), the CID map and a custom built-in
  encoding; the synthesized cmap maps each glyph name's Unicode and the built-in encoding at U+F000 + code. A Type0 font over a CID-keyed
  CFF numbers glyphs by CID, so `Decode` goes CID -> glyph through the charset. The expert SIDs (229-390) and Standard/Expert
  predefined encodings are not named; the PDF /Encoding covers the first and Latin-1 the second.
- **Type 1 fonts** (`Type1Font`): `/FontFile` is clear text, an eexec-obfuscated private part (fixed-key arithmetic, not cryptography;
  hex or binary, PFB headers stripped) and charstring-obfuscated Subrs/CharStrings (`lenIV`). The interpreter handles hsbw/sbw, path
  ops, callsubr, `div`, flex (othersubrs 0-2) and hint replacement (3), and `seac` by composing StandardEncoding base/accent glyphs
  (accent offset = composite sbx + adx - asb). `ToCff` re-expresses outlines as Type 2 charstrings (relative moves, width first) in a
  name-keyed CFF with a charset and a custom encoding (glyphs with codes first, since CFF encodings number consecutive glyphs), which
  then follows the CFF path. Hints are dropped. Tests build their own Type 1 fonts byte by byte - there is no Type 1 file in the repo.
- **ICC profiles** (`PdfColorSpace.FromIccProfile`): a gray/RGB `ICCBased` profile that parses through `PeachImage.IccColorProfile` and
  matches its component count converts to sRGB (relative colorimetric, black point compensation) - per colour through `ToColor`, and
  per image through `Bulk`, one call for the whole sample array. Anything else (unreadable, mismatched, CMYK) is read as its device
  alternate. CMYK images are handed to the canvas as an uncompressed CMYK TIFF so a PDF canvas embeds `/DeviceCMYK`; converting them
  to RGB ourselves had no CMYK profile to use and drifted from the original (`cmyk_tiff`). The fixture ICC profile has linear curves,
  which makes "profile applied" observable: 0.5 reads as ~188, not 128. The ICC accepted-gap note is gone with this change.
- **Tiling patterns** use `TileBrush` + `Canvas.CreateTile`: one cell (XStep x YStep, oversampled for a raster canvas) is drawn by a sub-interpreter
  into a tile - pattern space maps to the tile by `[1 0 0 -1 -x0 y0+YStep]`, clipped to the box - and the brush transform is
  `[1 0 0 -1 x0 y0+YStep] * pattern matrix * stream base * inverse(CTM)`. Uncoloured patterns (PaintType 2) get the `scn` colour from the
  `[/Pattern base]` space and lock their colour operators; tiles are cached per (pattern, colour, oversample). Not handled: overlap
  (box larger than the steps) is clipped to the cell.
- **Transfer functions** (`gs TR`/`TR2`) reuse the soft-mask group: what is painted under them goes into a tile, composited with
  `DrawImageWithColorMatrix` and a per-channel matrix. The matrix exists only for a function that is a clamped line on 0..1: `FitLinear`
  fits the unclamped samples and verifies the clamped ends, otherwise "gs TR (not a linear function)". A function array uses its first
  three entries for R, G, B. With this the showcase round trip reports nothing unsupported for any of its 226 files.
- File-layer target ranges can use `z`, so a document with layers lays out **all** its pages before drawing the first
  (`PdfGenerator.AddPages`); a document with no layers keeps the one-page-at-a-time path unchanged.

**Evidence:** `PeachDrawing.PDF.Tests/Import/*` (reader, painter pixel tests on a raster canvas, composer-text round trip
comparing ToUnicode) and `PeachPDF.Tests/Integration/FileLayerIntegrationTests` (the finished document is re-imported and
rasterized to check which layer landed on which page and on which side of the content).

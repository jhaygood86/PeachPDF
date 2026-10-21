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
- **CCITT fax, JBIG2 and JPEG 2000 images** (PeachImage 0.6.0): `PdfImageXObject.DecodeBilevel` decodes the first two to the 1-bit rows the PDF
  filters produce (0 = black unless `/BlackIs1`) and feeds them back through the ordinary 1-bit path, so stencils, `/Decode`, colour spaces and masks
  all work, including a fax/JBIG2 `/Mask` or `/SMask` (`ApplyAlpha`). JBIG2 reads `/JBIG2Globals`. JPX goes through `FromJpx`: decoded to RGBA with
  its own colour (the `/ColorSpace` entry is ignored), alpha forced opaque unless `/SMaskInData` is non-zero. Fixtures were made with Pillow
  (libtiff Group 4 strip; the same bytes as a JBIG2 MMR generic region; openjpeg) and MuPDF renders them identically.
- **Function-based shadings** (type 1) share the mesh path: a bitmap over the domain (at most 512 px a side: every pixel is a function
  evaluation), drawn under `/Matrix * pattern matrix * ...`; a sampled two-input function interpolates bilinearly.
- **Inline images** (`BI`/`ID`/`EI`): `ReadInlineImage` builds an image dictionary (abbreviated keys kept - the image code already reads `W`/`H`/`BPC`/`CS`/`F`/`IM`)
  with the data as its stream and draws it through the ordinary `DrawImage`. The end of the data is exact for an unfiltered image (rows x height from size and
  colour space) because binary data can contain ` EI `; otherwise the first whitespace-delimited `EI` that is followed by text-like bytes. No `EI` = "BI (malformed)".
- **Transparency groups under a constant alpha**: a form XObject with `/Group /S /Transparency` drawn while `ca` < 1 (how the writer emits CSS `opacity`:
  `/GSn gs /Fm Do`, with `/ca 1` set again inside the form) was drawn with the inner alpha only, so the outer alpha vanished - the translucent boxes of the
  `opacity` showcase came back opaque while the pixel share stayed small enough (5%) to pass for MuPDF/PDFium noise. `DrawTransparencyGroup` now draws the form into a
  tile at alpha 1 and composites the tile at the outer alpha (`SoftMaskGroup.Opacity`, composited in `EndSoftMaskGroup`). A blend mode in force when a group form is drawn now also goes through the tile (the tile is composited on the canvas the mode was pushed on, and the group's own state starts at Normal), so it applies to the finished group; a test with overlapping shapes under Multiply tells the two apart. With it all
  430 showcase pages are under the 2% round-trip threshold in both renderers (`css_filter`, `opacity` and `stacking_context` were the three that were not). Lesson: look at the
  diff image, not just the percentage.
- **Navigation carried by file layers**: `PdfPageSource.GetLinks/GetOutline/GetNamedDestinations/GetMetadata/GetAttachments` (`Import/PdfSourceNavigation`) read
  link annotations (`/URI`, `/GoTo`, `/Dest`), the outline, name tree and legacy `/Dests`, `/Info` and `/EmbeddedFiles`; positions go through
  `PdfPagePainter.PageToCanvas` so a rotated page's rectangles and destination tops are in painted terms. `FileLayerPlan` records where each
  source page was stamped and does the transfer in `Finish` (after the last page exists, since a link may point at a page stamped later):
  generated destination names `PeachPDFFile<layer>-<page>-<top>`, outline entries kept for drawn pages plus their ancestors as heading-only
  entries, info/attachments only fill blanks. Attachments are skipped under PDF/A-1/2 (the writer rejects them there). Forms, other
  annotations and tags are not carried yet.
- **Forms and annotation appearances through file layers**: `PdfSourceForms` turns widgets into `PdfFormFieldDefinition`s (inherited `FT`/`Ff`/`V`/`MaxLen`/`Opt`,
  dotted full name, `AS`/`V` for the checked state, the on-state being the one non-Off key of `AP/N`) with an `IPdfFormAppearance` that draws the widget's own
  `/AP /N` stream fitted to the control (`PdfAppearance.Fit`: BBox through Matrix onto Rect, ISO 32000-1 12.5.5). `/Tx BMC` spans are passed to the canvas as
  variable text (`PdfContentInterpreter.MarkVariableText`) so a reader that regenerates the field replaces only the text. Rotated pages: ContentSize is the
  upright size and `AppearanceTransform` is the page matrix's linear part flipped into the writer's y-down convention. Push buttons, signatures and other annotations are
  flattened by `Paint(..., PdfAnnotationPaint.ExceptFormFields)` (the writer has no generic annotation API); list boxes become combo boxes and multi-line is
  lost because `PdfFormBuilder` has no such kinds. `FileLayerPlan.Finish` adds the fields after all pages exist and renames a repeated page's fields through the
  `rename` hook (same answer per name, so a radio group stays one field). MuPDF and PDFium (with `init_forms`) both show the carried field's appearance. Trap:
  a test fixture's radio buttons must have one `/AS /Off`, else both read as selected.
- **Tags through file layers**: `PdfTagBinding` finds a marked-content sequence's structure element through the source's ParentTree (`/StructParents` of the
  page, or of a form XObject; entry index = MCID) and the chain of `/P` parents, then the interpreter opens `OpenPhantomAncestors` + `OpenContentElement` at `BDC`
  and disposes at `EMC`; `/Artifact` BMC/BDC open an artifact. Anything painted outside a structure-bound sequence gets an *implicit artifact*
  (`BeginUntagged` in path paint / text / image / shading, closed at the end of every `Run`) so a tagged target has no untagged content - marked content
  cannot nest, so the implicit one is closed before a `BDC`. Keys are `(per-Paint stamp object, source element)`: a page stamped twice gets two element sets. Source
  `LBody` after `LI` is skipped (the recorder's phantom LI makes its own), an LBody that owns content directly becomes a Span, custom roles go through
  `RoleMap` (unknown ones become Div/Span). Dropped: `/Lang`, `/A` attributes, `/ActualText`, link OBJR ties, source reading order. Trap seen (not investigated): a tagged declarative document with two `Page` handlers and a layer on both pages showed only one
  set of the file's elements - each handler builds its own `StructureTagBuilder`/recorder, which probably replaces the earlier `Document` root kid. The tests stamp one page twice instead.
- **PDF/A, PDF/X and output-intent markers**: read-only `GetConformance` (regex over the XMP `pdfaid:part/conformance` and `pdfxid:GTS_PDFXVersion` or the Info
  `GTS_PDFXVersion`; element and attribute forms) and `GetOutputIntent` (first intent whose `DestOutputProfile` PeachImage parses). `FileLayerPlan.ApplyDocumentInformation`
  adopts the file's `/Lang` into a blank language and the file's output intent for any target that has no profile of its own and is not PDF/A (the writer now emits a plain `/OutputIntents`, subtype `GTS_PDFX`, for a document with a profile and no level, claiming nothing). Deliberately not done: adopting the
  file's PDF/A/PDF/X claim (the target's content is not the file's; the target's config decides), (the subtype of a plain output intent is `GTS_PDFX`: the conformance claim lives in the XMP packet and the Info `GTS_PDFXVersion`, which a document with no level does not get). Fixture trap: `PdfComposerOptions` with PDF/A or PDF/X needs `CreationDate`.
- **Forms and annotations stay real (second pass)**: the writer gained `PdfFormFieldKind.List/PushButton/Signature` (`PdfListBoxField`, `PdfPushButtonField`, `PdfSignatureField`),
  `Multiline`/`MultiSelect` on the definition, and `PdfAnnotationBuilder` + `PdfAnnotationDefinition` (a `PdfGenericAnnotation` with Contents/T/Subj/C/IC/CA/Border/Name, QuadPoints, InkList,
  Vertices, L, flags, and a drawn appearance - coordinates flipped to y-up on the way in). `PdfSourceAnnotations` reads them back (page matrix applied to every point) and
  `FileLayerPlan` now paints the layer page with `PdfAnnotationPaint.None` and adds fields and annotations as objects. Dropped on purpose: actions (a push button is inert; JavaScript and submit
  actions are not something to copy), a signature's value (the file changed, so it would not verify - the field comes across unsigned), annotations with no `/AP` (the writer cannot synthesize
  one) and pop-ups. A real viewer round-trip: MuPDF lists the Highlight annotation and the Button widget, PDFium (`init_forms`) draws both appearances.
- File-layer target ranges can use `z`, so a document with layers lays out **all** its pages before drawing the first
  (`PdfGenerator.AddPages`); a document with no layers keeps the one-page-at-a-time path unchanged.

**Evidence:** `PeachDrawing.PDF.Tests/Import/*` (reader, painter pixel tests on a raster canvas, composer-text round trip
comparing ToUnicode) and `PeachPDF.Tests/Integration/FileLayerIntegrationTests` (the finished document is re-imported and
rasterized to check which layer landed on which page and on which side of the content).

# MathML 3.0 (Presentation) rendering + native PDF 2.0 Associated-File embedding

Added a complete, from-scratch MathML rendering subsystem (`src/PeachPDF/MathML/`) plus the PDF 2.0
plumbing it needed: real `PdfVersion.Pdf20` output, `/AF` (Associated Files, ISO 32000-2 §14.13) on both
`PdfStructureElement` and `PdfCatalog`, and a new `RGraphics.DrawGlyphs` raw-glyph-index paint primitive.
See [docs/architecture.md's MathML Rendering section](../../docs/architecture.md#mathml-rendering) for
the full design and [Supported MathML Features](../../docs/supported-mathml-features.md) for the
compatibility matrix; this note is about what running it (not just reading the code) actually found.

## Load-bearing idea

MathML slots into the exact same "foreign content" architectural seam SVG already established
(`CssBoxSvg`/`ISvgSourceNode`/`SvgTreeBuilder`/`SvgRenderer`), simplified in one real way: MathML's own
attributes (`mathvariant`, `displaystyle`, `scriptlevel`, `stretchy`, ...) are plain XML attributes, not
CSS properties, so no `css-properties.json`/source-generator involvement was needed at all - a
significant scope reduction versus the SVG precedent, confirmed by grepping SVG's actual generator
integration before assuming MathML would need the same. The layout algorithm targets
[MathML Core](https://w3c.github.io/mathml-core/) (the W3C/browser-vendor spec with a fully concrete box
model), not full MathML 3, which leaves layout implementation-defined.

## What was found by running it, not by reading it

**A stretchy fence rendered as invisible blank space, byte-identically across both PDFium and MuPDF
rasterizations**, despite the content stream containing correct-looking `Td`/`Tj` operators for the
chosen `MathVariants` glyph. Root cause: `DrawString`'s existing shaping path registers every glyph it
draws with the embedded font's subsetter via `PdfFont.AddShapedText` before drawing - the new
`RGraphics.DrawGlyphs` primitive (added specifically because MATH-table stretchy-assembly/size-variant
glyphs often have no Unicode codepoint to shape through cmap, so they must be addressed by raw glyph
index) bypassed that registration entirely, so the embedded font subset never actually contained the
glyphs being referenced - a real PDF viewer resolves an unregistered CID to nothing, not an error.
Fixed by calling the existing-but-previously-uncalled `PdfFont.AddShapedGlyph(glyphIndex, sourceText)`
for each glyph in `XGraphicsPdfRenderer.DrawGlyphsAtPositions` before emitting it. This was only found by
actually rasterizing a stretched-parenthesis matrix showcase and looking at it - the content-stream
substring assertions alone (`/ShadingType`-style checks) would have passed with the bug present, exactly
the pitfall CLAUDE.md's testing conventions warn about for PDF graphics-state changes generally, now
concretely reproduced for a text/CID-registration variant of the same class of bug.

**Ground-truth-verified binary parsing.** The OpenType `MATH` table reader (`Fonts/OpenType/MathTable.cs`)
was tested against values extracted independently via Python's `fontTools` library
(`TTFont('StixTwoMath-Regular.ttf')['MATH']`) rather than against the reader's own output - including a
deliberately negative field (`RadicalKernAfterDegree = -335`) specifically to catch a signed/unsigned
read mistake, and a real 3-part glyph assembly (top/extender/bottom parenthesis pieces) with real glyph
IDs, connector lengths, and the `EXTENDER_FLAG` bit. All matched on the first attempt.

## What was deliberately not done, and why

- Full OpenType MATH `GlyphAssembly` construction (building an arbitrarily tall shape from repeated
  parts) - only pre-sized `MathVariants` variants are used, falling back to the largest available one.
  STIX Two Math ships 13 vertical size steps for parentheses, which covers ordinary nesting depths; see
  `.claude/accepted-gaps/mathml-stretchy-operators-no-glyph-assembly.md`.
- The full per-character MathML operator dictionary for `mrow` inter-element spacing - a `form`-based
  (`prefix`/`infix`/`postfix`) simplified default is used instead; see
  `.claude/accepted-gaps/mathml-simplified-operator-spacing.md`.
- `MathKernInfo` (per-glyph sub/superscript corner kerning) - noted directly in `MathTable.cs`'s own file
  header as an intentional parser-level scope cut, not merely deferred silently.
- CSS `width`/`height` overriding `<math>`'s own computed size - `CssLayoutEngine.MeasureIntrinsicSize`
  (the mechanism `<img>`/`<svg>` use for this) was evaluated and rejected: its intrinsic-size contract is
  expressed in CSS-pixel space via `Length.PointsPerPx`, which doesn't cleanly compose with `MathBox`'s
  own already-`PixelsPerPoint`-scaled working-unit geometry without a real risk of a silent
  double-conversion bug - safer to leave as an accepted gap than to introduce one while integrating.
- Content MathML and elementary math (`mstack`/`mlongdiv`) - out of scope per the original plan, confirmed
  still correct after implementation; MathML Core itself dropped elementary math, so there was no
  reference algorithm to implement against even if it had been in scope.

## Evidence

- 10,925 tests pass on `net8.0` (was 10,854 before this change - net +71 new MathML-specific tests across
  `MathTableTests`, `MathAttributeParserTests`, `MathLayoutIntegrationTests`, `MathPaintTests`,
  `MathSmokeTests`, `TaggedPdfMathFormulaTests`, `PdfVersionTests`, `PdfAssociatedFileTests`), zero
  regressions.
- `dotnet build PeachPDF.slnx -t:Rebuild` - zero warnings, zero errors, whole solution (including
  `PeachPDF.Cli`, `PeachPDF.TestHarness`, `PeachPDF.Demo.BlazorWasm`'s referenced projects).
- Four representative formulas (quadratic formula, Pythagorean theorem, a stretchy-parenthesized 2×2
  matrix, a nested-fraction binomial coefficient) rasterized with both PDFium and MuPDF and visually
  compared - byte-for-byte agreement between renderers on the stretchy-fence fix.
- `PeachPDF.TestHarness` runs end-to-end (all 114 showcases, including the new `mathml` one) with no
  exceptions.

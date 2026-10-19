# `PeachDrawing.PDF` gets a public API: `PdfRenderContext`, `PdfCanvas`, `PdfComposer`

Phase 2b of the library extraction. The package is usable on its own: `new PdfComposer(context, options).AddPage(w, h)` returns a `PdfCanvas`,
`Save(stream)` writes the file. Names and the naming rules are in `.claude/invariants/peachdrawing-pdf-public-api-must-not-mirror-a-competitor.md`;
the guide is `docs/peachdrawing-pdf.md`; the reviewed snapshot is `src/PeachDrawing.PDF/PublicApi.txt`.

- **What is public:** `PdfRenderContext` (open for deriving: `OnFontFamilyAdded`, `ResolveVariationAxes`), `PdfCanvas` (constructor `internal`; only a composer makes
  one), `PdfComposer`, `PdfComposerOptions`, and `PdfVersion`/`PdfPermissions`/`PdfEncryptionOptions`. The last three used to be internal mirrors of PeachPDF's public
  types; they are now the package's own public types in `PeachDrawing.PDF` (not `.Engine`), and `EngineOptions` still maps PeachPDF's onto them. The other mirrors
  (`PdfAConformance`, `PdfXConformance`, `ColorOptions`, ...) stay internal until the composer grows those features.
- **Scope of the composer is deliberately small:** metadata, version, stream compression, AES-256 encryption. PDF/A, PDF/X, tags, links, outlines, forms and
  attachments are still driven from PeachPDF through the internals bridge. `PdfGenerator.EstablishDocumentOptions` holds the establishment logic for them; the
  next step is to move it into the package so the composer and `PdfGenerator` share one implementation, then drop `InternalsVisibleTo`.
- **Traps found by running it:**
  - A `PdfCanvas` made with `releaseGraphics: true` reports `IsOffscreenTile` (it doubles as the "paints into a tile" signal), so a page canvas must not own
    its `PdfGraphics`. The composer keeps `(canvas, graphics)` pairs and disposes both in `Save`/`Dispose`; a graphics that is not disposed never closes its
    renderer, and the page has no content.
  - The engine's default `/Producer` is PDFsharp's string, which a public package must not write. The composer sets `PeachDrawing.PDF <version>` from a generated
    constant (`GeneratePdfComposerProductInfo` target in the csproj), the way PeachPDF does for its own.
  - Text strings in the information dictionary are UTF-16BE hex (`/Title <FEFF...>`), and `/CreationDate` carries the machine's offset: the date is converted with
    `.LocalDateTime` first, or the offset mislabels the instant.
- **Evidence:** `PeachDrawing.PDF.Tests` (21 tests; they read the written bytes back and would fail if a feature were a no-op), `PeachPDF.Tests` unchanged (18365), a
  standalone page (fill, text, path) rasterized through both PDFium and MuPDF with matching output, and the API reference generated for the package
  (`-p:DisableDefaultDocumentation=false`). The full `Build-ApiReference.ps1` could not be run locally (Windows path length in `Remove-Item`); the api-reference
  workflow checks the links.

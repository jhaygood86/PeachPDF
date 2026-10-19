# The `InternalsVisibleTo` bridge from `PeachDrawing.PDF` to PeachPDF is gone

The last piece of the library extraction's second phase. PeachPDF and `PeachPDF.Tests` no longer see any `PeachDrawing.PDF` internal: the only
grant is to `PeachDrawing.PDF.Tests`, and `PublicSurfaceTests.TheGrantedInternalsAreExactlyTheDocumentedSet` pins that.

- **What moved into the package** (all behind format-neutral public members; positions are points, top-left origin, y down, and the PDF flip happens
  inside): the document-establishment logic (`PdfComposer.Configure`: version, PDF/A, PDF/X, colour management, encryption, embedded files, Factur-X, XMP,
  document information); pages as `PdfComposerPage` with `AddLink`/`AddDestinationLink`, `Shift` and `Finish`; `AddNamedDestination` and `AddBookmarks`;
  `PdfTagRecorder` (the structure tree, keyed by opaque objects so the HTML side supplies its own boxes); `PdfFormBuilder` (AcroForm fields, with the
  control's look drawn by a host `IPdfFormAppearance`); `PdfCanvas.CreateMeasuring` for laying out without a page.
- **PeachPDF's side is adapters now.** `StructureTagBuilder` and `FormFieldBuilder` translate the HTML core's `IStructureTagSink` and `PageFormField` into
  recorder and builder calls; `PdfGenerator` configures a composer from `PdfGenerateConfig` (`EngineOptions` maps PeachPDF's public option types onto the
  package's, pinned by `EngineOptionsParityTests`). `PeachPdfDocument` wraps a `PdfComposer`; it has no `PdfDocument` any more.
- **Behaviour the composer had to learn from PeachPDF:** `Save` is repeatable (PeachPDF saves a document, then appends pages and saves again), and a page may
  be zero-sized (an empty document still has a page). Both were rejected by the first version of the composer and found by the existing suite.
- **Tests.** The engine and canvas tests moved with the code to `PeachDrawing.PDF.Tests` (`Engine/`, `Backend/`); where a file mixed engine assertions with
  HTML-driven ones it was split (`ShapingCharacterizationTests`, `Format12CmapAstralTests`, `GlyphIdHexLiteralTests`, `PdfDocumentAddPageTests`,
  `PdfEncryptionTests`, `StructureTagBuilderTests` -> `PdfTagRecorderTests`). What stayed in `PeachPDF.Tests` and used to read the in-memory `PdfDocument`
  now reads the *saved bytes* through `TestSupport/PdfInspector.cs` (`doc.Inspect()`): pages, outlines, named destinations, the catalog, the structure tree
  and parent tree, form fields and their appearance streams. That is a stricter check than before - it is what a reader sees - and it needs no engine access.
- **Traps found by running it.**
  - An outline entry's expanded/collapsed state is only in the file as the sign of `/Count`, and only when the entry has children. The old test asserted
    `Opened` on childless entries, which the file cannot represent; it now uses entries with children.
  - Two `Inspect()` calls parse two independent object graphs, so `Assert.Same` across them fails; compare `PdfDict.ObjectNumber` instead.
  - The test-only `TestGraphicsPath` has no arc support, so SVG arc tests that need real curves take their path from a `PdfCanvas.CreateMeasuring` canvas.
  - The `Hash` of ISO 32000-2 Algorithm 2.B used to be called on the writer's own handler by the encryption tests, which made "a file only the writer
    can open" pass; `TestSupport/PdfPasswordHash.cs` is an independent statement of it. The GIF pass-through tests likewise compare against
    `TestSupport/GifLzwReference.cs` rather than the engine's repack routine.
- **Evidence:** `PeachDrawing.PDF.Tests` 954 and `PeachPDF.Tests` 17430 (9 skipped), all passing in Debug and in Release; `PeachPDF.Cli.Tests`, the
  source-generator tests, `PeachDrawing.Core.Tests` and `PeachDrawing.Tests` pass; `dotnet build PeachPDF.slnx -t:Rebuild` is clean.

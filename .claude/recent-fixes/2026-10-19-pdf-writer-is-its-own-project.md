# The PDF writer lives in `PeachDrawing.PDF`, behind a temporary internals bridge

`src/PeachPDF/PDF/` (the PDFsharp-derived engine) and `src/PeachPDF/PdfBackend/` (`PdfCanvas`, `PdfRenderContext`, ...) moved into the new
`src/PeachDrawing.PDF/` project as `Engine/` (namespace `PeachDrawing.PDF.Engine`) and `Backend/` (namespace `PeachDrawing.PDF`). Phase 2a of
the library extraction: nothing is public yet, so the package is not useful on its own until the public canvas API lands.

- **The writer cannot see PeachPDF**, so what it used from the root had to be cut:
  - The option types it reads (`PdfAConformance`, `PdfXConformance`, `PdfVersion`, `ImageCompression`, `PageSize`, `PageOrientation`, `PdfPermissions`,
    `PdfEncryptionOptions`, `ColorOptions` and its three enums) exist twice: PeachPDF's public ones, and internal mirrors in `Engine/`.
    `PeachPDF.Rendering.EngineOptions` maps by cast / property copy, and `EngineOptionsParityTests` fails when either side gains or renumbers a member.
    A new option is added in **both** places. Inside `namespace PeachPDF.*` the public name wins over the mirror, so a test or PeachPDF file that needs the
    engine's value says `PeachDrawing.PDF.Engine.X` or calls `.ToEngine()`.
  - `PdfDocumentOptions.EmbeddingPlan` is `object?` (the plan is PeachPDF's, built from `PdfGenerateConfig`); `PdfGenerator` casts it back.
  - `PdfRenderContext` was split. The base keeps fonts, images, named colors and `PixelsPerPoint`; `PeachPDF.Rendering.HtmlRenderContext` derives from it and adds
    everything that needed HTML/CSS/network knowledge: the generic font families and default font (constructor), `ResolveVariationAxes`
    (`font-variation-settings`), `OnFontFamilyAdded` (default-font adoption), resource loading (`NetworkLoader`, `AllowLocalFileAccess`, `BaseUri`,
    `GetResourceStream`) and `CreateSvgGlyphPainter`. Every PeachPDF/test construction site is now `HtmlRenderContext`.
  - `PdfCanvas` gets its SVG glyph painter from `renderContext.CreateSvgGlyphPainter(this)`; `PdfSvgGlyphPainter` moved to `PeachPDF/Rendering/`.
  - `PeachPDF.Utilities.Utils` was copied as `PeachDrawing.PDF.Units` (plus `PointsPerCssPixel`, replacing `CSS.Length.PointsPerPx`).
- **Internals bridge:** `PeachDrawing.PDF.csproj` grants `InternalsVisibleTo` to `PeachPDF` and `PeachPDF.Tests` (pinned by a test). It is temporary: PeachPDF's
  `PdfGenerator`, `PdfFeatures/` and the engine tests still use `PdfDocument`, `PdfPage`, outlines, annotations and AcroForm objects directly. Removing the grant
  needs a public document-writing API on the package first.
- **The engine tests still live in `PeachPDF.Tests`** (`PDF/`, `PdfBackend/`): about half of them lean on PeachPDF-level fixtures. Moving them to a
  `PeachDrawing.PDF.Tests` project is part of dropping the bridge.
- Evidence: `PeachPDF.Tests` net8.0 Debug and Release, `PeachPDF.SourceGenerators.Tests`, `PeachPDF.Cli.Tests`, `PeachDrawing.Tests` and `PeachDrawing.Core.Tests` all pass,
  and `dotnet build PeachPDF.slnx -t:Rebuild` has zero warnings. A Native AOT publish could not be linked locally (no C++ toolchain); CI's AOT smoke job covers it.

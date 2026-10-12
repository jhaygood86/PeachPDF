# The PDF engine draws with `PeachDrawing.Core` types directly; do not grow parallel types or translation glue

`src/PeachPDF/PDF/` (namespace `PeachPDF.PDF`) is the PDFsharp-derived writer. It used to carry its own
`XColor`/`XPoint`/`XRect`/`XSize`/`XMatrix`/`XBrush`/`XPen`/`XGraphicsPath`/`XFont`/enum twins of the
`PeachDrawing.Core` types, and `GraphicsAdapter` spent most of its lines converting one into the other. All of
that is gone. The writer takes `Rect`, `Size`, `PaintPoint`, `PaintColor`, `Brush` (and its subclasses), `Pen`,
`GraphicsPath`, `TypefaceFont`, `FontPalette` and `System.Numerics.Matrix3x2` as they are. What stays PDF-specific
lives in small internal helpers: `PdfColors`, `PdfPaints`, `PdfMatrix`, `PdfGraphicsPath` (the even-odd default and
`ClipToRect`), `PdfTilingBrush`.

Rules a change here must keep:

- **Gaps go into Core, not into a twin.** If the writer needs something every `Canvas` backend could use, add it to
  `PeachDrawing.Core`'s public API (docs, tests, `PublicApi.txt`, the naming register); PeachPDF has no
  `InternalsVisibleTo` into Core, so a local helper is the only other option. Never re-introduce a mirror type.
- **The semantics that silently differ between a twin and Core** each cost a debugging session when they were
  swapped: `FillMode` is numbered the other way round (an explicit `EvenOdd` default is needed);
  `default(Matrix3x2)` is all zeros, not the identity; `PaintColor.A` is a byte where `XColor.A` was a 0-1 fraction
  (`PdfColors.Opacity()`); `PaintColor.R/G/B` are 0 for a CMYK color; a `Pen`'s dash pattern is absolute where
  `XPen`'s was in multiples of the width; `Matrix3x2` and `Opacity()` are single precision, so a number formatted
  from one must be formatted as a `float` (widening to `double` first changes the last printed digit, e.g.
  `PdfContentWriter.Append<T>` and `PdfEncoders.ToString(Matrix3x2)`).
- **Measure changes against the showcase PDFs.** Render the TestHarness showcases and compare the decompressed
  objects and streams (ignoring the dates, font subset tags, `/ID`, `/NM` and `/M` values). Two runs of the same
  build must be identical before a difference means anything.

## Naming

Readable names, no cryptic abbreviations, in types, members, parameters and locals. The only acronyms allowed are
the vocabulary of the standards being written or of the BCL (PDF, PDF/A, PDF/X, XMP, ICC, CMYK, RGB, CMap, CID,
JPEG, PNG, GIF, URI) and they take .NET casing (`PdfCidFont`, not `PdfCIDFont`; `ObjectId`, not `ObjectID`).
Types are `Pdf*` where a plain name would clash with `PeachDrawing.Core`, `PeachPDF.CSS` or the BCL
(`PdfBrotliEncoder`, not `BrotliEncoder`). The backend types that implement Core's abstractions are
`Pdf<CoreName>` (`PdfCanvas`, `PdfRenderContext`), or `PdfCanvas<CoreName>` where `Pdf<CoreName>` is already a PDF
object (`PdfCanvasFont`, `PdfCanvasImage`); never `*Adapter`. Comments, identifiers and messages are English.

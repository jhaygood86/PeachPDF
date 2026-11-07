# PeachDrawing.PDF.Engine

The PDF writing engine inside PeachDrawing.PDF. It is derived from [PDFsharp](https://github.com/empira/PDFsharp) (MIT, see
[LICENSE.md](LICENSE.md)) and is shaped to what PeachPDF needs: this folder only writes PDF (reading, merging and
overlaying existing PDFs live in the package's `Import/` and `Operations/` folders), and every
type here is `internal`.

| Folder | Namespace | What it holds |
|---|---|---|
| (root) | `PeachDrawing.PDF.Engine` | The object model: `PdfDocument`, `PdfPage`, `PdfDictionary`, `PdfArray`, `PdfString`, `PdfName`, ... |
| `Advanced/` | `PeachDrawing.PDF.Engine.Advanced` | The indirect objects a page needs: images, fonts, shadings, patterns, resources, the cross-reference table |
| `AcroForms/`, `Annotations/`, `Structure/` | `PeachDrawing.PDF.Engine.AcroForms`, `.Annotations`, `.Structure` | Interactive forms, annotations and the tagged-PDF structure tree |
| `Drawing/` | `PeachDrawing.PDF.Engine.Drawing` | `PdfGraphics` (the drawing surface), `PdfForm`, `PdfRasterImage` and the image sources |
| `Rendering/` | `PeachDrawing.PDF.Engine.Rendering` | `PdfGraphicsRenderer`, which turns drawing calls into content-stream operators |
| `Resources/`, `Security/` | (embedded data; `PeachDrawing.PDF.Engine.Security`) | The bundled ICC colour profiles used for PDF/A and PDF/X output intents, and the AES-256 security handler behind encryption |
| `Filters/`, `IO/`, `Internal/` | `.Filters`, `.IO`, `.Internal` | Stream compression, `PdfWriter`, and the encodings and number formatting |

The engine draws with the shared `PeachDrawing.Core` types (`Rect`, `PaintColor`, `Brush`, `Pen`, `GraphicsPath`,
`TypefaceFont`) and `System.Numerics.Matrix3x2` instead of parallel types of its own. The `Backend/` folder next to
this one implements the `PeachDrawing.Core` `Canvas` on top of it.

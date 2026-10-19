# PeachPDF.PDF

The PDF writing engine inside PeachPDF. It is derived from [PDFsharp](https://github.com/empira/PDFsharp) (MIT, see
[LICENSE.md](LICENSE.md)) and has been reshaped to what PeachPDF needs: it only writes PDF, never reads it, and every
type is `internal`.

| Folder | Namespace | What it holds |
|---|---|---|
| (root) | `PeachPDF.PDF` | The object model: `PdfDocument`, `PdfPage`, `PdfDictionary`, `PdfArray`, `PdfString`, `PdfName`, ... |
| `Advanced/` | `PeachPDF.PDF.Advanced` | The indirect objects a page needs: images, fonts, shadings, patterns, resources, the cross-reference table |
| `AcroForms/`, `Annotations/`, `Structure/` | `PeachPDF.PDF.AcroForms`, `.Annotations`, `.Structure` | Interactive forms, annotations and the tagged-PDF structure tree |
| `Drawing/` | `PeachPDF.PDF.Drawing` | `PdfGraphics` (the drawing surface), `PdfForm`, `PdfRasterImage` and the image sources |
| `Rendering/` | `PeachPDF.PDF.Rendering` | `PdfGraphicsRenderer`, which turns drawing calls into content-stream operators |
| `Filters/`, `IO/`, `Internal/` | `.Filters`, `.IO`, `.Internal` | Stream compression, `PdfWriter`, and the encodings and number formatting |

The engine draws with the shared `PeachDrawing.Core` types (`Rect`, `PaintColor`, `Brush`, `Pen`, `GraphicsPath`,
`TypefaceFont`) and `System.Numerics.Matrix3x2` instead of parallel types of its own. The `PdfBackend/` folder next to
this one implements the `PeachDrawing.Core` `Canvas` on top of it.

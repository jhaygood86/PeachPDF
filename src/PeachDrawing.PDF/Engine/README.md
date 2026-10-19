# PeachDrawing.PDF.Engine

The PDF writing engine inside PeachDrawing.PDF. It is derived from [PDFsharp](https://github.com/empira/PDFsharp) (MIT, see
[LICENSE.md](LICENSE.md)) and has been reshaped to what PeachPDF needs: it only writes PDF, never reads it, and every
type is `internal`.

| Folder | Namespace | What it holds |
|---|---|---|
| (root) | `PeachDrawing.PDF.Engine` | The object model: `PdfDocument`, `PdfPage`, `PdfDictionary`, `PdfArray`, `PdfString`, `PdfName`, ... |
| `Advanced/` | `PeachDrawing.PDF.Engine.Advanced` | The indirect objects a page needs: images, fonts, shadings, patterns, resources, the cross-reference table |
| `AcroForms/`, `Annotations/`, `Structure/` | `PeachDrawing.PDF.Engine.AcroForms`, `.Annotations`, `.Structure` | Interactive forms, annotations and the tagged-PDF structure tree |
| `Drawing/` | `PeachDrawing.PDF.Engine.Drawing` | `PdfGraphics` (the drawing surface), `PdfForm`, `PdfRasterImage` and the image sources |
| `Rendering/` | `PeachDrawing.PDF.Engine.Rendering` | `PdfGraphicsRenderer`, which turns drawing calls into content-stream operators |
| `Filters/`, `IO/`, `Internal/` | `.Filters`, `.IO`, `.Internal` | Stream compression, `PdfWriter`, and the encodings and number formatting |

The engine draws with the shared `PeachDrawing.Core` types (`Rect`, `PaintColor`, `Brush`, `Pen`, `GraphicsPath`,
`TypefaceFont`) and `System.Numerics.Matrix3x2` instead of parallel types of its own. The `Backend/` folder next to
this one implements the `PeachDrawing.Core` `Canvas` on top of it.

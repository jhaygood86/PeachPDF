# PeachDrawing.PDF

A PDF `Canvas` for [PeachDrawing.Core](https://www.nuget.org/packages/PeachDrawing.Core/): draw shapes, text, images, gradients
and effects with the same drawing API every PeachDrawing target shares, and write them as PDF. Fonts are embedded and subset,
and the writer covers PDF/A and PDF/X conformance, encryption and tagged output, in pure .NET with no browser or external
process. It is the PDF writer [PeachPDF](https://peachpdf.net/) renders HTML and CSS through.

The PDF engine inside this package is derived from [PDFsharp](https://github.com/empira/PDFsharp) (MIT) and is not part of the
public API; what you program against is the PeachDrawing drawing surface.

```bash
dotnet add package PeachDrawing.PDF
```

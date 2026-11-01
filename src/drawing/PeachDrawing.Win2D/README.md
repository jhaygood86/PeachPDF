# PeachDrawing.Win2D

A Win2D backend for [PeachDrawing.Core](https://www.nuget.org/packages/PeachDrawing.Core/): draw shapes, gradients, clips, layers,
text and images onto a `Win2dCanvas` - hardware-accelerated Direct2D, into a drawing session you already have (a `CanvasControl`, a
swap chain) or into an offscreen bitmap you read back or save - with no HTML, CSS or PDF involved. It is the sibling of the PDF writer
(`PeachDrawing.PDF`), the SVG writer (`PeachDrawing.Svg`) and the raster canvas (`PeachDrawing`), and it records links so a host can make
them clickable and open them with the system's default handler.

```bash
dotnet add package PeachDrawing.Win2D
```

**Status:** pre-1.0. Windows only (`net10.0-windows10.0.19041.0`), on Win2D 1.4 for the Windows App SDK. It is versioned in lockstep with
PeachPDF (the same number for every release); until 1.0 the API may change between releases.

```csharp
using PeachDrawing.Core;
using PeachDrawing.Win2D;

using var context = Win2dRenderContext.CreateSoftware();
using var composer = new Win2dComposer(context, widthPoints: 400, heightPoints: 200);

composer.Canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 30, 144, 255)), 20, 20, 200, 100);

await using var file = File.Create("out.png");
await composer.SaveAsync(file);
```

Every native Win2D object the package creates has one owner and is released deterministically: a session or device you pass in is never
disposed, one the package makes is disposed with its owner, and a canvas disposed with clips or layers still open unwinds them. See
[the guide](https://peachpdf.net/peachdrawing-win2d.html) for the full lifecycle table, the text engines (`PeachDrawing.Text` or DirectWrite),
links, and the limitations.

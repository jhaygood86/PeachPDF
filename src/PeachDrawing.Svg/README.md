# PeachDrawing.Svg

A vector SVG backend for [PeachDrawing.Core](https://www.nuget.org/packages/PeachDrawing.Core/): draw shapes, gradients,
clips, text and images onto an `SvgCanvas` and save the result as an SVG document, with no HTML, CSS or PDF involved. It is
the sibling of the PDF writer (`PeachDrawing.PDF`) and the raster canvas (`PeachDrawing`), and keeps everything SVG can
express as vector content: paths keep their curves, gradients stay gradients, clips stay clips, glyphs are exact outlines
(or selectable `<text>` with an embedded subset font on request), and PNG/JPEG/GIF/WebP images are embedded as they came.

```bash
dotnet add package PeachDrawing.Svg
```

**Status:** pre-1.0. `SvgRenderContext`, `SvgComposer` and `SvgCanvas` are the package's public surface, and it is versioned
in lockstep with PeachPDF (the same number for every release); until 1.0 the API may change between releases.

```csharp
using PeachDrawing.Core;
using PeachDrawing.Svg;

var context = new SvgRenderContext();
var composer = new SvgComposer(context, widthPoints: 400, heightPoints: 200);

var canvas = composer.Canvas;
canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 30, 144, 255)), 20, 20, 200, 100);

await using var file = File.Create("out.svg");
composer.Save(file);
```

Units are points with the origin at the top left and y growing downwards, as in `PeachDrawing.PDF`. The only content that is
not vector is what SVG cannot express: an effect a caller asks for as a bitmap (`BeginRasterSurface`) is embedded as a PNG.

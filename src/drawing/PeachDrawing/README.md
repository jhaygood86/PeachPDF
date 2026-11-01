# PeachDrawing

A standalone CPU software rasterizer for .NET: draw shapes, text and images onto a `RasterCanvas` and save the result as a
PNG or any other [PeachImage](https://www.nuget.org/packages/PeachImage)-supported format, with no HTML, CSS or PDF involved.
It is the concrete `Canvas`/`RenderContext` implementation of
[PeachDrawing.Core](https://www.nuget.org/packages/PeachDrawing.Core/), usable with no [PeachPDF](https://peachpdf.net/)
reference at all, and it is the same implementation PeachPDF uses as its raster fallback for effects PDF cannot express as
vector content (filters, blurred shadows, backdrop effects). It depends only on `PeachDrawing.Core` and `PeachImage`, and
is trimmable and Native AOT compatible.

```bash
dotnet add package PeachDrawing
```

**Status:** pre-1.0. `RasterCanvas`, `RasterRenderContext` and `RasterSurface` are the package's public surface, and it is
versioned in lockstep with PeachPDF (the same number for every release); until 1.0 the API may change between releases.

```csharp
using PeachDrawing;
using PeachDrawing.Core;
using PeachImage.Formats.Png;

var ctx = new RasterRenderContext();
using var canvas = ctx.CreateCanvas(widthPx: 800, heightPx: 600);

canvas.DrawRectangle(ctx.GetSolidBrush(PaintColor.FromArgb(255, 30, 144, 255)), x: 50, y: 50, width: 200, height: 100);

var font = ctx.GetFont("Arial", size: 24, PaintFontStyle.Regular);
if (font is not null)
{
    canvas.DrawString("Hello, PeachDrawing", font, PaintColor.Black, new PaintPoint(50, 200), new Size(700, 40));
}

await using var file = File.Create("out.png");
await canvas.SaveAsync(file, "png", new PngEncoderOptions());
```

A canvas from `CreateCanvas` has the simplest possible unit convention: one user-space unit is one device pixel. The
constructor of `RasterRenderContext` registers every font installed on the machine, and `AddFont` registers a TrueType,
OpenType, WOFF or WOFF2 font from a stream under a family name.

What `RasterCanvas` draws:

- rectangles, polygons and arbitrary paths, filled or stroked (dashes, caps, joins, miter limits), with solid, linear,
  radial and conic gradient brushes, tile and hatch brushes;
- rectangular and arbitrary-path clips, affine transforms, anti-aliasing per call, and the 16 PDF/CSS blend modes;
- shaped text, including COLR/CPAL colour glyphs, CBDT/CBLC and sbix bitmap glyphs, grid-fitted (hinted) rendering through
  `PeachDrawing.Text`, and text on a path;
- images with opacity, blend-mode compositing, colour-matrix transforms, alpha and luminosity masking, and bicubic or
  pixelated sampling;
- layers with blur, drop-shadow and colour-matrix effects, and path boolean operations through `PeachDrawing.Core`.

A standalone `RasterCanvas` has no SVG engine, so an OpenType glyph defined by an SVG document falls back to its plain
outline. `ToPixelBuffer()` hands you the premultiplied RGBA pixels without a copy if you want them in-process rather than
in a file.

See [the PeachDrawing guide](https://peachpdf.net/peachdrawing.html) for the full tour.

## Sponsorship

PeachPDF is free and open source. If it saves you time, you can [sponsor jhaygood86 on GitHub](https://github.com/sponsors/jhaygood86);
sponsors can get paid support, see [Sponsorship](https://peachpdf.net/sponsorship.html).

## Licence

BSD 3-Clause (see `LICENSE`).

# PeachDrawing.Core

The drawing-surface abstraction [PeachPDF](https://peachpdf.net/) targets to write PDF, published as its own package so
other renderers can target it too: a `Canvas` to draw shapes, text and images on, a `RenderContext` to resolve colours,
fonts and paint objects from, and the paint-ready value types (`Brush`, `Pen`, `GraphicsPath`, `PaintColor`, `Rect` and the
rest) that connect them. Its only dependency is [PeachDrawing.Text](https://www.nuget.org/packages/PeachDrawing.Text/), for
font and typeface types. It is trimmable and Native AOT compatible.

```bash
dotnet add package PeachDrawing.Core
```

**Status:** pre-1.0. Every type in the package is documented public API, and it is versioned in lockstep with PeachPDF (the
same number for every release); until 1.0 the API may change between releases. Two implementations of `Canvas` exist today:
PeachPDF's own, which writes PDF, and [PeachDrawing](https://www.nuget.org/packages/PeachDrawing/), a standalone CPU
software rasterizer you can use with no PeachPDF reference at all.

What is in it:

- **`Canvas`:** clip, transform and blend-mode stacks, and drawing of rectangles, polygons, paths, lines, shaped text and
  images (with opacity, blending, colour-matrix and mask variants), plus layers with blur, drop-shadow and colour-matrix
  effects, tiles for content drawn once and reused, and tagged-PDF structure hooks that a backend with no such notion can
  ignore.
- **`RenderContext`:** the per-render factory that resolves fonts (installed, `@font-face`-style URLs and local faces, with
  per-codepoint CSS Fonts 4 fallback) and builds brushes and pens, including linear, radial and conic gradients.
- **Plain-data paint objects:** `Brush`, `Pen` and `GraphicsPath` are readable data rather than backend-owned handles, so a
  `Canvas` you write yourself can interpret what another implementation built without reaching into its internals.
- **Colour glyphs and paragraphs:** a walker that paints COLR v0/v1 colour glyphs onto any `Canvas`, and extension methods
  that draw a `PeachDrawing.Text` paragraph layout or shaped glyph run on any `Canvas`.

```csharp
using PeachDrawing.Core;

// Any concrete Canvas / RenderContext: PeachPDF's, PeachDrawing's RasterCanvas, or your own.
canvas.PushClip(new Rect(0, 0, 200, 100));
canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 30, 144, 255)), x: 10, y: 10, width: 180, height: 80);
canvas.DrawString("Hello", font, PaintColor.Black, new PaintPoint(20, 40), new Size(160, 20));
canvas.PopClip();
```

See [the PeachDrawing.Core guide](https://peachpdf.net/peachdrawing-core.html) for the full tour, including how to implement
your own `Canvas`.

## Sponsorship

PeachPDF is free and open source. If it saves you time, you can [sponsor jhaygood86 on GitHub](https://github.com/sponsors/jhaygood86);
sponsors can get paid support, see [Sponsorship](https://peachpdf.net/sponsorship.html).

## Licence

BSD 3-Clause (see `LICENSE`).

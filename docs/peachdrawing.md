# PeachDrawing

`PeachDrawing` is a standalone CPU software rasterizer: a concrete `Canvas`/`RenderContext` implementation
of [PeachDrawing.Core](peachdrawing-core.md), usable with **no PeachPDF reference at all**.
Draw shapes, text and images onto a `RasterCanvas` and save the result as a PNG or any other
[PeachImage](https://www.nuget.org/packages/PeachImage)-supported format - no HTML, no CSS, no PDF involved
anywhere. It depends only on `PeachDrawing.Core` and `PeachImage`, is trimmable and Native AOT
compatible, and is versioned in lockstep with PeachPDF: the same version number for every release.

```bash
dotnet add package PeachDrawing
```

> **Status: pre-1.0.** `RasterCanvas`, `RasterRenderContext` and `RasterSurface` are the package's full
> public surface today - see `PublicApi.txt` for the exact, machine-checked snapshot. PeachPDF is the
> package's other consumer: `GraphicsAdapter.BeginRasterSurface` drives this exact `RasterCanvas`
> implementation as its own raster fallback for effects PDF can't express as vector content (`filter:
> blur()`, `backdrop-filter`, and the like) - this is not a copy built for PeachPDF's benefit, it is the one
> implementation both PeachPDF and a standalone caller use. Until 1.0, the public API may change between
> releases.

## Quick start

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

Everything on the right of `.` above resolves inside `PeachDrawing`/`PeachDrawing.Core` alone - no
`PeachPDF` project reference anywhere in the chain.

## `RasterRenderContext`

`RasterRenderContext` is a directly-constructible `RenderContext`: its constructor registers every font
installed on the machine (by name, under `FontSet.InstalledFamilyNames`), so `GetFont`/`GetFontForCodepoint`
(both inherited from `RenderContext` - see [RenderContext: colors, fonts and paint
objects](peachdrawing-core.md#rendercontext-colors-fonts-and-paint-objects)) work immediately with
no setup. Unlike PeachPDF's own `PdfSharpAdapter`, it does **not** alias CSS generic family names
(`Helvetica`→`Arial`, `system-ui`, math-font resolution) - a standalone caller names a real family directly,
since there is no CSS cascade here to resolve a generic keyword against.

- **`CreateCanvas(widthPx, heightPx, dpi = 96)`** is the entry point: a `RasterCanvas` of exactly that many
  device pixels, with the plain unit convention below. `dpi` is accepted for forward-compatible signature
  parity with a future encoder that tags physical resolution on save; it does not otherwise affect drawing
  today.
- **`AddFont(Stream, fontFamilyName)`** registers a font (TrueType, OpenType, WOFF or WOFF2 - recognised by
  its content) directly under a family name, bypassing CSS `@font-face` entirely - the standalone
  equivalent of `RenderContext.AddFontFamilyFromUrl`.

## `RasterCanvas`'s unit convention: plain device pixels

`RasterCanvas` has no built-in notion of a PDF point or a CSS layout unit. A canvas from `CreateCanvas` has
the simplest possible contract: **one user-space unit is one device pixel.** `new Rect(0, 0, 100, 50)` and a
font `size` of `24` both mean exactly that many pixels, regardless of `dpi`. This is deliberately different
from how PeachPDF's own vector PDF output interprets a `Canvas`'s coordinates (points, `1/72` inch each);
a `Canvas` implementation chooses its own unit convention, and `Canvas.PixelsPerPoint` reports the scale between its units and true
PDF points, which is worth checking if you are implementing your own `Canvas` and comparing conventions.

Everything else `RasterCanvas` draws with - `DrawRectangle`, `DrawPolygon`, `DrawPath`, `DrawLine`,
`DrawString`, `DrawGlyphs`, `DrawImage` and its blended/masked/color-matrix variants, `PushClip`/
`PushTransform`/`PushBlendMode`, `CreateTile` - is exactly the `Canvas` surface documented in
[Canvas: the drawing surface](peachdrawing-core.md#canvas-the-drawing-surface); nothing about it is
raster-specific to learn separately.

## Exporting: `ToPixelBuffer`, `Save`, `SaveAsync`

- **`RasterCanvas.ToPixelBuffer()`** returns a `PixelBuffer` (premultiplied RGBA8, no copy) - what you want
  when you're handing the drawn pixels to something else in-process rather than encoding a file.
- **`RasterCanvas.Save(Stream, formatName, EncoderOptions)`** / **`SaveAsync(...)`** encode through
  [PeachImage](https://www.nuget.org/packages/PeachImage) directly: any format PeachImage supports (`"png"`
  with `PngEncoderOptions`, and whatever else that package's own format list covers) works here, not just
  PNG - this is a thin, intentional pass-through, not new encoding logic.

## Feature coverage

`RasterCanvas` implements the entire `Canvas` surface - rectangles, polygons, arbitrary vector paths (fill
and stroke, with the usual dash/cap/join/miter controls), solid and gradient (linear, radial, conic)
brushes, clipping (rectangular and arbitrary-path), affine transforms, the 16 PDF/CSS blend modes, shaped
text (including COLR/CPAL colour glyphs - v0 layers and the v1 paint graph with solid, linear, radial and sweep gradients, transforms and blend-mode composites, honouring `font-palette` overrides - `SVG ` table glyphs falling back to their plain outline - see
below - and CBDT/CBLC/sbix bitmap glyphs), and images (opacity, blend-mode compositing, colour-matrix
transforms, alpha and luminosity masking). Grid-fitted (hinted) text rendering is supported through
`PeachDrawing.Text`'s own hinting engine.

One capability gap, permanent rather than a gap to be closed: **a standalone `RasterCanvas` has no SVG
rendering engine of its own**, so an OpenType font glyph defined by an embedded SVG document (the `SVG `
table) falls back to its plain glyph outline instead of the SVG artwork - rendering an arbitrary SVG
document is not a generic raster primitive, and `PeachPDF`'s own SVG engine is inextricably tied to its
HTML/CSS/SVG layers. When PeachPDF drives a `RasterCanvas` as its own raster fallback, it supplies its SVG
engine through an internal hook (`RenderContext.CreateSvgGlyphPainter`), so `SVG `-table glyphs render fully
correctly there; a standalone caller sees the same plain-outline fallback a font with no `SVG` table at all
would get.

## Layers, effects, stroke outlines and sampling

`RasterCanvas` draws layers natively (`BeginLayer`, see [Layers and effects](peachdrawing-core.md#layers-and-effects))
and applies `BlurEffect`, `DropShadowEffect` and `ColorMatrixEffect` to them; `RasterLayerEffects.Apply` and
`GetInkMargin` do the same on a `RasterSurface` for a canvas of your own. `PathStroker.Stroke` turns the line a `Pen`
would draw into a fillable outline. Per-call anti-aliasing (`PushAntiAlias`), the `Bicubic` and `Pixelated` image
samplings, tile and hatch brushes, and `GetInkCrossings` for text all work on a `RasterCanvas`.

## Implementing your own `Canvas` instead

If you need a different rendering target entirely - a hardware-accelerated backend, an SVG writer, a
different image library - implement `Canvas`/`RenderContext` directly against `PeachDrawing.Core`
rather than using this package. See [Implementing your own
Canvas](peachdrawing-core.md#implementing-your-own-canvas); `RasterCanvas`'s own source is a
complete, real second worked example alongside PeachPDF's `GraphicsAdapter`.

## Licences

`PeachDrawing` is BSD 3-Clause. It carries no third-party code of its own; its dependencies
(`PeachDrawing.Core`, `PeachDrawing.Text`, `PeachImage`) carry their own notices. See
[License](license.md) for the whole list.

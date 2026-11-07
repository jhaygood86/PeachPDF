# PeachDrawing.Core

The drawing-surface abstraction [PeachPDF](https://peachpdf.net/) targets to write PDF, published as its own package so other
renderers can target it too: a `Canvas` to draw shapes, text and images on, a `RenderContext` to resolve colours, fonts and paint
objects from, and the paint-ready value types (`Brush`, `Pen`, `GraphicsPath`, `PaintColor`, `Rect` and the rest) that connect
them. Write your drawing code once against `Canvas` and run it on any backend. Its only dependency is
[PeachDrawing.Text](https://www.nuget.org/packages/PeachDrawing.Text/), for font and typeface types. It is trimmable and Native AOT
compatible.

```bash
dotnet add package PeachDrawing.Core
```

**Status:** pre-1.0, versioned in lockstep with PeachPDF (the same number for every release); until 1.0 the API may change
between releases. Targets .NET 10 and later.

You normally install this package together with a backend that implements it:

| Backend package | Draws to | `Canvas` / `RenderContext` |
|---|---|---|
| [PeachDrawing.PDF](https://www.nuget.org/packages/PeachDrawing.PDF/) | PDF files | `PdfCanvas` / `PdfRenderContext` |
| [PeachDrawing](https://www.nuget.org/packages/PeachDrawing/) | bitmaps (CPU rasterizer), PNG/JPEG/WebP... | `RasterCanvas` / `RasterRenderContext` |
| [PeachDrawing.Svg](https://www.nuget.org/packages/PeachDrawing.Svg/) | vector SVG documents | `SvgCanvas` / `SvgRenderContext` |
| [PeachDrawing.Skia](https://www.nuget.org/packages/PeachDrawing.Skia/) | any `SKCanvas`, CPU or GPU, any platform | `SkiaCanvas` / `SkiaRenderContext` |
| [PeachDrawing.Win2D](https://www.nuget.org/packages/PeachDrawing.Win2D/) | Win2D on Windows | `Win2dCanvas` / `Win2dRenderContext` |
| [PeachDrawing.CoreGraphics](https://www.nuget.org/packages/PeachDrawing.CoreGraphics/) | a `CGContext` on macOS and iOS | `CoreGraphicsCanvas` / `CoreGraphicsRenderContext` |

## Quick start: draw once, target any backend

```csharp
using PeachDrawing.Core;

// Works on every backend above; the canvas and context are whatever the backend gave you.
static void Paint(Canvas canvas, RenderContext context)
{
    var font = context.GetFont("Arial", size: 24, PaintFontStyle.Regular);

    canvas.PushClip(new Rect(0, 0, 200, 100));
    canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 30, 144, 255)), x: 10, y: 10, width: 180, height: 80);
    if (font is not null)
    {
        canvas.DrawString("Hello", font, PaintColor.Black, new PaintPoint(20, 40), canvas.MeasureString("Hello", font));
    }
    canvas.PopClip();
}
```

For example, with the PDF backend: `Paint(composer.AddPage(400, 300).Canvas, context)`; with the bitmap one:
`Paint(context.CreateCanvas(400, 300), context)`.

## `RenderContext`: colours, fonts and paint objects

```csharp
Font? font = context.GetFont("Source Sans", size: 16, PaintFontStyle.Bold);       // CSS Fonts 4 face matching
Font? emoji = context.GetFontForCodepoint("Arial", 16, PaintFontStyle.Regular, new System.Text.Rune(0x1F600));   // per-codepoint fallback

await context.AddFontFromBytes("Brand", File.ReadAllBytes("Brand.ttf"));          // or AddFontFamilyFromUrl, AddLocalFontFamily

Brush solid = context.GetSolidBrush(PaintColor.FromArgb(255, 231, 76, 60));
Brush linear = context.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(200, 0), [(PaintColor.White, 0), (PaintColor.Black, 1)]);
Pen pen = context.GetPen(PaintColor.Black);
pen.Width = 2;
```

Each backend's context adds its own `AddFont(stream, family)`. Gradients can be linear, radial (with a focal point) or conic, and
repeat. A `Pen` strokes with any brush and carries width, caps, joins, miter limit and dashes.

## `Canvas`: the drawing surface

```csharp
canvas.PushTransform(System.Numerics.Matrix3x2.CreateRotation(float.Pi / 6));       // transform, clip and blend mode are stacks:
canvas.PushClip(new Rect(0, 0, 200, 100));                                          // what you push, you pop, in order
canvas.PushBlendMode(PaintBlendMode.Multiply);

canvas.DrawRectangle(brush, 10, 10, 180, 80);
canvas.DrawLine(pen, 0, 0, 200, 100);
canvas.DrawImage(image, new Rect(20, 20, 64, 64));                                  // + opacity, blend, colour-matrix and mask variants

canvas.PopBlendMode();
canvas.PopClip();
canvas.PopTransform();
```

- **Vector paths:** `using var path = canvas.GetGraphicsPath(); path.Start(0, 0); path.LineTo(100, 0); path.AddBezierTo(...); path.CloseFigure();`
  then `DrawPath(brush, path)`, `DrawPath(pen, path)` or `PushClip(path)`. `path.Flatten(tolerance)` and `GetCurveContours()` read the geometry back;
  `PathOperations` unions, intersects and subtracts paths, `PathMeasure` samples them, `PathShapes` builds rounded rectangles and ellipses.
- **Text:** `DrawString` draws a shaped run (ligatures, kerning, complex scripts); `MeasureString`, `CountShapedGlyphs`, `GetTextOutline` and
  `GetInkCrossings` measure against the same shaping. `DrawStringInRect` aligns a run in a rectangle by `BoxAlignment`. `DrawGlyphs` draws glyphs you
  shaped yourself with `PeachDrawing.Text`.
- **Layers and effects:** `canvas.BeginLayer(new LayerOptions(Opacity: 0.5, Effects: [new BlurEffect(4)]))` draws a group that is composited as one piece,
  with blur, drop-shadow and colour-matrix effects.
- **Tiles:** `CreateTile(width, height)` returns a canvas and image for content drawn once and reused (an SVG pattern cell, say).
- **Links, cursors and semantics:** `AddLink`, `AddDestinationLink`, `AddDestination`, `AddCursor` and `AddSemanticRegion` say what is clickable and what
  things mean; a canvas of pixels ignores them, PDF writes annotations, SVG writes `<a>`, and the Skia, Win2D and CoreGraphics backends record them for a host to hit-test.
- **Structure hooks:** `BeginArtifact` and `BeginMarkedContent` bracket tagged-PDF content; other backends ignore them.

## Paragraphs and colour glyphs on any Canvas

```csharp
using PeachDrawing.Text.Layout;

var layout = new ParagraphBuilder(new RunStyle(typeface, 16))
    .AddText("Hello, ").PushRun(new RunStyle(boldTypeface, 16)).AddText("world").PopRun()
    .Build().Layout(availableWidth: 300);

canvas.DrawParagraph(layout, new PaintPoint(10, 10), PaintColor.Black);             // glyphs exactly where the shaper put them
```

`ColorGlyphPainter` walks COLR v0/v1 colour glyphs into an `IColorGlyphTarget`; `CanvasColorGlyphTarget` paints them on any `Canvas`.

## Implementing your own Canvas

`Brush`, `Pen` and `GraphicsPath` are plain, readable data rather than backend-owned handles, so a `Canvas` you write can interpret
what another implementation built (pattern-match on `SolidBrush`, `LinearGradientBrush`, ... and read `path.Flatten`). Derive from `Canvas` and
`RenderContext`, implement the abstract members, and override the virtual ones you can do better than the default (layers, links,
tiles). The guide has the full list and the contracts, such as units (`PixelsPerPoint`) and how raster fallbacks are requested.

## More

- [The PeachDrawing.Core guide](https://peachpdf.net/peachdrawing-core.html): every type, layers and retained layers, tile and hatch brushes, anti-aliasing and sampling, implementing a backend.
- [API reference](https://peachpdf.net/api/PeachDrawing.Core/index.html)

## Sponsorship

PeachPDF is free and open source. If it saves you time, you can [sponsor jhaygood86 on GitHub](https://github.com/sponsors/jhaygood86);
sponsors can get paid support, see [Sponsorship](https://peachpdf.net/sponsorship.html).

## Licence

BSD 3-Clause (see `LICENSE`).

# PeachDrawing

A standalone CPU software rasterizer for .NET: draw shapes, text and images onto a `RasterCanvas` and save the result as a
PNG, JPEG, WebP or any other [PeachImage](https://www.nuget.org/packages/PeachImage)-supported format, with no HTML, CSS or PDF
involved. It is the concrete bitmap implementation of the [PeachDrawing.Core](https://www.nuget.org/packages/PeachDrawing.Core/)
`Canvas`/`RenderContext`, usable with **no [PeachPDF](https://peachpdf.net/) reference at all**, and it is the same
implementation PeachPDF uses as its raster fallback for effects PDF cannot express as vector content (filters, blurred shadows,
backdrop effects). It runs everywhere .NET runs (no native code), depends only on `PeachDrawing.Core` and `PeachImage`, and is
trimmable and Native AOT compatible.

```bash
dotnet add package PeachDrawing
```

**Status:** pre-1.0, versioned in lockstep with PeachPDF (the same number for every release); until 1.0 the API may change
between releases. Targets .NET 10 and later.

## Quick start

```csharp
using PeachDrawing;
using PeachDrawing.Core;
using PeachImage.Formats.Png;

var ctx = new RasterRenderContext();                 // registers every font installed on this machine
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

A canvas from `CreateCanvas` has the simplest possible unit convention: one user-space unit is one device pixel, and the origin is
the top-left corner. To use your own font instead of an installed one, `await ctx.AddFont(stream, "My Family")` (TrueType,
OpenType, WOFF or WOFF2) before asking for it.

## Saving or reading the pixels

```csharp
using PeachImage.Formats.Jpeg;
using PeachImage.Formats.Webp;

await using (var jpg = File.Create("out.jpg"))
    await canvas.SaveAsync(jpg, "jpeg", new JpegEncoderOptions { Quality = 85 });
await using (var webp = File.Create("out.webp"))
    await canvas.SaveAsync(webp, "webp", new WebpEncoderOptions());

PixelBuffer pixels = canvas.ToPixelBuffer();         // premultiplied RGBA8, no copy: hand it to your own code
```

`SaveAsync` is a pass-through to PeachImage, so any format it can encode (PNG, JPEG, GIF, WebP, AVIF, BMP, JPEG XL) works.

## Shapes, paths, gradients and strokes

```csharp
var gradient = canvas.GetRadialGradientBrush(new PaintPoint(150, 150), 100, 100,
    [(PaintColor.White, 0), (PaintColor.FromArgb(255, 200, 30, 60), 1)]);

using var path = canvas.GetGraphicsPath();
path.Start(50, 300); path.LineTo(250, 300); path.AddBezierTo(300, 300, 300, 400, 250, 400); path.CloseFigure();
canvas.DrawPath(gradient, path);

var pen = canvas.GetPen(PaintColor.Black);
pen.Width = 3;
pen.SetDashPattern([6, 3], offset: 0);               // dashes, caps, joins and miter limits are all on Pen
canvas.DrawPath(pen, path);
```

Brushes can be solid, linear, radial or conic gradients, tiles or hatches; fills and strokes both work on any `GraphicsPath`.

## Clips, transforms and blend modes

```csharp
canvas.PushClip(new Rect(0, 0, 200, 100));                                 // or a GraphicsPath
canvas.PushTransform(System.Numerics.Matrix3x2.CreateRotation(0.3f));
canvas.PushBlendMode(PaintBlendMode.Multiply);                             // the 16 PDF/CSS blend modes
canvas.DrawRectangle(gradient, 20, 20, 160, 60);
canvas.PopBlendMode();
canvas.PopTransform();
canvas.PopClip();
```

## Text

Text is shaped by [PeachDrawing.Text](https://www.nuget.org/packages/PeachDrawing.Text/): ligatures, kerning, mark attachment,
Arabic and Indic scripts, bidirectional text, COLR/CPAL color glyphs and bitmap glyphs, with grid-fitted rendering. `canvas.MeasureString`
measures with the same shaping, and `canvas.DrawParagraph(layout, origin, color)` paints a laid-out
`PeachDrawing.Text` paragraph. A standalone canvas has no SVG engine, so an OpenType glyph defined by an SVG document falls back to
its plain outline.

## Images

```csharp
using var stream = File.OpenRead("photo.jpg");
var image = ctx.ImageFromStream(stream);             // any PeachImage format; animated images keep every frame

canvas.DrawImage(image, new Rect(0, 0, 320, 240));
canvas.DrawImageWithOpacity(image, new Rect(340, 0, 320, 240), opacity: 0.5);
canvas.DrawImageWithColorMatrix(image, new Rect(0, 260, 320, 240), ColorMatrix.Identity);
```

Image variants cover opacity, blend modes, color matrices, alpha and luminosity masks, and bicubic or pixelated sampling.

## Layers and effects

```csharp
var effects = new LayerEffect[] { new BlurEffect(sigma: 4) };               // also DropShadowEffect, ColorMatrixEffect
var (mx, my) = RasterLayerEffects.GetInkMargin(effects);                     // how far the blur spreads
var bounds = new Rect(100 - mx, 100 - my, 200 + 2 * mx, 100 + 2 * my);

using (var layer = canvas.BeginLayer(new LayerOptions(Bounds: bounds, Effects: effects)))
{
    layer?.Canvas.DrawRectangle(ctx.GetSolidBrush(PaintColor.FromArgb(160, 0, 0, 0)), 100, 100, 200, 100);
}
```

Layers also carry opacity and a blend mode (`new LayerOptions(Opacity: 0.5)`), with CSS `opacity` semantics. The package also
exposes the raster helpers PeachPDF uses: `PathStroker` (turn a pen's stroke into a fillable outline), `Filters.GaussianBlur`,
`DropShadow`, `FilterOps` and `Turbulence`, `Warp`/`Homography` and `ColorMatrixFilter`.

## More

- [The PeachDrawing guide](https://peachpdf.net/peachdrawing.html): the unit convention, feature coverage and implementing your own `Canvas`.
- [API reference](https://peachpdf.net/api/PeachDrawing/index.html)
- [PeachDrawing.Core](https://www.nuget.org/packages/PeachDrawing.Core/) (the `Canvas` API), [PeachDrawing.PDF](https://www.nuget.org/packages/PeachDrawing.PDF/), [PeachDrawing.Svg](https://www.nuget.org/packages/PeachDrawing.Svg/): other backends for the same drawing code.

## Sponsorship

PeachPDF is free and open source. If it saves you time, you can [sponsor jhaygood86 on GitHub](https://github.com/sponsors/jhaygood86);
sponsors can get paid support, see [Sponsorship](https://peachpdf.net/sponsorship.html).

## License

BSD 3-Clause (see `LICENSE`).

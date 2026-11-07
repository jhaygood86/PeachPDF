# PeachDrawing.Skia

A [SkiaSharp](https://github.com/mono/SkiaSharp) `Canvas` for [PeachDrawing.Core](https://www.nuget.org/packages/PeachDrawing.Core/): draw shapes, gradients, clips,
layers, text and images with Skia, with no PDF, HTML or CSS involved and no [PeachPDF](https://peachpdf.net/) reference needed. Because it
is a PeachDrawing `Canvas`, drawing code written against `PeachDrawing.Core` runs on Skia unchanged, and the same code can also write PDF, SVG or a bitmap.

**You supply the surface.** The package never creates a window, a GPU context or a surface: it draws into an `SKCanvas` you already have, so it
draws on the GPU exactly when the surface behind that canvas does. It is not tied to a platform: use it on Android (`SKGLView`, `SKCanvasView`), iOS,
Windows, Linux or macOS.

```bash
dotnet add package PeachDrawing.Skia
```

**Status:** pre-1.0, versioned in lockstep with PeachPDF (the same number for every release); until 1.0 the API may change between releases.
Targets .NET 10 and later, on SkiaSharp 3. The application referencing this package chooses the SkiaSharp native binaries
(`SkiaSharp.NativeAssets.*`, or the platform's own package) for the platforms it ships to.

## Quick start: draw into a view's SKCanvas

```csharp
using PeachDrawing.Core;
using PeachDrawing.Skia;

using var context = new SkiaRenderContext();       // create once, reuse for every paint

// Inside your view's paint handler, with the SKCanvas it gives you:
using var canvas = SkiaCanvas.FromCanvas(context, skCanvas, new Rect(0, 0, 400, 300));
using var brush = canvas.GetSolidBrush(PaintColor.FromArgb(255, 220, 90, 40));
canvas.DrawRectangle(brush, 20, 20, 360, 260);

var font = context.GetFont("Arial", 24, PaintFontStyle.Regular);
if (font is not null)
    canvas.DrawString("Hello, Skia", font, PaintColor.White, new PaintPoint(40, 60), canvas.MeasureString("Hello, Skia", font));
```

The `SKCanvas` is only borrowed: disposing the `SkiaCanvas` pops anything still pushed and restores the canvas's save level, matrix and clip, and leaves it usable.
A matrix you set beforehand (a DPI scale, a zoom, a scroll offset) is kept; the canvas draws under it.

## Render a page to a bitmap

```csharp
using SkiaSharp;

using var surface = SKSurface.Create(new SKImageInfo(800, 600));        // a CPU surface; a GPU-backed SKSurface works the same way
using var composer = new SkiaComposer(context, surface, widthPoints: 400, heightPoints: 300,
    new SkiaComposerOptions { Background = PaintColor.White });         // scales the page to fill the surface

composer.Canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 30, 144, 255)), 20, 20, 200, 100);

composer.Finish();
await using var file = File.Create("out.png");
composer.Save(file, SkiaImageFormat.Png);                               // also Jpeg (with quality) and Webp; or composer.ToPixelBuffer()
```

`SkiaComposer` borrows the surface and never disposes it.

## Text

Text is shaped by the shared [PeachDrawing.Text](https://www.nuget.org/packages/PeachDrawing.Text/) engine by default (OpenType substitution and positioning, Arabic and
Indic scripts, bidirectional text, color glyphs), so it looks the same on every platform. Register your own fonts with `await context.AddFont(stream, "Family")`.

```csharp
using var native = new SkiaRenderContext(SkiaTextEngine.Native);        // Skia's own fonts and the platform's glyph rendering
using var withShaper = new SkiaRenderContext(SkiaTextEngine.Native, textShaper: new PeachTextShaper());   // or supply your own ISkiaTextShaper (e.g. HarfBuzz)
```

Native-font mode is not feature complete (the guide has the table); the default engine is.

## Images

`SkiaImageCodec` chooses what decodes an image: `Auto` (the default: Skia's codecs where Skia has one, PeachImage otherwise), `PeachImage` (the same pixels on every
platform) or `Native`. Telling the context about the host's `GRContext` (`context.GpuContext = grContext`) uploads decoded images once and reuses the texture.

## Layers, effects and blend modes

`canvas.BeginLayer(new LayerOptions(...))` is an isolated `SaveLayer` with opacity, a blend mode and blur, drop-shadow and color-matrix effects; the 16 blend modes
are `SKBlendMode`s and conic gradients are sweep shaders. Everything else on the `Canvas` API (paths, gradients, clips, transforms, masks) maps directly to Skia.

## Links, cursors and semantics

`AddLink`, `AddDestinationLink`, `AddCursor` and `AddSemanticRegion` draw nothing; they are recorded in `canvas.Links` for your host to use:

```csharp
SkiaLink? link = canvas.Links.HitTestDevice(pointer.X, pointer.Y);      // topmost link under a point in the SKCanvas's own pixels
if (link is not null) await canvas.Links.ActivateAsync(link);           // raises LinkActivating; opens through the launcher you supplied

SkiaCursorArea? cursor = canvas.Links.CursorAtDevice(pointer.X, pointer.Y);          // a CursorShape for your platform's pointer
SkiaSemanticRegion? region = canvas.Links.SemanticAtDevice(pointer.X, pointer.Y);    // role/name/level for accessibility
```

Document content is untrusted, so only `http`, `https`, `mailto` and `tel` addresses are opened by default (`SkiaLinkOptions.AllowedSchemes` widens that), and nothing is opened while
painting. There is **no default launcher**: pass `new SkiaLinkOptions { Launcher = ... }` (an `ISkiaLinkLauncher`, for example one that starts an Android `Intent`) to
`FromCanvas`, or handle `Links.LinkActivating`.

## Lifecycle

Every native resource has one owner and a deterministic disposer: the `SKCanvas` and `SKSurface` you pass in are never disposed, a `SkiaRenderContext` owns the native font
faces it creates (dispose it), and a canvas disposed with clips or layers still open unwinds them.

## More

- [The PeachDrawing.Skia guide](https://peachpdf.net/peachdrawing-skia.html): the full Canvas-to-Skia mapping, the native-font table, the lifecycle table and limitations.
- [API reference](https://peachpdf.net/api/PeachDrawing.Skia/index.html)

## Sponsorship

PeachPDF is free and open source. If it saves you time, you can [sponsor jhaygood86 on GitHub](https://github.com/sponsors/jhaygood86);
sponsors can get paid support, see [Sponsorship](https://peachpdf.net/sponsorship.html).

## License

BSD 3-Clause (see `LICENSE`).

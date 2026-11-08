# PeachDrawing.CoreGraphics

A CoreGraphics `Canvas` for [PeachDrawing.Core](https://www.nuget.org/packages/PeachDrawing.Core) on macOS and iOS: draw shapes, gradients, clips, layers, text and images with Apple's
Quartz 2D, with no PDF, HTML or CSS involved and no [PeachPDF](https://peachpdf.net/) reference needed. Because it is a PeachDrawing `Canvas`, drawing code written against
`PeachDrawing.Core` runs on CoreGraphics unchanged, and the same code can also write PDF, SVG or a bitmap. Layer effects (blur, drop shadow, color matrices) run through Core Image,
on a Metal device when there is one; conic gradients are computed on the CPU.

**You supply the context.** The package never creates a window, a view or a layer: it draws into a `CGContext` you already have - a view's, a layer's, a bitmap's - and borrows it,
restoring its graphics state when the canvas is disposed.

```bash
dotnet add package PeachDrawing.CoreGraphics
```

**Status:** pre-1.0, versioned in lockstep with PeachPDF (the same number for every release); until 1.0 the API may change between releases. Targets `net10.0-macos` and `net10.0-ios`
(and `net11.0-...` under the .NET 11 SDK) and uses the .NET macOS and iOS bindings; building needs a Mac with the `macos` and `ios` workloads.

## Quick start: draw into a view's CGContext

```csharp
using PeachDrawing.Core;
using PeachDrawing.CoreGraphics;

using var context = new CoreGraphicsRenderContext();      // create once, reuse for every draw

// Inside your view's draw handler, with the CGContext it gives you:
using var canvas = CoreGraphicsCanvas.FromContext(context, cgContext, new Rect(0, 0, 400, 300));
using var brush = canvas.GetSolidBrush(PaintColor.FromArgb(255, 220, 90, 40));
canvas.DrawRectangle(brush, 20, 20, 360, 260);

var font = context.GetFont("Helvetica Neue", 24, PaintFontStyle.Regular);
if (font is not null)
    canvas.DrawString("Hello", font, PaintColor.White, new PaintPoint(40, 60), canvas.MeasureString("Hello", font));
```

**Orientation.** The canvas draws top-down (y grows downwards), like every other `Canvas`. A raw CoreGraphics context and an AppKit view have their origin at the bottom left, so
by default `FromContext` flips them about `bounds`; a UIKit context (and a flipped AppKit view) already has it at the top left, so pass `originAtBottomLeft: false`.

## Render a page to a bitmap

```csharp
using var composer = new CoreGraphicsComposer(context, widthPoints: 612, heightPoints: 792, pixelWidth: 1224, pixelHeight: 1584,
    new CoreGraphicsComposerOptions { Background = PaintColor.White });
composer.Canvas.DrawRectangle(brush, 20, 20, 100, 100);

using var file = File.Create("page.png");
composer.Save(file);                                      // CoreGraphicsImageFormat: Png (default), Jpeg or Tiff, through ImageIO; or composer.ToPixelBuffer()
```

## Text

Text is shaped by the shared [PeachDrawing.Text](https://www.nuget.org/packages/PeachDrawing.Text/) engine by default, so it looks and measures the same as on every other
PeachDrawing canvas (OpenType shaping, Arabic and Indic scripts, bidi, color glyphs). `new CoreGraphicsRenderContext(CoreGraphicsTextEngine.Native)` uses CoreText instead, so text
matches the rest of the platform; that mode is not feature complete (the guide has the table). Register your own fonts with `await context.AddFont(stream, "Family")`.

## Images

`CoreGraphicsImageCodec` chooses the decoder when the context is made: `PeachImage` (the default; the same pixels on every platform), `Native` (ImageIO), or `Auto` (ImageIO where the
context has a Metal device, so a hardware-decoded image makes no managed copy; PeachImage otherwise).

```csharp
using var native = new CoreGraphicsRenderContext(CoreGraphicsTextEngine.PeachDrawing, CoreGraphicsImageCodec.Auto);
```

## Layers, effects and gradients

`canvas.BeginLayer(new LayerOptions(...))` composites a group with opacity, a blend mode and blur, drop-shadow and color-matrix effects. Blur and shadow run in Core Image (in sRGB, so a
blur averages the same numbers as on the other backends), on the GPU when `context.EffectsUseGpu` is true. Layers and tiles are bitmaps made at the resolution the canvas draws at.

## Links, cursors and semantics

`AddLink`, `AddDestinationLink`, `AddCursor` and `AddSemanticRegion` draw nothing; they are recorded in `canvas.Links` for your host to use:

```csharp
CoreGraphicsLink? link = canvas.Links.HitTestDevice(point.X, point.Y);
if (link is not null) await canvas.Links.ActivateAsync(link);                             // raises LinkActivating; opens through your launcher

CoreGraphicsCursorArea? cursor = canvas.Links.CursorAtDevice(point.X, point.Y);           // a CursorShape for NSCursor / UIPointerStyle
CoreGraphicsSemanticRegion? region = canvas.Links.SemanticAtDevice(point.X, point.Y);     // role/name/level for VoiceOver
```

Document content is untrusted: only `http`, `https`, `mailto` and `tel` are opened (`CoreGraphicsLinkOptions.AllowedSchemes` widens that), and nothing is opened while painting. There is
**no default launcher**: supply an `ICoreGraphicsLinkLauncher` over `NSWorkspace` or `UIApplication` in `CoreGraphicsLinkOptions.Launcher`, or handle `Links.LinkActivating`.

## Lifecycle

Every native object the package creates has one owner and a deterministic disposer: the `CGContext` you pass in is never disposed, a canvas disposed with clips or layers still open unwinds
them, and the context owns the Core Image context and native font faces (dispose it after the canvases and composers made from it). A canvas, like a `CGContext`, is for one thread at a time.

## More

- [The PeachDrawing.CoreGraphics guide](https://peachpdf.net/peachdrawing-coregraphics.html): the Canvas-to-CoreGraphics mapping, effects on Core Image and Metal, both text engines, the three image codecs, the lifecycle table and limitations.

## Sponsorship

PeachPDF is free and open source. If it saves you time, you can [sponsor jhaygood86 on GitHub](https://github.com/sponsors/jhaygood86);
sponsors can get paid support, see [Sponsorship](https://peachpdf.net/sponsorship.html).

## License

BSD 3-Clause (see `LICENSE`).

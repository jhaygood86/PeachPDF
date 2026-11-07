# PeachDrawing.Win2D

A Win2D backend for [PeachDrawing.Core](https://www.nuget.org/packages/PeachDrawing.Core/): draw shapes, gradients, clips, layers, text and images onto a
`Win2dCanvas` - hardware-accelerated Direct2D, into a drawing session you already have (a `CanvasControl`, a swap chain) or into an offscreen bitmap you read back or
save - with no HTML, CSS or PDF involved and no [PeachPDF](https://peachpdf.net/) reference needed. It is the sibling of the PDF writer (`PeachDrawing.PDF`), the SVG writer
(`PeachDrawing.Svg`) and the raster canvas (`PeachDrawing`): drawing code written against `PeachDrawing.Core` runs on any of them. It records links, cursors and semantic
regions so a host can make them clickable, set the pointer and describe them to UI Automation.

```bash
dotnet add package PeachDrawing.Win2D
```

**Status:** pre-1.0, versioned in lockstep with PeachPDF (the same number for every release); until 1.0 the API may change between releases. **Windows only**
(`net10.0-windows10.0.19041.0`), on Win2D 1.4 for the Windows App SDK. The application that uses the package supplies the Win2D native binary by building for a
platform (`x64`, `ARM64`) or publishing with a runtime identifier, as for any Win2D app.

## Quick start: an offscreen page saved as a PNG

```csharp
using PeachDrawing.Core;
using PeachDrawing.Win2D;

using var context = Win2dRenderContext.CreateSoftware();           // a software (WARP) device the context owns
using var composer = new Win2dComposer(context, 480, 300, new Win2dComposerOptions { Background = PaintColor.White, Dpi = 192 });

var canvas = composer.Canvas;
canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 220, 20, 60)), 20, 20, 200, 120);

var font = context.GetFont("Segoe UI", 24, PaintFontStyle.Regular)!;
canvas.DrawString("Hello", font, PaintColor.Black, new PaintPoint(30, 160), canvas.MeasureString("Hello", font));

await using var file = File.Create("hello.png");
await composer.SaveAsync(file);                                       // finishes the drawing, then writes it (Win2dImageFormat: Png, Jpeg, Bmp, Tiff, Gif)
```

`composer.ToPixelBuffer()` hands you the pixels instead. Pass your own `CanvasDevice` to `new Win2dRenderContext(device)` to draw on the GPU.

## Quick start: into a drawing session you already have

```csharp
void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
{
    using var canvas = Win2dCanvas.FromSession(_context, args.DrawingSession, new Rect(0, 0, sender.ActualWidth, sender.ActualHeight));
    Paint(canvas);                                                    // any PeachDrawing.Core drawing code
}                                                                     // the session is the control's: it is left open
```

`Win2dCanvas.FromRenderTarget` draws into a `CanvasRenderTarget` and blends text on the GPU.

## Text

Text is shaped by the shared [PeachDrawing.Text](https://www.nuget.org/packages/PeachDrawing.Text/) engine by default - the same glyphs on every platform - and rendered as
ClearType-style LCD, grayscale or outlines (`context.TextRendering`, `Win2dTextRendering.Auto` by default). `new Win2dRenderContext(device, Win2dTextEngine.DirectWrite)`
uses DirectWrite instead, so text looks like the rest of Windows; that mode is not feature complete (the guide has the table). Register your own fonts with
`await context.AddFont(stream, "Family")`.

## Images and effects

Images decode with `Win2dImageCodec.Auto` (the default: the Windows Imaging Component where Windows has a codec, PeachImage otherwise), `PeachImage` (identical pixels everywhere)
or `Native`. Animated GIFs and WebP images keep every frame. `canvas.BeginLayer(new LayerOptions(...))` composites a group with opacity, a blend mode and blur,
drop-shadow and color-matrix effects, on the GPU. Direct2D blends natively by add and minimum only, so other blend modes draw as normal (the guide lists the limits).

## Links, cursors and semantics

```csharp
Win2dLink? link = canvas.Links.HitTestDips(point.X, point.Y);                    // topmost link under a pointer position
if (link is not null) await canvas.Links.ActivateAsync(link);                    // opens http, https, mailto and tel by default

Win2dCursorArea? cursor = canvas.Links.CursorAtDips(point.X, point.Y);           // a CursorShape for ProtectedCursor
Win2dSemanticRegion? region = canvas.Links.SemanticAtDips(point.X, point.Y);     // role/name/level for UI Automation
```

Document content is untrusted: only `http`, `https`, `mailto` and `tel` are opened (`Win2dLinkOptions.AllowedSchemes` widens that), and nothing is opened while painting.
An address is opened with the system's default handler (`Windows.System.Launcher`); a custom `ILinkLauncher` (`Win2dLinkOptions.Launcher`) replaces it, or handle `Links.LinkActivating` yourself.

## Lifecycle

Every native Win2D object the package creates has one owner and is released deterministically: a session or device you pass in is never disposed, one the package makes is
disposed with its owner, and a canvas disposed with clips or layers still open unwinds them. Dispose the context after the canvases and composers made from it. If the GPU
device is lost, `Win2dRenderContext.DeviceLost` is raised and a device the context made is replaced on next use.

## More

- [The PeachDrawing.Win2D guide](https://peachpdf.net/peachdrawing-win2d.html): the Canvas-to-Win2D mapping, the lifecycle table, both text engines, retained layers and limitations.
- [API reference](https://peachpdf.net/api/PeachDrawing.Win2D/index.html)

## Sponsorship

PeachPDF is free and open source. If it saves you time, you can [sponsor jhaygood86 on GitHub](https://github.com/sponsors/jhaygood86);
sponsors can get paid support, see [Sponsorship](https://peachpdf.net/sponsorship.html).

## License

BSD 3-Clause (see `LICENSE`).

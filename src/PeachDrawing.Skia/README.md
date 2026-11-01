# PeachDrawing.Skia

A [SkiaSharp](https://github.com/mono/SkiaSharp) `Canvas` for [PeachDrawing.Core](https://www.nuget.org/packages/PeachDrawing.Core): draw shapes, gradients, clips, layers, text and images with Skia, with no PDF, HTML or CSS involved.

**You supply the surface.** The package never creates a window, a GPU context or a surface: it draws into an `SKCanvas` you already have, so it draws on the GPU exactly when the surface behind that canvas does. It is not tied to a platform - use it on Android (`SKGLView`/`SKCanvasView`), iOS, Windows, Linux or macOS.

```csharp
using PeachDrawing.Core;
using PeachDrawing.Skia;

using var context = new SkiaRenderContext();

// Inside your view's paint handler, with the SKCanvas it gives you:
using var canvas = SkiaCanvas.FromCanvas(context, skCanvas, new Rect(0, 0, 400, 300));
using var brush = canvas.GetSolidBrush(PaintColor.FromArgb(255, 220, 90, 40));
canvas.DrawRectangle(brush, 20, 20, 360, 260);
```

The `SKCanvas` is only borrowed: disposing the `SkiaCanvas` restores its matrix and clip and leaves it usable. See the guide for the mapping from each `Canvas` call to Skia, the text engines (the shared `PeachDrawing.Text` engine by default, which shapes text itself - OpenType substitution and positioning, Arabic and Indic scripts, bidi - or Skia's own fonts with a shaper you can supply), the image codecs (PeachImage by default, or Skia's own), links, and the lifecycle of everything native.

The application referencing this package chooses the SkiaSharp native binaries (`SkiaSharp.NativeAssets.*`) for the platforms it ships to.

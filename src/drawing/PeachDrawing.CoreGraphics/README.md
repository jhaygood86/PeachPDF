# PeachDrawing.CoreGraphics

A CoreGraphics `Canvas` for [PeachDrawing.Core](https://www.nuget.org/packages/PeachDrawing.Core) on macOS and iOS: draw shapes, gradients, clips, layers, text and images with Apple's Quartz 2D, with no PDF, HTML or CSS involved. Layer effects (blur, drop shadow, colour matrices) and conic gradients run through Core Image on a Metal device when there is one.

**You supply the context.** The package never creates a window, a view or a layer: it draws into a `CGContext` you already have - a view's, a layer's, a bitmap's - and borrows it, restoring its graphics state when the canvas is disposed.

```csharp
using PeachDrawing.Core;
using PeachDrawing.CoreGraphics;

using var context = new CoreGraphicsRenderContext();

// Inside your view's draw handler, with the CGContext it gives you:
using var canvas = CoreGraphicsCanvas.FromContext(context, cgContext, new Rect(0, 0, 400, 300));
using var brush = canvas.GetSolidBrush(PaintColor.FromArgb(255, 220, 90, 40));
canvas.DrawRectangle(brush, 20, 20, 360, 260);
```

Text is shaped by the shared `PeachDrawing.Text` engine by default, or by CoreText with `CoreGraphicsTextEngine.Native`; images are decoded by PeachImage by default, or by ImageIO with `CoreGraphicsImageCodec.Native` / `Auto`. See the guide for the details.

# PeachDrawing.Skia

`PeachDrawing.Skia` is a [SkiaSharp](https://github.com/mono/SkiaSharp) backend for [`PeachDrawing.Core`](peachdrawing-core.md)'s `Canvas`:
the same drawing calls that write a PDF page ([PeachDrawing.PDF](peachdrawing-pdf.md)), an SVG ([PeachDrawing.Svg](peachdrawing-svg.md)), a
bitmap in memory ([PeachDrawing](peachdrawing.md)) or a Windows [Win2D](peachdrawing-win2d.md) surface are drawn with Skia - into a canvas
**you** supply. It is usable with **no PeachPDF reference at all**, and is versioned in lockstep with the other packages.

```bash
dotnet add package PeachDrawing.Skia
```

It is not tied to a platform: it targets the same frameworks as the rest of the family (`net8.0`, `net10.0` and, with the .NET 11 SDK,
`net11.0`) and uses SkiaSharp 3, so it runs wherever SkiaSharp does - Windows, Linux, macOS, iOS and .NET Android. The application that uses
it picks the SkiaSharp native binaries for the platforms it ships to (`SkiaSharp.NativeAssets.*`, or the platform's own package) exactly as
for any SkiaSharp app.

> **Status: pre-1.0.** The public surface is `SkiaCanvas`, `SkiaRenderContext`, `SkiaComposer`, `SkiaComposerOptions`, `SkiaTextEngine`,
> `SkiaImageCodec`, `SkiaImageFormat`, `ISkiaTextShaper`, `PeachTextShaper`, `SkiaShapedRun`, `SkiaLinkMap`, `SkiaLink`, `SkiaLinkOptions`, `SkiaLinkActivatingEventArgs` and `ISkiaLinkLauncher`;
> `PublicApi.txt` beside the project is the reviewed snapshot.

## You supply the surface

The package never creates a window, a GPU context, a surface or a bitmap to draw on. `SkiaCanvas.FromCanvas` takes the `SKCanvas` you
already have and draws into it, so the drawing is hardware accelerated exactly when the surface behind that canvas is: hand it the canvas of
a GPU-backed `SKSurface` (a `GRContext` surface, an `SKGLView`, a Metal or Vulkan view) and every call runs on the GPU; hand it a bitmap's
and it runs on the CPU. Nothing else changes.

```csharp
using PeachDrawing.Core;
using PeachDrawing.Skia;

using var context = new SkiaRenderContext();

// In your view's paint handler, with the SKCanvas the view gives you (on .NET Android, SKGLView.PaintSurface / SKCanvasView.PaintSurface):
void OnPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
{
    using var canvas = SkiaCanvas.FromCanvas(context, e.Surface.Canvas, new Rect(0, 0, 400, 300));
    var brush = canvas.GetSolidBrush(PaintColor.FromArgb(255, 220, 90, 40));
    canvas.DrawRectangle(brush, 20, 20, 360, 260);
}                                                       // the SKCanvas is the view's: it is left as it was found and not flushed
```

The `SKCanvas` is only **borrowed**. Disposing the `SkiaCanvas` pops anything still pushed and restores the canvas's save level, matrix and
clip; it never disposes or flushes it, and a matrix the host set beforehand (a DPI scale, a zoom, a scroll offset) is kept - the canvas draws
under it. Telling the render context about the host's `GRContext` (`SkiaRenderContext.GpuContext`) is optional: when set, decoded images are
uploaded to it once and the texture is reused for every draw.

To draw a whole page onto a surface you hold and then read it back, `SkiaComposer` scales a page of a given size in points to fill the
surface, clears it, and offers `ToPixelBuffer()` and `Save(stream, format)`. It borrows the surface and never disposes it.

The example project (`src/PeachDrawing.Skia.Example`) draws a handful of scenes onto a CPU surface it makes itself and writes them as PNG
files; a phone app would pass the canvas of its GPU view and change nothing else.

## Units

As on the other canvases, a layout unit is `1 / PixelsPerPoint` of a point (`SkiaRenderContext.PixelsPerPoint`, 1 by default). Rectangles,
lines, polygons, image rectangles, brush geometry and text positions are in layout units; a `GraphicsPath`'s coordinates, a pen's width and
dash lengths are in points. The `SKCanvas`'s user space is the layout units, under whatever matrix the host had set.

## What each `Canvas` call becomes

| Call | Skia |
|---|---|
| `DrawRectangle`, `DrawPath`, `DrawPolygon` (brush) | `DrawRect` / `DrawPath` with a fill `SKPaint` (an `SKPath` built from the path's curves for the one call) |
| `DrawLine`, `DrawRectangle`, `DrawPath` (pen) | The same with a stroke paint: caps, joins, miter limit and an `SKPathEffect` dash; a zero width is Skia's hairline |
| Solid, linear and radial brushes | `SKShader` colour, linear gradient, radial gradient (an ellipse is the circle squeezed by the shader's matrix; a focus or focus radius is a two-point conical gradient); clamp or repeat |
| Conic gradient | A native sweep gradient |
| `TileBrush`, hatch brush | A picture or image shader that repeats; a hatch is a tile made for the call |
| `PushClip` (rectangle, path), `PushClipExclude` | `Save` + `ClipRect` / `ClipPath` (intersect) or `ClipRect` (difference) |
| `PushTransform` / `PopTransform` | `Save` + `Concat` |
| `PushBlendMode` | `SKPaint.BlendMode` on every draw: all sixteen blend modes, including the non-separable ones |
| `PushAntiAlias` | `SKPaint.IsAntialias` and the clip's anti-aliasing |
| `DrawImage` (with source rectangle, sampling) | `DrawImage` with `SKSamplingOptions` (nearest, linear with mipmaps, or Mitchell cubic) |
| `BeginLayer` | `SaveLayer` with a paint: opacity, blend mode and colour matrix, and `SKImageFilter` blur and drop shadow, applied to the group when the layer is finished |
| `CreateTile` | An `SKPictureRecorder`: vector, resolution independent, replayed by the GPU |
| `DrawImageWithOpacity` / `WithColorMatrix` | A paint with alpha or an `SKColorFilter` (a picture is drawn as one group) |
| `DrawImageMasked` / `AlphaMasked` / `BlendedOver` | An isolated `SaveLayer`: the content, then the mask drawn with `DstIn` (luminance filter for a luminosity mask) or `DstOut`; or the two tiles blended with each other inside it |
| `BeginRasterSurface` / `DrawRaster` | The shared raster fallback (for effects such as SVG filter primitives), drawn back as an image |
| `AddLink`, `AddDestinationLink`, `AddDestination` | Recorded in `SkiaCanvas.Links`; see [links](#links) |
| `BeginMarkedContent` and the other tagging calls | Nothing: there is no structure in pixels |

## Text

`SkiaRenderContext(SkiaTextEngine, SkiaImageCodec)` chooses which engine shapes, measures and draws text; the context makes fonts of the
matching kind, so a font is never measured by one engine and drawn by the other.

- **`SkiaTextEngine.PeachDrawing`** (default): the shared [`PeachDrawing.Text`](peachdrawing-text.md) engine, which does the **full text shaping
  itself** - GSUB and GPOS (ligatures, kerning, mark attachment, contextual lookups), Arabic and Syriac joining, Indic and Khmer shaping,
  bidirectional text and script itemization - with no shaping library for you to add. Text is shaped from the font file and drawn as exact
  glyph outlines (colour, bitmap and SVG glyphs included), so it lays out exactly as it does on the PDF, SVG and raster canvases.
- **`SkiaTextEngine.Native`**: Skia's own fonts. Families are resolved by the platform's font manager (`SKFontManager`), shaped with
  an `ISkiaTextShaper` and drawn by Skia as glyph runs, from the glyph atlas on a GPU surface. Unlike the default engine, this one does not
  shape text itself: the package ships a basic shaper that needs nothing beyond Skia (one glyph per character, the font's own advances); for ligatures, kerning and complex scripts the context is given `PeachTextShaper` (the
  `PeachDrawing.Text` shaper, shipped in the package) or a shaper of the host's own (see [shaping native text](#shaping-native-text-with-harfbuzz)). The text looks like the rest of the platform's, but **this mode is not feature complete**; see
  [native fonts](#native-font-mode-is-not-feature-complete).

Fonts a document brings (`@font-face`, `AddFont`) work in both modes.

### Shaping native text with HarfBuzz

`PeachTextShaper` ships in the package: it shapes with the [`PeachDrawing.Text`](peachdrawing-text.md) engine (OpenType substitution and
positioning, Arabic, Indic and Khmer shaping) by reading the font file Skia has loaded, and Skia draws the glyphs. It needs no library beyond
this package:

```csharp
using var context = new SkiaRenderContext(SkiaTextEngine.Native, textShaper: new PeachTextShaper());
```

A typeface whose file cannot be read back (a platform font Skia will not give a stream for) falls back to the basic shaper, and
`font-feature-settings` are not passed on. The package does not depend on `SkiaSharp.HarfBuzz`; a host that prefers it references it and
hands the context its own shaper:

```csharp
sealed class HarfBuzzShaper : ISkiaTextShaper, IDisposable
{
    private readonly Dictionary<SKTypeface, SKShaper> _shapers = [];

    public SkiaShapedRun Shape(string text, SKFont font)
    {
        if (!_shapers.TryGetValue(font.Typeface, out var shaper))
            _shapers[font.Typeface] = shaper = new SKShaper(font.Typeface);

        var r = shaper.Shape(text, font);
        return new SkiaShapedRun(Array.ConvertAll(r.Codepoints, g => (ushort)g), r.Points.Select(p => p.X).ToArray(),
            r.Points.Select(p => p.Y).ToArray(), r.Clusters, r.Width);
    }

    public void Dispose() { foreach (var s in _shapers.Values) s.Dispose(); }
}

using var context = new SkiaRenderContext(SkiaTextEngine.Native, textShaper: new HarfBuzzShaper());
```

The context calls the shaper from one thread at a time, and the shaper is the host's to dispose after the context.

### Native font mode is not feature complete

Native mode is a convenience for text that should look like the rest of the platform's, not a second full text engine. Choose
`SkiaTextEngine.PeachDrawing` (the default) for anything that must match the other backends or use the whole CSS font feature set.

| Area | In native mode |
|---|---|
| Layout | Metrics, hinting, kerning and font fallback are Skia's and the platform's, so line breaks and measured widths differ from the PDF, SVG and raster canvases. |
| Outlines | None. `GetTextOutline` returns `null`, so text with a gradient or pattern fill or a stroke falls back to a solid colour, and `text-decoration-skip-ink` skips nothing. |
| Glyphs by index (`DrawGlyphs`, MathML stretchy parts) | Not drawn: they are addressed in the font file the `PeachDrawing.Text` engine reads. |
| Shaping | The basic shaper does no substitution, kerning or reordering; supply an `ISkiaTextShaper` for those. `font-feature-settings` and the `font-variant-*` groups are never passed on. |
| Variable fonts | `font-variation-settings` and `font-optical-sizing` are ignored; weight, style and stretch select among the family's faces. |
| `unicode-range` | Not split per range; a face is used for every character it covers. |
| Fonts the document adds | Supported where Skia can load the file (TrueType and OpenType); a WOFF or WOFF2 file is not loadable by Skia and is ignored in this mode. |
| Bold and italic | Synthesised (emboldening, a slant) when the matched face lacks them and the document has not forbidden it. |
| Colour glyphs | Drawn by Skia's own colour-font support where the platform has it; per-palette choice (`font-palette`) is not applied. |
| Vertical text | Vertical metrics are not provided. |

## Images

`SkiaImageCodec` chooses what turns an encoded image into pixels:

- **`SkiaImageCodec.PeachImage`** (default): the shared [PeachImage](peachimage.md) decoder, with the same formats and the same pixels as on
  every other canvas.
- **`SkiaImageCodec.Native`**: Skia's codecs (`SKCodec`), which the platform may accelerate. An image Skia cannot decode is handed to PeachImage
  unless `SkiaRenderContext.FallbackToPeachImage` is switched off. The orientation stored with the image is reported as the other decoder
  reports it, so the document can apply it.

Either way the image is a drawable `Image` the canvas turns into an `SKImage` (a texture, once, when `GpuContext` is set); an `Image` made by
another backend is converted from its pixels once per canvas.

## Links

A Skia canvas draws pixels, so a link is not part of the picture. The canvas records every `AddLink`, `AddDestinationLink` and
`AddDestination` call into `SkiaCanvas.Links` (also `SkiaComposer.Links`), a managed map with no native resources that outlives the canvas:

```csharp
var link = canvas.Links.HitTestDevice(pointer.X, pointer.Y);    // topmost link under a point in the SKCanvas's own pixels; null if none
if (link is not null)
    await canvas.Links.ActivateAsync(link);                      // raises LinkActivating; opens an address through the launcher you supplied
```

`HitTest` takes layout units; `HitTestDevice` takes the host canvas's own coordinates, which the matrix the host had set maps onto layout
units. Document content is untrusted, so only `http`, `https`, `mailto` and `tel` addresses are opened by default
(`SkiaLinkOptions.AllowedSchemes` widens that), relative addresses are never opened, and nothing is opened while a page is painted. Skia has
no notion of a platform, so there is **no default launcher**: supply an `ISkiaLinkLauncher` (on .NET Android, one that starts an `Intent`
for the address) or follow the link yourself from `Links.LinkActivating`. A link to a place in the document (`Destination`) is left to the
host to scroll to (`TryGetDestination`). Links drawn inside a tile or a layer are not recorded: they are composited into the page later, in
coordinates a pointer cannot use.

## Lifecycle

Every native Skia object the backend creates has one owner and is released deterministically; nothing relies on a finalizer.

| Resource | Owner | Released by |
|---|---|---|
| The `SKCanvas`, `SKSurface`, `GRContext` | The caller (always; never created here) | The caller |
| A pushed clip, transform or blend (a save level) | The canvas that pushed it | The matching pop, or `SkiaCanvas.Dispose()` for any still pushed |
| Paints, shaders, path effects, paths, filters | The one draw call that needs them | The end of that call, even when it throws |
| A tile (`CreateTile`) | The caller, through the canvas and image it returned | Disposing them; disposing the image also ends a tile canvas still open |
| A layer (`BeginLayer`) | The `CanvasLayer` returned | Disposing it composites and releases; a canvas disposed first ends it as it stands |
| Decoded image | The `Image` | `Dispose()`; the texture is made per GPU context on first draw and rebuilt for another |
| Native font faces and shapers | `SkiaRenderContext` | `Dispose()` |

`SkiaCanvas.Dispose()` is idempotent: it pops everything still pushed in reverse order, restores the host canvas's save level, and from then
on every drawing call throws `ObjectDisposedException`. Dispose the context after the canvases and composers made from it. A canvas, like an
`SKCanvas`, is for one thread at a time.

## Limitations

- **Surface only.** The package draws; it does not create, present or flush surfaces. Flushing a GPU surface is the host's.
- **Gradients.** Gradient colours interpolate unpremultiplied, as Skia does by default; a conic gradient with stops that go past one turn
  is clamped to the first turn.
- **Native text** is not feature complete; see the [table above](#native-font-mode-is-not-feature-complete).
- **Links** have no default launcher; see [links](#links).

## Licenses

BSD 3-Clause. SkiaSharp (MIT, Microsoft and contributors; Skia is BSD 3-Clause) is a dependency; see the package's `THIRD-PARTY-LICENSES.md` and the [license page](license.md).

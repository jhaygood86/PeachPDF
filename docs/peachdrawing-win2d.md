# PeachDrawing.Win2D

`PeachDrawing.Win2D` is a Windows backend for [`PeachDrawing.Core`](peachdrawing-core.md)'s `Canvas`: the same drawing calls that write a
PDF page ([PeachDrawing.PDF](peachdrawing-pdf.md)), an SVG ([PeachDrawing.Svg](peachdrawing-svg.md)) or a bitmap in memory
([PeachDrawing](peachdrawing.md)) are drawn with [Win2D](https://github.com/microsoft/Win2D) - hardware-accelerated Direct2D - either into
a drawing session you already have (a `CanvasControl`, a swap chain, a render target) or into an offscreen bitmap you read back or save.
It is usable with **no PeachPDF reference at all**, and is versioned in lockstep with the other packages.

```bash
dotnet add package PeachDrawing.Win2D
```

It targets `net10.0-windows10.0.19041.0` (and `net11.0-windows10.0.19041.0` when built with the .NET 11 SDK) and uses Win2D 1.4, the Windows
App SDK flavour. The application that uses it supplies the Win2D native binary by building for a platform (`x64`, `ARM64`) or publishing
with a runtime identifier, as for any Win2D app.

> **Status: pre-1.0.** The public surface is `Win2dCanvas`, `Win2dRenderContext`, `Win2dComposer`, `Win2dComposerOptions`,
> `Win2dImageFormat`, `Win2dTextEngine`, `Win2dImageCodec`, `Win2dLinkMap`, `Win2dLink`, `Win2dLinkOptions`, `Win2dLinkActivatingEventArgs`,
> `ILinkLauncher` and `Win2dDeviceLostException`; `PublicApi.txt` beside the project is the reviewed snapshot.

## Quick start

An offscreen page, saved as a PNG:

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
await composer.SaveAsync(file);                                       // finishes the drawing, then writes it
```

Into a session you already have - the same canvas, over a `CanvasControl`'s `Draw` handler:

```csharp
void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
{
    using var canvas = Win2dCanvas.FromSession(_context, args.DrawingSession, new Rect(0, 0, sender.ActualWidth, sender.ActualHeight));
    Paint(canvas);                                                    // any PeachDrawing.Core drawing code
}                                                                     // the session is the control's: it is left open
```

The example project (`src/examples/PeachDrawing.Win2D.Example`) draws a handful of scenes
(shapes, gradients, clips, compositing, patterns, text, links) and writes them as PNG files.

## Units

As on the other canvases, a layout unit is `1 / PixelsPerPoint` of a point (`Win2dRenderContext.PixelsPerPoint`, 1 by default). Rectangles,
lines, polygons, image rectangles, brush geometry and text positions are in layout units; a `GraphicsPath`'s coordinates, a pen's width
and dash lengths are in points. The session's user space is the layout units, so one layout unit is one device-independent pixel; a
composer's bitmap is `width x PixelsPerPoint` DIPs wide at `Win2dComposerOptions.Dpi`.

## What each `Canvas` call becomes

| Call | Win2D |
|---|---|
| `DrawRectangle`, `DrawPath`, `DrawPolygon` (brush) | `FillRectangle` / `FillGeometry` (a `CanvasGeometry` built from the path's curves for the one call) |
| `DrawLine`, `DrawRectangle`, `DrawPath` (pen) | `DrawLine` / `DrawRectangle` / `DrawGeometry` with a `CanvasStrokeStyle` (caps, joins, miter limit, dashes in stroke widths) |
| Solid, linear and radial brushes | `CanvasSolidColorBrush`, `CanvasLinearGradientBrush`, `CanvasRadialGradientBrush` (clamp or wrap) |
| Conic gradient | A pixel shader (`Shaders/ConicGradient.hlsl`) over the shape's clip, for up to 16 stops; more than that, or a device without pixel shaders, draws aliased wedges |
| Hatch brush | A tile made for the call |
| `TileBrush` | `CanvasImageBrush` (wrap) over the tile's bitmap or command list |
| `PushClip` (rectangle, path), `PushClipExclude` | `CreateLayer` with the rectangle or geometry; `PopClip` disposes the layer |
| `PushTransform` / `PopTransform` | `CanvasDrawingSession.Transform` |
| `PushBlendMode` | `CanvasBlend` where Win2D has the rule (add, minimum); see [limitations](#limitations) |
| `PushAntiAlias` | `CanvasAntialiasing` |
| `DrawImage` (with source rectangle, sampling) | `DrawImage` of a `CanvasBitmap` made from the decoded pixels, per device |
| `BeginLayer` | A `CanvasCommandList`, composited with opacity, colour matrix, blur and drop shadow effects when the layer is finished. `HasNativeLayerEffects` is true, so the HTML painter draws a blurred outset `box-shadow`, a CSS `filter` and a `backdrop-filter` as layers with effects (a blur holds its layer's edge pixels only when `BlurEffect.ClampEdges` is set) instead of through a CPU bitmap |
| `CreateTile` | A `CanvasCommandList` (vector, resolution independent) |
| `DrawImageWithOpacity` / `WithColorMatrix` / `Masked` / `AlphaMasked` / `BlendedOver` | `OpacityEffect`, `ColorMatrixEffect`, `LuminanceToAlphaEffect` + `AlphaMaskEffect`, `BlendEffect` |
| `BeginRasterSurface` / `DrawRaster` | The shared raster fallback, drawn back as a bitmap |
| `AddLink`, `AddDestinationLink`, `AddDestination` | Recorded in `Win2dCanvas.Links`; see [links](#links) |
| `BeginMarkedContent` and the other tagging calls | Nothing: there is no structure in pixels |

## Text

`Win2dRenderContext(device, Win2dTextEngine)` chooses which engine shapes, measures and draws text; the context makes fonts of the matching
kind, so a font is never measured by one engine and drawn by the other.

- **`Win2dTextEngine.PeachDrawing`** (default): the shared [`PeachDrawing.Text`](peachdrawing-text.md) engine. Text is shaped from the font
  file and drawn as exact glyph outlines (colour, bitmap and COLR glyphs included), so it lays out exactly as it does on the PDF, SVG and
  raster canvases.
- **`Win2dTextEngine.DirectWrite`**: Windows' own text stack through `CanvasTextLayout`: installed fonts, shaped and rasterized as
  Windows does it, with OpenType features through `CanvasTypography`. **This mode is not feature complete**; see
  [DirectWrite mode](#directwrite-mode-is-not-feature-complete).

### Text rendering (PeachDrawing engine)

`Win2dRenderContext.TextRendering` chooses how the `PeachDrawing` engine's glyphs become pixels. It has no effect in DirectWrite mode.

| `Win2dTextRendering` | What is drawn |
|---|---|
| `Auto` (default) | Chooses for each run, so text looks like the rest of Windows wherever that is possible: `Lcd` on screen (a session the host owns) when ClearType is on and the pixels behind the text can be read, `Grayscale` otherwise, and `Outline` for large text and for text that is rotated or skewed. A `Win2dComposer`'s bitmap is an export that may be shown on any display, so it gets `Grayscale`, never colour fringes. |
| `Outline` | Each run's exact outlines filled as vector geometry: full quality under any transform and brush, but not fitted to the pixel grid and blended as plain coverage, so small text looks lighter and softer than Windows draws it. |
| `Grayscale` | The run is fitted to the pixel grid by the font's own hinting (the TrueType interpreter and CFF engine of the shared text engine; `RenderContext.TextHinting` picks the dialect, `Standard` when unset), rasterized to a coverage mask, corrected for the brightness of the text colour so light and dark text have the same apparent weight, and drawn as a bitmap on whole device pixels. Close to Windows' grayscale text. |
| `Lcd` | As `Grayscale`, with a coverage per red, green and blue stripe (3x horizontal detail, a short filter across stripes, `LcdBgrStripes` for the opposite order) blended per channel. |

`Grayscale` and `Lcd` draw a run as `Outline` when it is rotated or skewed, or in a colour font, and a run too large to rasterize over a host's session. `Auto` also draws text above 48 device pixels per em as `Outline`, where hinting and the weight correction mean nothing and a mask costs far more.

**What LCD text needs.** Blending each colour channel with its own coverage needs the pixels behind the text. There are two ways to get them.

- **On the GPU, with `Win2dCanvas.FromRenderTarget`.** Give the canvas the `CanvasRenderTarget` its session draws into (made with `CanvasAlphaMode.Ignore`, an opaque surface) and text is not blended as it is drawn: runs are queued and composited by a pixel shader (`Shaders/LcdBlend.hlsl`, embedded as `LcdBlend.bin`) in one pass, `backdrop * (1 - coverage) + colour * coverage` for each colour channel, against a copy of the pixels already drawn there. Nothing is read back to the CPU, so a screen of text costs about a millisecond rather than tens of them, over any background: a flat colour, a gradient, an image or something translucent. The queue is drawn before anything else that could touch it (a shape, image, layer or clip or blend change over the same pixels), so text stays where it was drawn in the order things were drawn; a run over another queued run starts a new queue so the two blend with each other. Grayscale text takes the same path. `HtmlView` paints every region this way, into a render target of its own that it then puts on the control's surface.
- **By reading back, with `Win2dCanvas.FromSession`.** A `Win2dComposer` reads its own bitmap. Over a session you hand to `FromSession`, the canvas asks Win2D for the session's native Direct2D target and copies the rectangle behind each run into a CPU-readable bitmap (about 0.04 to 0.1 ms a run on a GPU, which adds up on a page of text), blends on the CPU and draws the result. It applies only when the target has no alpha channel and the blend is the ordinary one; drawing inside a clip is fine. A session that records into a command list, or a target with an alpha channel, gets `Grayscale`. Anything unexpected in the native interface (a missing interface, a pixel format that is not 32-bit BGRA, a failed copy) also falls back to `Grayscale`; nothing throws.

**Repeated runs and scrolling.** A run's mask is kept in a cache (up to 48 MB, least recently used out first) keyed by the text, font, scale and the run's position within its pixel (to 1/64), not by colour or by what is behind it. Drawing the same run again - as a scrolling view does for the strip it reveals - skips shaping, hinting and rasterizing; moving it by whole pixels still hits. A word seen for the first time is built from the masks of its letters, which are kept the same way (up to 24 MB; a letter is rasterized once for each font, size and position within the pixel to 1/16), so only letters not drawn before cost anything. On the GPU path the bitmaps made from a mask (its coverage per colour channel, and the text colour times it) are kept too (up to 64 MB), so a repeated run is two draw calls and no CPU work. The scratch render targets the blend works in belong to the render context and are released with it, or when the device is lost. Setting the `PEACH_TEXT_STATS` environment variable writes the time spent rasterizing and blending text, and the cache's hit count, to the debugger output every couple of seconds.

**How close it is.** The weight correction and the filter were fitted to what Direct2D's own text renderer produces, so the result is close to DirectWrite's text, not identical: glyph advances and metrics still follow the font file rather than DirectWrite's, and DirectWrite's rasterizer is not available to match exactly. A masked run costs several times an outline run (a page of body text paints in about 95 ms against about 35 ms as outlines on a software device), so choose `Outline` for a text-heavy page that does not need the look.

Fonts a document brings (`@font-face`, `AddFont`) work in both modes; DirectWrite loads them from a temporary file the context deletes.

**Sharing fonts between contexts.** Every context has a `FontSet` of its own unless you give it one: `new Win2dRenderContext(fontSet: shared)`. Contexts that
share a set share the fonts added to it, and a font that is added to the set is read once, however many documents and contexts use it; adding the same
font again with the same description is a no-op. The set may be used from several threads. A context registers the families the set holds when it is made;
call `SyncFonts()` to make it see families added since (a view does so before each load). The DirectWrite engine keeps its own faces per context and
ignores the shared set. `AddFont` returns a `ValueTask`, so awaiting it from a `MemoryStream` or a file does not suspend.

### DirectWrite mode is not feature complete

DirectWrite mode is a convenience for text that should look like the rest of Windows, not a second full text engine. Choose
`Win2dTextEngine.PeachDrawing` (the default) for anything that must match the other backends or use the whole CSS font feature set.

| Area | In DirectWrite mode |
|---|---|
| Layout | Metrics, hinting, kerning and font fallback are DirectWrite's, so line breaks and measured widths differ from the PDF, SVG and raster canvases. |
| Outlines | None. `GetTextOutline` returns `null`, so text with a gradient or pattern fill or a stroke falls back to a solid colour, and `text-decoration-skip-ink` skips nothing. |
| Glyphs by index (`DrawGlyphs`, MathML stretchy parts) | Drawn only when the same family is also found in the `PeachDrawing.Text` font set; otherwise nothing is drawn. |
| OpenType features | `font-feature-settings` tags, kerning on/off and ligatures off are mapped; the `font-variant-*` caps, numeric and East Asian groups are not. |
| Variable fonts | `font-variation-settings` and `font-optical-sizing` are ignored; weight, style and stretch select among the family's faces. |
| `unicode-range` | Not split per range; a face is used for every character it covers. |
| Fonts the document adds | Supported, by weight and italic only. They are loaded from temporary files the context deletes on `Dispose` (best effort: a file DirectWrite still has mapped is left behind). |
| Colour glyphs | Drawn by DirectWrite's colour-font support; per-palette choice (`font-palette`) is not applied. |
| Vertical text, `letter-spacing` | Vertical metrics are not provided; letter spacing is applied as trailing space after each character. |

## Images

`Win2dRenderContext(device, textEngine, Win2dImageCodec)` chooses what turns an encoded image into pixels:

- **`Win2dImageCodec.Auto`** (default): an image Windows has a codec for is kept as its encoded bytes and decoded by the Windows Imaging
  Component straight into the bitmap of each device it is drawn on, so no managed copy of its pixels is made (the pixels are decoded only if
  something asks for them, through the image's `GetPixels`). Anything Windows cannot decode is decoded by PeachImage, as is an image that
  carries an orientation in its metadata, because loading into a bitmap applies that orientation and the canvas applies it itself. The pixels
  can differ slightly from PeachImage's (JPEG decoders round differently; colour-managed and CMYK files may convert differently).
- **`Win2dImageCodec.PeachImage`**: the shared [PeachImage](peachimage.md) decoder, with the same formats and the same pixels as on
  every other canvas.
- **`Win2dImageCodec.Native`**: the Windows Imaging Component (`BitmapDecoder`), which can read what the codecs installed on the machine can,
  including formats PeachImage does not know. An image Windows cannot decode is handed to PeachImage unless
  `Win2dRenderContext.FallbackToPeachImage` is switched off. The decode is done on a thread-pool thread and waited for, so a UI thread that
  loads an image cannot deadlock; the orientation stored with the image is read by PeachImage so it is reported as on every other canvas.

## Links

A Win2D canvas draws pixels, so a link is not part of the picture. The canvas records every `AddLink`, `AddDestinationLink` and
`AddDestination` call into `Win2dCanvas.Links` (also `Win2dComposer.Links`), a managed map with no native resources that outlives the
canvas:

```csharp
var link = canvas.Links.HitTest(pointer.X, pointer.Y);          // topmost link under a point, in layout units; null if none
cursor = link is null ? Arrow : Hand;

if (await canvas.Links.TryActivateAsync(pointer.X, pointer.Y))  // an https link opens in the default browser
    return;
```

An address is opened with the system's default handler for its scheme (`Windows.System.Launcher`). Document content is untrusted, so only
`http`, `https`, `mailto` and `tel` are launched by default (`Win2dLinkOptions.AllowedSchemes` widens that), relative addresses are
never launched, nothing is launched while a page is painted, and a launcher that fails reports `false` rather than throwing. A host can
intercept with `Links.LinkActivating` (set `Cancel`), and a link to a place in the document (`Destination`) is left to the host to scroll to
(`TryGetDestination`). A custom `ILinkLauncher` replaces the system one.

### Cursors

`canvas.Links.CursorAt(x, y)` (or `CursorAtDips`) gives the topmost area the document asked a pointer for (`Canvas.AddCursor`), as a
`CursorShape` and any images it names, or `null` where it asked for nothing. A WinUI host sets `ProtectedCursor` from it:

| `CursorShape` | `InputSystemCursorShape` |
|---|---|
| `Default`, `Alias`, `Copy`, `NoDrop`, `Grab`, `Grabbing`, `Progress` | `Arrow` |
| `Pointer` | `Hand` |
| `Text`, `VerticalText` | `IBeam` |
| `Wait` | `Wait` |
| `Help` | `Help` |
| `Crosshair`, `Cell` | `Cross` |
| `NotAllowed` | `UniversalNo` |
| `Move`, `ScrollAll` | `SizeAll` |
| `ResizeHorizontal`, `ResizeEast`, `ResizeWest`, `ResizeColumn` | `SizeWestEast` |
| `ResizeVertical`, `ResizeNorth`, `ResizeSouth`, `ResizeRow` | `SizeNorthSouth` |
| `ResizeDiagonalUp`, `ResizeNorthEast`, `ResizeSouthWest` | `SizeNortheastSouthwest` |
| `ResizeDiagonalDown`, `ResizeNorthWest`, `ResizeSouthEast` | `SizeNorthwestSoutheast` |

A cursor image cannot be used as a WinUI pointer, so the shape stands in for it.

## Lifecycle

Every native Win2D object the backend creates has one owner and is released deterministically; nothing relies on a finalizer.

| Resource | Owner | Released by |
|---|---|---|
| `CanvasDevice` | The caller if passed to `Win2dRenderContext`; the context if it made one (`CreateSoftware`, or on first use) | `Win2dRenderContext.Dispose()`, only for a device it made |
| Drawing session | The caller for `Win2dCanvas.FromSession` (borrowed); the composer for its bitmap | The caller; `Win2dComposer.Finish()`/`Dispose()` |
| Offscreen bitmap | `Win2dComposer` | `Dispose()`, after closing the session |
| A pushed clip (a Win2D layer) | The canvas that pushed it | `PopClip`, or `Win2dCanvas.Dispose()` for any still pushed |
| Brushes, stroke styles, geometry, effects | The one draw call that needs them | The end of that call, even when it throws |
| A tile (`CreateTile`) | The caller, through the canvas and image it returned | Disposing them; disposing the image also closes a tile canvas still open |
| A layer (`BeginLayer`) | The `CanvasLayer` returned | Disposing it composites and releases; a canvas disposed first abandons it without compositing |
| Decoded image | The `Image` | `Dispose()`; its bitmap is made per device on first draw and rebuilt for another |
| DirectWrite font sets and temp font files | `Win2dRenderContext` | `Dispose()` |

`Win2dCanvas.Dispose()` is idempotent: it pops everything still pushed in reverse order, puts back the transform, blend and anti-aliasing a
borrowed session had, closes the session only if the canvas opened it, and from then on every drawing call throws
`ObjectDisposedException`. Dispose the context after the canvases and composers made from it. A canvas, like a drawing session, is for
one thread at a time.

**Device loss.** If the device is lost (a driver reset, a removed adapter) `Win2dRenderContext.DeviceLost` is raised, a device the
context made is replaced on next use, and the images and bitmaps it made are rebuilt lazily. Reading or saving a composer's page whose
device was lost throws `Win2dDeviceLostException`; draw it again.

## Limitations

- **Blend modes.** A drawing session blends only by add and minimum, so `Plus` and `Darken` are exact and the other `PaintBlendMode`s
  (including `Lighten`) draw as `Normal`; `DrawImageBlendedOver` is exact. CSS `mix-blend-mode` is therefore not honoured on this canvas.
- **Gradients.** A conic gradient of more than 16 stops is painted as aliased wedges, a two-circle radial gradient starts from a point focus, and a blur or shadow
  with unequal x and y deviations uses their mean.
- **Text inside a rounded or path clip** (`PushClip(GraphicsPath)`, `PushClipExclude`) is drawn as `Grayscale` even where `Auto` would use `Lcd`: Direct2D keeps what is
  drawn inside such a clip in a layer until the clip is popped, so the pixels behind the text cannot be read or sampled. Rectangular clips are not affected.
- **DirectWrite text** is not feature complete; see the [table above](#directwrite-mode-is-not-feature-complete).
- **Windows only.** Win2D is a Windows App SDK component, so the package builds, tests and packs on Windows only. The other packages still
  build elsewhere, but a build of the whole solution (`PeachPDF.slnx`) needs Windows because it includes this project.

## Licenses

BSD 3-Clause. Win2D (MIT, Microsoft) is a dependency; see the package's `THIRD-PARTY-LICENSES.md` and the [license page](license.md).

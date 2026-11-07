# PeachDrawing.CoreGraphics

`PeachDrawing.CoreGraphics` is an Apple [Quartz 2D](https://developer.apple.com/documentation/coregraphics) (CoreGraphics) backend for
[`PeachDrawing.Core`](peachdrawing-core.md)'s `Canvas` on **macOS and iOS**: the same drawing calls that write a PDF page
([PeachDrawing.PDF](peachdrawing-pdf.md)), an SVG ([PeachDrawing.Svg](peachdrawing-svg.md)), a bitmap in memory ([PeachDrawing](peachdrawing.md)), a
Windows [Win2D](peachdrawing-win2d.md) surface or a [Skia](peachdrawing-skia.md) canvas are drawn with CoreGraphics - into a `CGContext`
**you** supply. It is usable with **no PeachPDF reference at all**, and is versioned in lockstep with the other packages.

```bash
dotnet add package PeachDrawing.CoreGraphics
```

It targets `net10.0-macos` and `net10.0-ios` (and, with the .NET 11 SDK, `net11.0-macos` and `net11.0-ios`), so it is built on a Mac with the `macos`
and `ios` workloads installed, and uses the .NET macOS and iOS bindings for CoreGraphics, CoreText, ImageIO, Core Image and Metal.

> **Status: pre-1.0.** The public surface is `CoreGraphicsCanvas`, `CoreGraphicsRenderContext`, `CoreGraphicsComposer`,
> `CoreGraphicsComposerOptions`, `CoreGraphicsTextEngine`, `CoreGraphicsImageCodec`, `CoreGraphicsImageFormat`, `CoreGraphicsLinkMap`,
> `CoreGraphicsLink`, `CoreGraphicsLinkOptions`, `CoreGraphicsLinkActivatingEventArgs`, `CoreGraphicsCursorArea`, `CoreGraphicsSemanticRegion`,
> `CoreGraphicsGlyphPlacement` and `ICoreGraphicsLinkLauncher`; `PublicApi.txt` beside the project is the reviewed snapshot.

## You supply the context

The package never creates a window, a view, a layer or a surface to draw on. `CoreGraphicsCanvas.FromContext` takes the `CGContext` you already
have - the one an AppKit view's `DrawRect` or a UIKit view's `Draw` hands you, a layer's, a bitmap's - and draws into it, so the drawing is
accelerated exactly as far as the system accelerates what is behind that context (a layer-backed view is composited on the GPU by Core Animation).

```csharp
using PeachDrawing.Core;
using PeachDrawing.CoreGraphics;

using var context = new CoreGraphicsRenderContext();

// Inside your view's draw handler, with the CGContext it gives you:
using var canvas = CoreGraphicsCanvas.FromContext(context, cgContext, new Rect(0, 0, 400, 300));
using var brush = canvas.GetSolidBrush(PaintColor.FromArgb(255, 220, 90, 40));
canvas.DrawRectangle(brush, 20, 20, 360, 260);
```

The context is only **borrowed**: disposing the canvas restores its graphics state (matrix, clip, colours) and leaves it usable.

**Orientation.** The canvas draws top-down (y grows downwards), like every other `Canvas`. A raw CoreGraphics context and an AppKit view have their
origin at the **bottom left**, so by default `FromContext` flips them about `bounds`; a UIKit context (and an AppKit view that is flipped) already has
it at the top left, and you say so with `originAtBottomLeft: false`.

For a drawing that is not on screen, `CoreGraphicsComposer` makes a bitmap context of the pixel size you ask for, scales a page of the point size you
give to fill it, and gives you the canvas; it reads the result back as pixels (`ToPixelBuffer`) or saves PNG, JPEG or TIFF (`Save`, through ImageIO).

```csharp
using var composer = new CoreGraphicsComposer(context, 612, 792, 1224, 1584, new CoreGraphicsComposerOptions { Background = PaintColor.White });
composer.Canvas.DrawRectangle(brush, 20, 20, 100, 100);
using var file = File.Create("page.png");
composer.Save(file);
```

### Units

Rectangles, lines, polygons, image rectangles, brush geometry and text positions are used as given in the context's user space (the canvas's
*layout units*). What the `Canvas` contract defines in **points** - a `GraphicsPath`'s coordinates, a pen's width and dashes - is multiplied by
`CoreGraphicsRenderContext.PixelsPerPoint` first. A matrix already set on your context (a DPI scale, a zoom) is kept: the canvas draws under it.

## What each `Canvas` call becomes

| `Canvas` call | CoreGraphics |
|---|---|
| `PushClip(Rect)`, `PushClip(GraphicsPath)`, `PushClipExclude` | A graphics-state level plus `ClipToRect`, or the path as a non-zero or even-odd clip; the exclusion is an even-odd clip of a huge rectangle with a hole. The pop restores the level |
| `PushTransform` | A graphics-state level plus `ConcatCTM` |
| `PushBlendMode` | A graphics-state level plus `SetBlendMode`; all sixteen blend modes (and `Plus`, as plus-lighter) have a CoreGraphics counterpart, so a pushed blend applies to each draw exactly |
| `PushAntiAlias` | `SetShouldAntialias` |
| Rectangles, lines, paths, polygons | Fills and strokes of a `CGPath`; line width, caps, joins, miter limit and dashes are the pen's |
| Solid brush or pen | The fill or stroke colour (sRGB) |
| Linear and radial gradient | `CGGradient`: linear and radial (with a true focus circle) are native; an ellipse is a circle under a scaled matrix; the *repeat* spread draws as many periods as reach the clip |
| Conic gradient | CoreGraphics has no angular gradient, so the fill is computed per pixel on the CPU at the device's resolution over the clip's bounds and drawn as an image |
| Gradient or pattern *pen* | The pen's outline (`ReplacePathWithStrokedPath`) is the clip the brush paints through |
| Tile and hatch brushes | `DrawTiledImage` of the tile's bitmap, which is made at the device's resolution |
| `CreateTile` | A bitmap context at the resolution the canvas is drawing at; its image is a snapshot |
| `DrawImage` (and its source rectangle and sampling) | `DrawImage` of the (cropped) `CGImage` with the interpolation quality the sampling asks for (`Nearest`/`Pixelated` is none; the others medium or high - CoreGraphics has no cubic) |
| `DrawImageWithOpacity` | The context's alpha and blend mode, then the image |
| `DrawImageMasked`, `DrawImageAlphaMasked`, `DrawImageBlendedOver` | An isolated bitmap of the destination: the content is drawn, then the mask with a destination-in (or destination-out, inverted) blend - a luminosity mask first becomes opacity in Core Image - or the two images with the blend mode between them |
| `DrawImageWithColorMatrix` | Core Image's colour matrix filter |
| `BeginLayer` | The group is drawn into a bitmap of its own and composited back through its opacity, blend mode, colour matrix and effects when the layer is disposed |
| `BlurEffect`, `DropShadowEffect`, `ColorMatrixEffect` | Core Image (see below); `HasNativeLayerEffects` is true |
| `BeginRasterSurface` / `DrawRaster` | The shared `PeachDrawing` software rasterizer for what is not expressible as vector content; its pixels are drawn as an image |
| `DrawString`, `DrawGlyphs` | See [text](#text) |
| `AddLink`, `AddDestinationLink`, `AddDestination`, `AddCursor`, `AddSemanticRegion` | Recorded in the canvas's [link map](#links); nothing is drawn |
| Marked content, artifacts | Nothing (there is no structure to tag in pixels) |

## Effects and the GPU

Blur, drop shadow, colour matrices and luminance-to-opacity run in **Core Image**. When the machine has a Metal device (`MTLDevice.SystemDefault`,
or the one you give `CoreGraphicsRenderContext`'s `gpuDevice` parameter) they run on the GPU; with `useGpu: false`, or where there is none, they run on the
CPU with the same result. `CoreGraphicsRenderContext.EffectsUseGpu` says which. Core Image works in sRGB here, not its default linear space, so a blur
averages the same numbers it does on the other backends. A blur uses the mean of its two sigmas (Core Image's blur is not elliptical).

## Text

Two engines, chosen when the context is made (`CoreGraphicsTextEngine`).

- **`PeachDrawing` (the default).** The shared `PeachDrawing.Text` engine: full OpenType shaping, joining scripts, Indic shaping and bidi from the font file; each run is
  drawn as glyph outlines (one filled `CGPath` per run), so text measures and lays out exactly as it does on the PDF, SVG and raster canvases. Colour
  glyphs (COLR, embedded bitmaps, SVG glyphs) are drawn too. System fonts come from CoreText through the engine's font set.
- **`Native`.** CoreText resolves the family, shapes the run (`CTLine`) and draws it, so text looks like the rest of the platform's (system font, optical sizes)
  but does not lay out identically to the other canvases. It is **not feature complete**:

| Not available with the native engine | What happens |
|---|---|
| Text outlines | `GetTextOutline` returns `null`, so a caller falls back to drawing the string |
| Font features requested by a document (`font-variant-caps`, `font-variant-position`) | Reported unsupported; CoreText applies the font's default features |
| Colour glyph fonts' COLR/SVG layers | CoreText draws the font's own colour glyphs (Apple Color Emoji) but not the library's layer painters |
| Variable-font axes from `font-variation-settings` | Not applied |
| Ink-skipping underlines | `GetInkCrossings` reports none |

**Matching CoreText's layout.** With the same font file the two engines lay out identically. For the *system font* (San Francisco) the default engine does what CoreText does so that it measures and wraps the same:
`AutomaticOpticalSize` (on) sets the font's optical-size axis to its size in points - San Francisco is drawn narrower and tighter in its display cut at its default, and CoreText picks the text cut below 17 points - and `ApplyFontTracking` (on) adds
the font's size-dependent tracking (its `trak` table, which makes it looser when small and tighter when large) to every glyph's advance. Widths then agree with CoreText to a fraction of a pixel from 9 to 48 points. Both change only a font that has the
axis or the table. A host that passes `font-variation-settings` supplies its own axes (an HTML host overrides `ResolveVariationAxes`, whose optical size is the size in CSS pixels).

**Glyph placement.** `GlyphPlacement` (default `Platform`) places glyphs the way Apple does. The baseline of a line is rounded to a whole device pixel, which is what gives system text its crisp baselines. A glyph's horizontal origin is rounded *down* to a grid of 1/n
pixel, where n is the least whole number for which n times the text's size in device pixels reaches 33.3, at most 5 on macOS and 3 on iOS - so text 34 pixels or more high sits on whole pixels, 17 to 33 on half pixels, 12 to 16 on thirds, 9 to 11 on quarters (macOS) and
smaller on fifths (macOS) or thirds (iOS). This was measured against CoreGraphics itself rather than documented by Apple, with one font and a handful of sizes on each platform, and the default engine matches it exactly at those sizes; the native engine leaves placement to CoreGraphics.
`Exact` puts every glyph at precisely the position layout gave it.

**Matching the platform's weight.** macOS draws its own text with font smoothing, which puts down about a fifth more ink than the bare glyph outlines (iOS does not). So that the default engine's text sits next to
system text without looking thin, `MatchPlatformTextWeight` (on by default on macOS and Mac Catalyst, off on iOS) strokes each outlined glyph by about a hundredth of an em; against CoreText's own smoothed text
this brings the ink to within a few percent and cuts the pixel difference by a third or more. It is a first-order match of a contrast curve, and it is a user preference on the platform, so turn it off if you want the plain outlines.
The native engine places every glyph at its exact fractional position (CoreGraphics otherwise snaps glyph origins to a coarse sub-pixel grid, which jitters against the measured line).

**Hinting.** Text is unhinted by default, as Apple's own is, which is right for Retina (two or more pixels to a point). On a display with fewer than two - a Mac's external 1x
monitor, an older iPad - set `HintTextOnLowDensityDisplays` on the context to fit the default engine's glyphs to the pixel grid **vertically only** (light hinting: baselines and x-heights land on
pixel edges, horizontal metrics and so layout are unchanged). Setting the context's `TextHinting` to `Standard` or `Monochrome` hints at every density and takes precedence. Hinting needs the text to
reach the pixels unrotated and unskewed; otherwise it is skipped for that run. It does not apply to the native engine, where CoreText decides.

`@font-face` data is registered with CoreText as well as the shared engine; a format CoreText cannot load (WOFF) stays available to the default engine only. A
face's synthetic bold and italic are applied where the matched face lacks the style.

## Images

The codec is chosen when the context is made (`CoreGraphicsImageCodec`).

- **`PeachImage` (the default).** The shared decoder: the same formats and the same pixels as on every other PeachDrawing canvas.
- **`Native`.** ImageIO decodes. A format ImageIO cannot read is handed to PeachImage unless `FallbackToPeachImage` is switched off (then `SupportsImageType`
  narrows to ImageIO's types).
- **`Auto`.** Chosen image by image: where the context has a Metal device, an image ImageIO has a codec for is kept as its **encoded bytes** and decoded by the system
  on first draw - in hardware where it can - so no managed copy of its pixels is made unless something asks for them (`GetPixels`); any other image, any image with an
  orientation stored in its metadata, and every image on a context without a Metal device is decoded by PeachImage. Pixels can differ slightly from PeachImage's
  where the system converts colour-managed or CMYK files.

## Links

A link is not part of a picture of pixels. `AddLink`, `AddDestinationLink`, `AddCursor` and `AddSemanticRegion` on the **page** canvas (not a layer or tile, whose
coordinates mean nothing to a pointer) are recorded in `canvas.Links`, a `CoreGraphicsLinkMap` that outlives the canvas. The host hit-tests the pointer
(`HitTest`, or `HitTestDevice` for a position in the context's own pixels before your matrix) and activates it with `TryActivateAsync`. Nothing is launched
while painting. `canvas.Links.CursorAt` (or `CursorAtDevice`) gives the cursor area under a point, and `SemanticAt` (or `SemanticAtDevice`) the topmost
`CoreGraphicsSemanticRegion`, which is what a host needs to answer VoiceOver for what is drawn there; `Cursors` and `Semantics` list them all. There is **no default launcher** (the package does not know your app's navigation): supply an `ICoreGraphicsLinkLauncher` over `NSWorkspace` or
`UIApplication`, or handle `LinkActivating`; only `http`, `https`, `mailto` and `tel` are opened unless you allow more.

## Lifecycle

Every native object the backend creates has one owner and is released deterministically; nothing relies on a finalizer.

| Resource | Owner | Released by |
|---|---|---|
| The `CGContext` passed to `FromContext` | The caller (always) | The caller |
| A pushed clip, transform or blend (a graphics-state level) | The canvas that pushed it | The matching pop, or `CoreGraphicsCanvas.Dispose()` for any still pushed |
| Paths, gradients, colours | The one draw call that needs them | The end of that call, even when it throws |
| A tile (`CreateTile`) | The caller, through the canvas and image it returned | Disposing them; disposing the image also ends a tile canvas still open |
| A layer (`BeginLayer`) | The `CanvasLayer` returned | Disposing it composites and releases its bitmap; a canvas disposed first ends it as it stands |
| Decoded image | The `Image` | `Dispose()` |
| Core Image context, native font faces | `CoreGraphicsRenderContext` | `Dispose()` |
| The composer's bitmap | `CoreGraphicsComposer` | `Dispose()` |

`CoreGraphicsCanvas.Dispose()` is idempotent; afterwards every drawing call throws `ObjectDisposedException`. Dispose the context after the canvases and composers made
from it. A canvas, like a `CGContext`, is for one thread at a time.

## Limitations

- **Context only.** The package draws; it does not create, present or flush views or layers. The GPU does what the system gives the context's backing store; only
  effects (and `Auto` image decoding) use Metal directly.
- **Layers and tiles are bitmaps**, made at the resolution the canvas draws at (capped by `MaxRasterPixels`), where Skia records vector pictures.
- **Conic gradients** are computed on the CPU and drawn as an image; stops that go past one turn are clamped.
- **Image interpolation** has no cubic filter; `Bicubic` draws with the high-quality setting.
- **Native text** is not feature complete; see the [table above](#text).
- **Links** have no default launcher; see [links](#links).
- **Building** needs a Mac with the `macos` and `ios` workloads, so the package is not in `PeachPDF.slnx` and its API reference is generated on a Mac.

## Licenses

BSD 3-Clause. The package calls the public Apple frameworks through the .NET macOS and iOS bindings; see the package's `THIRD-PARTY-LICENSES.md` and the [license page](license.md).

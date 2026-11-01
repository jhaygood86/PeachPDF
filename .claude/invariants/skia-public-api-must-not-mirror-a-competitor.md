# The PeachDrawing.Skia public API must not mirror a competitor's

Same standing rule as [[core-public-api-must-not-mirror-a-competitor]], [[svg-public-api-must-not-mirror-a-competitor]],
[[win2d-public-api-must-not-mirror-a-competitor]] and [[peachdrawing-public-api-must-not-mirror-a-competitor]], scoped to
`PeachDrawing.Skia`'s own new public surface - the types the package adds on top of `PeachDrawing.Core`. The package wraps SkiaSharp, so the
vocabulary that is already taken is SkiaSharp's own: `SKCanvas`, `SKSurface`, `SKPaint`, `SKPicture`, `SKBitmap`, `SKImage`, `SKPath`,
`SKTypeface`, `SKFont`, `SKShader`, `SKCodec`, `GRContext`. Reading this file is the precondition for adding a public type, member or option
to the package.

## The rules

Identical to [[core-public-api-must-not-mirror-a-competitor]]'s five rules, and the same pending maintainer-review status as
[[svg-public-api-must-not-mirror-a-competitor]]. The shape to follow is the one the siblings settled on: `Skia<Base>` for the backend's own
implementation of a `PeachDrawing.Core` base (`SkiaCanvas`, `SkiaRenderContext`) and `Skia<Composer...>` for the document-level surface,
mirroring `PdfComposer`/`SvgComposer`/`Win2dComposer`. Do **not** reuse a SkiaSharp name (`SK*` for a type of ours, `Surface`, `Paint`,
`Picture` as a type) and do not expose a SkiaSharp type other than where a caller must hand one in or take one out: `SKCanvas` (what
`SkiaCanvas.FromCanvas` draws into), `SKSurface` (what `SkiaComposer` draws on) and `GRContext` (`SkiaRenderContext.GpuContext`) are the
integration points, and nothing else - no `SKPaint`, `SKImage` or `SKTypeface` in a signature.

## Register

| Public name | Role | Origin of the name and shape |
|---|---|---|
| `SkiaCanvas` (sealed, extends `Canvas`), `SkiaCanvas.FromCanvas` | The drawing surface that draws each call with Skia, over a caller's `SKCanvas` | `Canvas` with the backend prefix - the `<Backend><Base>` shape of `PdfCanvas`, `RasterCanvas`, `SvgCanvas`, `Win2dCanvas`; `FromCanvas` names what it is made from (the sibling's is `FromSession`) |
| `SkiaRenderContext` (extends `RenderContext`, `IDisposable`, subclassable) | Fonts, image decoding, the layout-unit scale and the host's GPU context | `RenderContext` with the backend prefix; `PixelsPerPoint`/`AddFont`/`OnFontFamilyAdded`/`ResolveVariationAxes` are the same members, with the same meaning, as `SvgRenderContext`'s and `Win2dRenderContext`'s; `GpuContext`/`FallbackToPeachImage` are ours |
| `SkiaComposer`, `SkiaComposerOptions` | One page drawn onto a caller's surface: its canvas, finishing and reading back | `Composer` is this repository's name for the document-level assembler; `Finish` mirrors the page-completing call of `PdfComposerPage`; `ToPixelBuffer`/`Save` name what they return and do |
| `SkiaImageFormat` | The file format a page is saved as | Ours; each value is the format's name |
| `SkiaTextEngine` (`PeachDrawing`, `Native`) | Which engine shapes and draws text | Ours; each value names the engine |
| `PeachTextShaper` | The ready-made `ISkiaTextShaper` over the `PeachDrawing.Text` engine | Ours; named for the engine it wraps, not the Skia type it serves |
| `ISkiaTextShaper`, `SkiaShapedRun` | The seam a host fills to shape native-font text (e.g. over HarfBuzz, which the package does not depend on) | Ours; plain arrays, no SkiaSharp.HarfBuzz type in the signature |
| `SkiaImageCodec` (`PeachImage`, `Native`) | Which decoder turns an encoded image into pixels (`Win2dImageCodec` is the same switch) | Ours; each value names the decoder |
| `SkiaLinkMap`, `SkiaLink`, `SkiaLinkOptions`, `SkiaLinkActivatingEventArgs`, `ISkiaLinkLauncher` | The links a page recorded, finding the one under a point (`HitTest`, `HitTestDevice` for the host canvas's own pixels), and opening it through a launcher the host supplies | Ours; `TryActivateAsync`/`TryGetDestination` name what they do; `LinkActivating`/`Cancel` follow the .NET event idiom; there is no default launcher on purpose |

Everything else in the assembly is `internal` (`SkiaImage`, `SkiaTileImage`, `SkiaGraphicsPath`, `SkiaFont`, `NativeSkiaFont`,
`NativeFonts`, `NativeFace`, `SkiaResourceCounts`, the brush and layer helpers), and `PeachDrawing.Skia.Tests` alone is granted
`InternalsVisibleTo`; `PublicApi.txt` and `PublicSurfaceTests` pin both.

## Lifetime is part of the contract

Every native Skia object the backend makes has one owner and one disposer, and `SkiaResourceCounts` (internal) exists so a test can prove
none is left: a pushed clip, transform or blend holds a save level of the caller's `SKCanvas` until its pop, a paint/shader/path lives for one
draw call, a tile owns a picture recorder, a layer owns a save level and its paint, and the caller's `SKCanvas`, `SKSurface` and `GRContext` are
never disposed, flushed or replaced. A change that adds a native allocation must say who disposes it and bump the counters; see
`LifecycleAndLinkTests`.

## The consumer supplies the surface

Nothing in this package may create the surface a drawing lands on, or a GPU context: hardware acceleration is whatever the caller's
`SKCanvas` is backed by, and a feature that would need the package to allocate one (a presenting view, a swap chain) belongs in the host. The
package may allocate scratch bitmaps for its own use (a decoded image's raster, a raster-seam image) and must count and release them.

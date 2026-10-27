# The PeachDrawing.Win2D public API must not mirror a competitor's

Same standing rule as [[core-public-api-must-not-mirror-a-competitor]], [[svg-public-api-must-not-mirror-a-competitor]] and
[[peachdrawing-public-api-must-not-mirror-a-competitor]], scoped to `PeachDrawing.Win2D`'s own new public surface - the types the
package adds on top of `PeachDrawing.Core`. The package wraps Microsoft's Win2D (`Microsoft.Graphics.Canvas`), so the vocabulary that is
already taken is Win2D's own: `CanvasDevice`, `CanvasDrawingSession`, `CanvasRenderTarget`, `CanvasControl`, `CanvasBitmap`,
`CanvasTextLayout`, `CanvasTextFormat`, `CanvasGeometry`, `CanvasStrokeStyle`. Reading this file is the precondition for adding a public
type, member or option to the package.

## The rules

Identical to [[core-public-api-must-not-mirror-a-competitor]]'s five rules, and the same pending maintainer-review status as
[[svg-public-api-must-not-mirror-a-competitor]]. The shape to follow is the one the siblings settled on: `Win2d<Base>` for the backend's
own implementation of a `PeachDrawing.Core` base (`Win2dCanvas`, `Win2dRenderContext`) and `Win2d<Composer...>` for the document-level
surface, mirroring `PdfComposer`/`SvgComposer`. Do **not** reuse a Win2D name (`Canvas*` for a type of ours, `DrawingSession`,
`RenderTarget`, `Device` as a type) and do not expose a Win2D type other than where a caller must hand one in or take one out
(`CanvasDevice` on the context, which is the integration point).

## Register

| Public name | Role | Origin of the name and shape |
|---|---|---|
| `Win2dCanvas` (sealed, extends `Canvas`), `Win2dCanvas.FromSession` | The drawing surface that draws each call with Win2D, over a caller's session or a composer's bitmap | `Canvas` with the backend prefix - the `<Backend><Base>` shape of `PdfCanvas`, `RasterCanvas`, `SvgCanvas`; `FromSession` names what it is made from |
| `Win2dRenderContext` (extends `RenderContext`, `IDisposable`, subclassable) | Fonts, image decoding, the layout-unit scale and the device an offscreen drawing and DirectWrite text need | `RenderContext` with the backend prefix; `PixelsPerPoint`/`AddFont`/`OnFontFamilyAdded`/`ResolveVariationAxes` are the same members, with the same meaning, as `SvgRenderContext`'s; `Device`/`DeviceLost`/`CreateSoftware` are ours |
| `Win2dComposer`, `Win2dComposerOptions` | One offscreen page: its canvas, size, finishing and reading back | `Composer` is this repository's name for the document-level assembler; `Finish` mirrors the page-completing call of `PdfComposerPage`; `ToPixelBuffer`/`SaveAsync` name what they return and do |
| `Win2dImageFormat` | The file format a page is saved as | Ours; each value is the format's name |
| `Win2dTextEngine` (`PeachDrawing`, `DirectWrite`) | Which engine shapes and draws text | Ours; each value names the engine |
| `Win2dLinkMap`, `Win2dLink`, `Win2dLinkOptions`, `Win2dLinkActivatingEventArgs`, `ILinkLauncher` | The links a page recorded, finding the one under a point, and opening it with the system's handler | Ours; `HitTest`/`TryActivateAsync`/`TryGetDestination` name what they do; `LinkActivating`/`Cancel` follow the .NET event idiom |
| `Win2dDeviceLostException` | Reading a drawing whose device was lost | Ours |

Everything else in the assembly is `internal` (`Win2dImage`, `Win2dTileImage`, `Win2dGraphicsPath`, `Win2dFont`, `DirectWriteFont`,
`DirectWriteFonts`, `Win2dResourceCounts`, the brush and layer helpers), and `PeachDrawing.Win2D.Tests` alone is granted
`InternalsVisibleTo`; `PublicApi.txt` and `PublicSurfaceTests` pin both.

## Lifetime is part of the contract

Every native Win2D object the backend makes has one owner and one disposer, and `Win2dResourceCounts` (internal) exists so a test can
prove none is left: a pushed clip holds a layer until its pop, a brush/stroke style/geometry lives for one draw call, a tile or layer owns
a command list, a borrowed session or device is never closed. A change that adds a native allocation must say who disposes it and bump
the counters; see `LifecycleTests`.

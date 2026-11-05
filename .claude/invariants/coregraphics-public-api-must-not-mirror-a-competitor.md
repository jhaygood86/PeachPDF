# The PeachDrawing.CoreGraphics public API must not mirror a competitor's

Same standing rule as [[core-public-api-must-not-mirror-a-competitor]], [[skia-public-api-must-not-mirror-a-competitor]],
[[win2d-public-api-must-not-mirror-a-competitor]], [[svg-public-api-must-not-mirror-a-competitor]] and
[[peachdrawing-public-api-must-not-mirror-a-competitor]], scoped to `PeachDrawing.CoreGraphics`'s own new public surface. The package wraps Apple's
frameworks, so the vocabulary that is already taken is Apple's: `CG*` (`CGContext`, `CGImage`, `CGPath`, `CGGradient`), `CT*` (`CTFont`, `CTLine`), `CI*`
(`CIContext`, `CIImage`), `MTL*`, `NS*` and `UI*`. Reading this file is the precondition for adding a public type, member or option to the package.

## The rules

Identical to [[core-public-api-must-not-mirror-a-competitor]]'s five rules. The shape to follow is the siblings': `CoreGraphics<Base>` for the backend's
own implementation of a `PeachDrawing.Core` base (`CoreGraphicsCanvas`, `CoreGraphicsRenderContext`) and `CoreGraphicsComposer` for the document-level
surface. Do **not** reuse an Apple name for a type of ours, and do not expose an Apple type other than where a caller must hand one in or take one out:
`CGContext` (what `CoreGraphicsCanvas.FromContext` draws into) and `IMTLDevice` (`CoreGraphicsRenderContext`'s `gpuDevice` and `GpuDevice`) are the
integration points, and nothing else - no `CGImage`, `CTFont` or `CIContext` in a signature.

## Register

| Public name | Role | Origin of the name and shape |
|---|---|---|
| `CoreGraphicsCanvas` (sealed, extends `Canvas`), `CoreGraphicsCanvas.FromContext` | Draws each call with CoreGraphics over a caller's `CGContext` | The `<Backend><Base>` shape; `FromContext` names what it is made from |
| `CoreGraphicsRenderContext` (extends `RenderContext`, `IDisposable`, subclassable) | Fonts, image decoding, the layout-unit scale and the Core Image/Metal effects context | `PixelsPerPoint`/`AddFont`/`OnFontFamilyAdded`/`ResolveVariationAxes`/`FallbackToPeachImage` are the siblings' members with the same meaning; `GpuDevice`/`EffectsUseGpu` are ours |
| `CoreGraphicsComposer`, `CoreGraphicsComposerOptions` | One page drawn onto a bitmap it owns: its canvas, finishing and reading back | The repository's name for the document-level assembler |
| `CoreGraphicsImageFormat`, `CoreGraphicsTextEngine`, `CoreGraphicsImageCodec` | File format saved, text engine, image decoder | Ours; each value names what it is (`CoreGraphicsImageCodec` is `SkiaImageCodec`/`Win2dImageCodec`'s switch) |
| `CoreGraphicsLinkMap`, `CoreGraphicsLink`, `CoreGraphicsLinkOptions`, `CoreGraphicsLinkActivatingEventArgs`, `CoreGraphicsCursorArea`, `CoreGraphicsSemanticRegion`, `ICoreGraphicsLinkLauncher` | The links a page recorded and opening one through a launcher the host supplies | Same shape and meaning as the Skia ones; there is no default launcher on purpose |

Everything else is `internal` (`CoreGraphicsImage`, `CoreGraphicsTileImage`, `CoreGraphicsOffscreen`, `CoreGraphicsEffects`, `CoreGraphicsFont`,
`CoreGraphicsNativeFont`, `NativeFonts`, `NativeFace`, `ConicPixels`, `CoreGraphicsResourceCounts`), and `PeachDrawing.CoreGraphics.Tests` alone is granted
`InternalsVisibleTo`; `PublicApi.txt` and `PublicSurfaceTests` pin both.

## Lifetime is part of the contract

Every native object the backend makes has one owner and one disposer, and `CoreGraphicsResourceCounts` (internal) exists so a test can prove none is left: a
pushed clip, transform or blend holds a graphics-state level of the caller's `CGContext` until its pop, a path/gradient/colour lives for one draw call, a tile or layer owns a
bitmap context, and the caller's `CGContext` is never released, flushed or replaced. A change that adds a native allocation must say who disposes it and bump the counters.

## The consumer supplies the context

Nothing in this package may create the context a drawing lands on, or a view or layer: acceleration is whatever backs the caller's `CGContext`. The package may allocate
scratch bitmaps (layers, tiles, masks, the conic fill) and the Core Image context, and must count and release them. Native interop the bindings leave a gap in is
source-generated `[LibraryImport]` only, never `[DllImport]`.

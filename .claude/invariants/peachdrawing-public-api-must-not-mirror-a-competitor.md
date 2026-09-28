# The PeachDrawing public API must not mirror a competitor's

Same standing rule as [[abstractions-public-api-must-not-mirror-a-competitor]] and
[[text-public-api-must-not-mirror-a-competitor]], scoped to `PeachDrawing`'s own new public surface - the
three types the package adds on top of `PeachDrawing.Abstractions` (`RasterCanvas`, `RasterRenderContext`,
`RasterSurface`). This package's collision risk is the lowest of the three siblings: it has no direct
competitor at all in the sense SixLabors.Fonts was for `PeachDrawing.Text` or a specific vector-drawing
library might be for `PeachDrawing.Abstractions` - a CPU software rasterizer with a `Canvas`/`RenderContext`
implementation is not a product category with an incumbent name to accidentally echo. The same discipline
still applies because the *names* draw from the same decades-old 2D-graphics vocabulary the sibling
packages do, and that's exactly the vocabulary a "too similar" objection would be raised against.

## The rules

Identical to [[abstractions-public-api-must-not-mirror-a-competitor]]'s five rules and the same pending
maintainer-review status: the naming-review waiver ([[no-more-api-review-gates]]) was granted for
`PeachDrawing.Text` specifically and has not been confirmed to extend here, so treat the register below as
pending, not covered, until told otherwise.

## Register

| Public name | Role | Origin of the name and shape |
|---|---|---|
| `RasterCanvas` (sealed, extends `Canvas`) | The concrete CPU software-raster drawing surface | `Canvas` (the abstract base, already registered) with a `Raster` prefix identifying the concrete backend - the same `<Backend><Base>` naming shape as `PeachPDF.Adapters.GraphicsAdapter` uses for the PDF backend, except named for what it *is* rather than a generic "adapter" suffix, since this type has no PeachPDF-specific adapting to do - it's the raster engine's own native `Canvas` |
| `RasterRenderContext` (sealed, extends `RenderContext`) | The concrete, directly-constructible factory a `RasterCanvas` is built from | Same `<Backend><Base>` pattern as `RasterCanvas`, mirroring `RenderContext` (already registered) |
| `RasterSurface` (`Width`, `Height`, `GridX`, `GridY`, `PixelsPerUnitX`, `PixelsPerUnitY`, `Stride`, `Pixels`, `Row`, `LayoutRect`, `Clear`) | A premultiplied RGBA8 pixel buffer placed on the engine's device-pixel grid | Ours; "surface" is the standard term for an addressable pixel buffer a 2D rasterizer draws into (Cairo `cairo_surface_t`, Skia `SkSurface`), `Stride`/`Row`/`Pixels` are the ordinary raw-bitmap-access vocabulary every one of those APIs also uses |
| `RasterCanvas.ToPixelBuffer`, `.Save`, `.SaveAsync` | Exporting a drawn canvas as pixels or an encoded image file | Ours; `PixelBuffer` is the already-registered `PeachDrawing.Abstractions` type, `Save`/`SaveAsync` mirror `PeachImage.Image.Save`/`.SaveAsync`'s own names since this is a thin, intentional pass-through to that dependency, not new vocabulary |

`PixelBuffer` itself (`Width`, `Height`, `PremultipliedRgba`) lives in `PeachDrawing.Abstractions`, not this
package - it belongs in the [[abstractions-public-api-must-not-mirror-a-competitor]] register, which is
missing a row for it (a pre-existing gap in that file, not introduced here; worth fixing the next time that
file is touched for an unrelated reason, per this repo's own "add exactly one new file, edit nothing else"
convention for invariants - fixing it here would mean editing a second file for this entry, which that
convention exists specifically to avoid).

Every name above either reuses an already-registered `PeachDrawing.Abstractions` name with the ordinary
`Raster` backend prefix this codebase already uses for its PDF backend (`GraphicsAdapter`), or is
generic, multi-decades-old 2D-rasterizer vocabulary (`Surface`, `Stride`, `Row`) that predates every
current commercial or open-source 2D graphics library and belongs to none of them.

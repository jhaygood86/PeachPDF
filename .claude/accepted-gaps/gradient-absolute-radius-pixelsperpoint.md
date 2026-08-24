# Gradient absolute-unit radii/positions ignore `PixelsPerPoint`

`CssImagePainter`'s gradient brush construction resolves absolute-length gradient geometry — an explicit
`radial-gradient(50px at ..., ...)` radius (`GetRadialGradientBrush`'s `ExplicitRadiusX`/`ExplicitRadiusY`
branch) and `ConvertLength`'s absolute-length branch (used for conic/radial gradient stop positions) — via
the bare `Length.ToPixel()`, a fixed conversion with no knowledge of `PixelsPerPoint`. These values feed
`originRect`-relative paint-time geometry, which is already in PeachPDF's internal, `PixelsPerPoint`-
inflated layout coordinate space whenever `PdfGenerateConfig.PixelsPerInch` is non-default — so an
absolute-unit gradient radius/stop position resolves too small relative to the box it paints into, the
same way issue #814's reported `<img>`/`<svg>` sizing bug did before that fix.

This is the same bug class as #814, just not reachable through that fix's mechanism: unlike
`CssValueParser`'s box-aware `ParseLength` overloads (the #814 fix's core mechanism), these call sites
don't currently have a `CssBox`/adapter in scope to consult for the ambient `PixelsPerPoint`.

Left out of #814's fix to keep that change bounded to the reported bug plus the two pre-existing bugs it
directly exposed while implementing the fix (see [.claude/recent-fixes/](../recent-fixes/) for that PR).
Tracked as [issue #821](https://github.com/jhaygood86/PeachPDF/issues/821).

# Inline SVG artwork at a layout scale other than 1 (follow-up to the font-size fix)

Before: the viewBox-to-viewport matrix had its *linear* part divided by `PixelsPerPoint` ("paths are raw points"), while the canvas
divides only a matrix's *translation* and everything else it takes (positions, rects, brush geometry, tile/layer/raster bounds,
`CurrentTransform`) by it as layout units. Paths happened to land right; every non-path primitive was off by the scale, and the two
halves disagreed inside any `<g transform>`, nested `<svg>`, `<use>`, `<symbol>`, `<pattern>`, marker, filter, gradient, image...
Rasterizing 27 small SVGs at `PixelsPerInch` 72/96/144 and diffing showed gradients, images, opacity groups, transforms, `use`,
`symbol`, nested svg, patterns, markers, filters and `textPath` all moved at 96.

**Fix (one rule, not per-feature patches):** the SVG user space is the canvas's layout-unit space.
- `RenderInto`/nested viewports/`<image>` push the raw viewBox matrix (no linear pre-division).
- The few things the canvas takes as raw *points* are scaled by `1 / PixelsPerPoint` where they reach it: path coordinates
  (`DrawUserPath`/`PushUserClip` copy-and-scale), pen width and dashes (`ResolveStrokePen`, text-decoration pen).
- `<foreignObject>` content is laid out in layout units, so it is pushed under `scale(1/ppp)`.
- The `DrawUserText` wrapper from the font-size fix is gone: with a raw matrix, `DrawString` is right as-is.

**Trap:** `PixelsPerInchAbsoluteSizingIntegrationTests.Svg_ContentTransformAgreesWithItsOwnClip...` pinned the old convention
(divided linear part); it now asserts viewBox corners map onto the clip rect with no conversion.
Residual differences between scales (<= ~1pt) are whole-layout-unit rounding of font ascent/line height, by design.

Evidence: `LayoutScaleSvgGeometryTests` (painted bounds of nested svg, transform, use, symbol, stroke, text decoration, viewBox,
marker, foreignObject and embedded images equal at 72/96/144, read back with a small content-stream interpreter), the raster diff
above reduced to antialiasing noise, Engine/PeachPDF/PeachBrackets/Svg suites green in Release.

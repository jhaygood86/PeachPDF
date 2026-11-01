# Inline SVG keeps its geometry at a non-default `PixelsPerInch`

Before: at a `PixelsPerInch` other than 72, inline SVG gradients, images, group opacity, `transform`, `<use>`, `<symbol>`, nested
`<svg>`, patterns, markers, filters, text on a path and text decorations were drawn at the wrong size or position (the paths were
right, the rest was off by the layout scale). Now they match the 72 rendering. Documents rendered at 96 may look different (correctly).

# mask-image (gradients and images) on HTML boxes

`mask-image` was not a property at all: it was dropped at parse time, so the element drew solid (PDFium and MuPDF disagreed only because of the unrelated hand-off of an unknown declaration). It is now six real longhands (`mask-image/-mode/-repeat/-position/-size/-origin`, plus parse-only `mask-clip`/`mask-composite`) in `css-properties.json`, with `-webkit-` aliases.

Painting reuses what already existed: `CssImagePainter.Paint` draws each layer (so size/position/repeat/gradients/url images behave exactly like backgrounds) into a tile, and the PDF soft-mask path (`DrawImageAlphaMasked` for alpha, `DrawImageMasked` for luminance, each with its own `/Group`) composes it with the content tile. Opacity/blend mode go through a `BeginLayer` around the masked result.

Trap found by running it on the bitmap canvas: sizing the tiles to the box and shifting them with `PushTransform` made a tiled mask layer disappear, because `BackgroundImageDrawHandler` intersects with `Canvas.GetClip()`, whose initial value is in the tile's own space (not shifted). Tiles now start at the page origin and nothing is shifted. Verified in PDFium and MuPDF (agree, including tiled radial masks, luminance and opacity) and on `RasterCanvas`.

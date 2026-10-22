# Two-value `background-repeat`

`BackgroundImageDrawHandler.DrawBackgroundImage` switched on the layer's repeat string with cases for `no-repeat`, `repeat-x`, `repeat-y` and a default that tiles both ways, so the valid pairs `repeat no-repeat` / `no-repeat repeat` fell into the default. The CSS-OM accepted them, so nothing failed loudly.

`BackgroundLayerResolver.NormalizeRepeat` now reduces a layer value to the keyword form at the top of `DrawBackgroundImage`, which every layer kind (bitmap, SVG, gradient tile) goes through. Normalizing there rather than in the switch also makes the `Pixelated` sampling check (`!= "no-repeat"`) see `no-repeat no-repeat` as `no-repeat`. `space`/`round` pairs are returned unchanged: those keywords are not implemented by the painter at all.

Evidence: `BackgroundRepeatOriginClipLayersIntegrationTests` asserts the tile positions in the PDF (a second tile to the right, none below, and the mirror); `BackgroundRepeatNormalizationTests` covers the reduction.

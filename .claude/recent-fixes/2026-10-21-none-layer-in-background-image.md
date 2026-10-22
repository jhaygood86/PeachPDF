# A `none` layer in `background-image`

`CssValueParser.ParseImages` skipped a layer whose `ParseImage` was null (`none`, or an image that failed to parse), so the painters' `layerIndex` — an index into that shortened list — no longer matched the declared position of the layer in `background-position`/`-size`/`-repeat`/`-origin`/`-clip`/`-attachment`.

`ParseImages` now returns one slot per declared layer, `null` for `none`; the three consumers (`FragmentPainter.Decorations` for boxes, `LayeredBackgroundPainter` for `@page` and margin boxes, `CssBox.Dispose`/`EnsureAuxiliaryImagesLoadedAsync`) skip a null slot but keep the index. That meant the `BackgroundImages` type in `css-properties.json` (`csharpDataType`) became `IReadOnlyList<CssImage?>?` — the generator checks it against the real property, and the cache dictionaries in `PagePainter`/`MarginBoxRenderer` follow. An all-`none` list still returns null.

Evidence: `BackgroundRepeatOriginClipLayersIntegrationTests.NoneLayerInBackgroundImage_...` asserts the image lands at layer 1's top-left and tiles along x only.

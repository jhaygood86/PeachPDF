# SVG `color-interpolation: linearRGB` on `<mask>` is not applied (#1618)

A mask's luminance is always computed from sRGB values. `color-interpolation` is honoured on gradients (stops sampled
through linear light at tree-build time, `SvgTreeBuilder.ExpandLinearLight`) but not on `<mask>`.

**Why:** the mask tile is a `Canvas.CreateTile` image — a vector form XObject on the PDF backend, not pixels — so there
is nothing to linearize before `DrawImageMasked` takes its luminosity (raster path: `RasterCanvas` integer luma weights;
PDF path: `/Luminosity` soft mask). Converting needs a rasterized tile (`BeginRasterSurface` + `FilterOps.ConvertColorSpace`)
or a `/TR` transfer function on the soft mask. Rarely authored; tracked as #1618.

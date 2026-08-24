# A raster `<image>` inside an SVG `<pattern>` doesn't account for `patternTransform`'s scale when downscaling

`PdfGenerateConfig.DownscaleImages` resizes an oversized raster image down to its actual on-page display
size before embedding, computed in `XGraphicsPdfRenderer.Realize` by folding the currently-active CTM's
scale into the image's own destRect (`PdfSharpCore/Drawing.Pdf/XGraphicsPdfRenderer.cs`). This correctly
covers an ordinary CSS `transform:` on an `<img>`/background, and an SVG element/group `transform`, since
both apply through the same page-level CTM the image is ultimately drawn against.

It does not cover a raster `<image>` painted inside an SVG `<pattern>` whose `patternTransform` scales the
tile up. `SvgRenderer.PaintPatternFill` renders the pattern's content into its own Form XObject via
`GraphicsAdapter.CreateTile` at the pattern's native `width`/`height`, then places that tile on the page
wrapped in a separate `patternTransform` scale (`Adapters/GraphicsAdapter.cs`'s `CreateTile` /
`SvgRenderer.cs`'s `PaintPatternFill`). A `<image>` drawn inside the tile's own content stream computes its
downscale target from the CTM realized *within that tile* — which has no visibility into the extra scale
`patternTransform` applies only when the finished tile is placed on the page, in a different content
stream. The image ends up downscaled to fit the tile's *unscaled* size, then visibly blurry once
`patternTransform` scales the tile (and its already-shrunk raster content) back up.

Example that triggers it:

```svg
<pattern width="10" height="10" patternTransform="scale(5)">
  <image href="photo.jpg" width="10" height="10"/>
</pattern>
```

This is a new, narrow gap `DownscaleImages` introduces — before it existed, every image always embedded at
full resolution, so there was no downscale decision to get wrong here. `DownscaleImages = false` avoids it
entirely (nothing is ever downscaled). A correct fix needs `CreateTile`'s caller to communicate the tile's
eventual placement scale down into the tile's own graphics state before any nested raster `<image>` is
painted into it — a real design task (the tile's *vector* content must not be affected, only a nested
raster image's resize decision), not a one-line fix, and out of scope for the change that introduced
`DownscaleImages`. The two other `CreateTile` callers in this codebase (the opacity-group tile and the
`<mask>` luminosity tile, both in `SvgRenderer.cs`) place their tiles 1:1 with no extra scale, so they are
unaffected.

Revisit if a real user document hits this (a raster photo used as SVG pattern fill content with a scaling
`patternTransform` is an uncommon combination).

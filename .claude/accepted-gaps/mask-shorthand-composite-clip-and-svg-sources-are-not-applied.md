# `mask` shorthand, `mask-composite`, `mask-clip`, SVG `<mask>` sources and mask-with-filter are not applied

`mask-image` (images and gradients), `mask-mode`, `mask-repeat`, `mask-position`, `mask-size` and `mask-origin` apply to HTML boxes (`FragmentPainter.Mask.cs`: the element goes into one tile, the mask layers into another, and `DrawImageAlphaMasked`/`DrawImageMasked` compose them). What is left, tracked in #2150:

- the `mask` shorthand: `mask` is registered as an SVG-only longhand, because on an SVG element it names a `<mask>` element;
- `mask-composite` other than `add` and `mask-clip` other than `border-box` are parsed (so `mask-*` text round-trips) but ignored;
- `mask-image: url(#id)` pointing at an SVG `<mask>` from an HTML box;
- an element with both a mask and a `filter` is painted by the filter path and the mask is skipped;
- one `mask-mode` (the first image layer's) is used for the whole mask.

The tiles run from the page origin to the box's far corner rather than being sized to the box and shifted, because a tile's initial clip is in the tile's own space: shifting the canvas intersects every clip pushed inside it with the wrong rectangle (a tiled mask layer vanished on the bitmap canvas).

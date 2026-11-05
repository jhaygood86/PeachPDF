# `image-set()` now paints

`image-set()` in `background-image`, `mask-image`, `border-image-source`, `list-style-image` and `content` used to validate
but paint nothing. It now paints the option it resolves to: paged output takes the highest resolution, a screen document
the smallest option sharp enough for its device pixel ratio (`ImageSelectionOptions`), and an option whose `type()` cannot be
decoded is skipped. A page that carried an `image-set()` as a progressive enhancement over a `url()` fallback
declaration will now show the `image-set()` image. The image's natural size is its pixel size over the resolution it declared (a 200px-wide `2x`
option is 100 CSS px), and `HtmlDocument.UpgradeImagesAsync` moves a zoomed screen document to a denser option, as it does for
`<img srcset>`, without changing the size. `border-image-source` is chosen once, when styles are computed.

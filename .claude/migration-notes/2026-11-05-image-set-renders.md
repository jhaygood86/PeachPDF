# `image-set()` now paints

`image-set()` in `background-image`, `mask-image`, `border-image-source`, `list-style-image` and `content` used to validate
but paint nothing. It now paints the option it resolves to: paged output takes the highest resolution, a screen document
the smallest option sharp enough for its device pixel ratio (`ImageSelectionOptions`), and an option whose `type()` cannot be
decoded is skipped. A page that carried an `image-set()` as a progressive enhancement over a `url()` fallback
declaration will now show the `image-set()` image. A screen document chooses when styles are computed, so a later
`SetRenderScale` zoom does not re-pick a CSS image as it does for `<img srcset>`.

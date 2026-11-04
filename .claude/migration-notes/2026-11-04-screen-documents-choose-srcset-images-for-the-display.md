# Screen documents choose srcset images for the display; resolution queries report the device pixel ratio

**Before** (checked against `v0.9.26-preview1`): every document, `Print` or `Screen`, loaded the candidate with the highest
pixel density from a `srcset` or `<picture>`, and the `resolution`, `device-pixel-ratio` and `-webkit-` forms of the media
features always evaluated against 96 dpi (1 dppx), whatever the host's display.

**Now:**

- `HtmlDocumentOptions.ImageSelection` defaults by medium: `Print` (and every PDF) keeps the highest density, so paged output
  is unchanged; `Screen` loads the smallest candidate that is sharp enough for `HtmlDocumentOptions.DevicePixelRatio` times
  the zoom the host reports through `HtmlDocument.SetRenderScale`. A host that shows a `Screen` document and wants the old
  behaviour sets `ImageSelection = new ImageSelectionOptions { Policy = ImageSelectionPolicy.HighestDensity }`.
- `DevicePixelRatio` (1 by default) feeds the resolution media features and `<source media>`. A page with
  `@media (min-resolution: 2dppx)` rules now matches them on a 2x display; the WinUI and Android views set it from the
  display. At the default of 1 nothing changes.
- A `<source type>` is skipped when the render context cannot decode it (`RenderContext.SupportsImageType`); with a context
  that decodes with PeachImage (the default) every type listed before is still accepted.
- A `srcset` candidate that cannot be loaded now falls back to the next best, then `src`, rather than leaving a blank image.
- `sizes="auto"` is honored on `loading="lazy"` images.

# `<img srcset/sizes>` and `<picture>`/`<source>` (issue 1283)

`Html/Core/Utils/ResponsiveImageSelector.cs` picks the URL (and pixel density) `CssBoxImage` loads;
`CssBoxImage.ImageSource` now routes through it, memoised once per box.

- **Highest density wins**, not DPR-matching: a PDF has no device. `w` candidates get density
  `w / slot`, slot from `sizes` (first entry whose media condition matches, default `100vw`). `100vw`
  is `GetViewportUnitBasis()`, i.e. the page content width (555pt on A4 with 20pt margins), same as the `vw` unit.
- **`<source>` had to become a void element** (`HtmlUtils._list`, plus `track`). The tokenizer is not a
  spec HTML5 parser; without this it nested the following `<img>` inside `<source>` and the `<picture>`
  walk found nothing. UA sheet now hides `source`/`track` (`display: none`).
- **Density-corrected natural size**: `MeasureWordsSize` divides the image's pixel size by the density
  before `MeasureIntrinsicSize`, so a 400px image at `2x` lays out as 200 CSS px.
- Anonymous boxes may wrap the `<img>` (block-in-inline), so the picture lookup skips untagged parents
  and `Collect` descends untagged children when gathering preceding `<source>`s.
- `media` reuses `MediaQueryMatcher` with `container.Media`; an unparsable `media` is a non-match.
- Not done: format sniffing beyond the `type` MIME allow-list; `loading`/`decoding`/`fetchpriority`.
- Evidence: `Integration/ResponsiveImageTests.cs` (layout widths prove which image loaded, plus an
  end-to-end PDF `/Width` check); full net8.0 suite green.

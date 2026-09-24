# SVG `feImage` and the `FillPaint`/`StrokePaint`/`BackgroundImage`/`BackgroundAlpha` filter inputs render (they used to paint the element unfiltered)

**Before:** a `<filter>` containing an `feImage`, or any primitive whose `in`/`in2` (or `feMergeNode in`) named `FillPaint`,
`StrokePaint`, `BackgroundImage` or `BackgroundAlpha`, was rejected as a whole at parse time. The element referencing it painted completely
unfiltered.

**Now:** such a filter is evaluated over pixels by the raster backend (like a blur).

- `feImage` places its image into the primitive subregion by its own `preserveAspectRatio`, or renders the element an `href="#id"` names in the
  filtered element's user space (moved by the subregion's `x`/`y`).
- `FillPaint`/`StrokePaint` are the whole filter region filled with the element's `fill`/`stroke` paint; `none` is transparent.
- `BackgroundImage`/`BackgroundAlpha` are what was painted behind the element: for an inline `<svg>` the page behind it over white paper, then the
  SVG's own earlier content; for an SVG used as an image, only its own earlier content. An SVG whose filters read the backdrop is painted
  in place each time it is used instead of once as a shared form, so its PDF grows with each placement.
- A filter using one of these is now a bitmap at `PdfGenerateConfig.RasterizationDpi`, with the same consequences as any pixel filter (no
  selectable text inside it, larger file, rejected under PDF/A-1 and PDF/X-1a/X-3).

Verified at the previous release tag: `docs/supported-svg-features.md` listed all of the above under "Unsupported SVG Features".

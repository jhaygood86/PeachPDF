# SVG `<filter>` elements with pixel primitives now render (they used to paint unfiltered)

**Before:** a `<filter>` containing any primitive PDF has no operator for — `feGaussianBlur`, `feMorphology`,
`feConvolveMatrix`, `feTurbulence`, `feDisplacementMap`, `feDiffuseLighting`/`feSpecularLighting`, `feDropShadow`,
`feComposite operator="arithmetic"`, `feColorMatrix type="saturate"`/`"hueRotate"` or a cross-channel `type="matrix"`,
`feComponentTransfer` with `gamma`/`table`/`discrete` or a non-identity `feFuncA`, or a per-primitive `x`/`y`/`width`/`height`
subregion — was rejected as a whole at parse time. The element referencing it painted completely unfiltered.

**Now:** such a filter is evaluated over pixels by the raster backend. The element is painted into a bitmap covering the filter
region, every primitive runs on it, and the result is placed on the page at `PdfGenerateConfig.RasterizationDpi` (default 300).
Output that used to show the plain element now shows the blur, shadow, lighting, noise and so on the author wrote.
Consequences a document author can notice:

- The filtered element is a bitmap rather than vector art, so its text is no longer selectable *inside SVG* (text of an HTML
  element under a CSS filter stays selectable), and the file grows with the filter region's pixel area.
- A filter evaluated over pixels honours `color-interpolation-filters` (`linearRGB` by default), so a blur across a colour edge
  looks like the browser's. A filter of only PDF-expressible primitives is unchanged: still vector, still computed in sRGB.
- An `objectBoundingBox` filter region on an element with no measurable bounding box (text) now falls back to the viewport, where
  it used to collapse to a sliver at the origin and hide the element. This applies to vector filters too.
- A document targeting PDF/A-1 or PDF/X-1a/X-3 that uses one of these filters is now rejected (a bitmap with soft edges needs a
  soft mask, which those levels forbid) instead of silently rendering unfiltered.
- `feImage` and the `BackgroundImage`/`BackgroundAlpha`/`FillPaint`/`StrokePaint` inputs are still unsupported; a filter using one
  still paints unfiltered.

Verified at the previous release tag: `docs/supported-svg-features.md` listed all of the above primitives under "Unsupported SVG
Features".

# SVG color management: `icc-color()` and `color-interpolation` on gradients (#189)

- **`color-interpolation-filters` was already implemented** (`FilterPrimitive.LinearRgb`); the issue and docs were stale on that.
- **`icc-color`**: `SvgIccColor.Rewrite` turns `<color> icc-color(name, c...)` into `rgb(...)` (or the leading fallback)
  *before* the value reaches `SvgValueParsers`/`CssValueParser`, so no parser grammar changed and `fill`/`stroke` still go
  through the generated `SvgPropertyRegistry`. Applied in `ApplyCommon` (fill/stroke), `BuildStops`, and the three
  `flood-color`/`lighting-color` reads. Previously a trailing `icc-color(` made `IsColorValid` fail and the element fell
  back to the *inherited* paint, not the written fallback.
- **Profiles**: `CollectColorProfiles` runs *before* `CollectDefinitions` because filters are built eagerly during that walk and
  their `flood-color` may name a profile defined later. `<color-profile href>` is a `data:` URI, or a resource fetched by
  `PrefetchImageResourcesAsync` (`CollectImageHrefs` now also lists color-profile hrefs).
- **Conversion** goes through `PeachImage.IccColorProfile.ConvertToSrgb` with a one-pixel byte buffer (8-bit quantization at
  both ends). A float single-color API in PeachImage would remove that; not required for correctness.
- **`color-interpolation: linearRGB`** on gradients: extra stops (15 per segment, `ColorSpaceConverter` `SrgbLinear`) are
  inserted in `BuildStops`, the same technique as CSS `in srgb-linear`, so the renderer and PDF shading writers are untouched.
  Inheritance walks the definition's ancestors (`_definitionAncestors`, set by `BuildDeferredDefinitions`).
- **Mask luminance (#1618)**: a mask tile is a vector form on the PDF backend, so its pixels cannot be re-encoded before
  `DrawImageMasked` takes the luminosity. Instead `LinearizeMaskContent` converts the mask content's *colors* (solid
  fill/stroke, and a copy of each referenced gradient with converted stops, since the original may paint elsewhere) to
  linear light at build time, after every gradient exists (`_linearMasks`, drained at the end of `BuildDeferredDefinitions`).
  Luminosity of the converted colors equals luminosity of the linear-light ones for opaque content. Patterns, raster
  `<image>`s and `<use>` targets in mask content keep sRGB values; alpha compositing inside the mask still happens in sRGB.
- Evidence: `SvgColorManagementTests` (22 tests).

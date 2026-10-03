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
- **Not done**: mask luminance (see accepted gap, #1618).
- Evidence: `SvgColorManagementTests` (22 tests).

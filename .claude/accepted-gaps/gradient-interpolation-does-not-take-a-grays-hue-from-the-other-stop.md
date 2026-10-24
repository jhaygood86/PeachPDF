# A gray gradient stop keeps a noise hue in `oklch` interpolation

A gradient in `lch` takes a gray stop's missing hue from the other stop, as `color-mix()` does (`ColorSpaceConverter.Interpolate` calls `ColorSpaceMath.ResolveMissingHues` for `lch`). `oklch` is deliberately left alone,
and so are `hsl` and `hwb` (a gray's hue there is 0, not powerless): `ColorSpaceConverter.Interpolate` interpolates the hue of a gray `oklch` stop as converted, which for a gray is whatever angle the conversion leaves behind.

Chrome is not consistent here, and the measurement is easy to misread:

- `linear-gradient(in lch to right, #808080, red)` has Chrome's midpoint at `rgb(200, 97, 71)`, the same as `color-mix(in lch, #808080, red)`: the gray's hue is missing, as CSS Color 4 says. PeachPDF matches.
- `linear-gradient(in oklch to right, #808080, red)` has Chrome's midpoint at `rgb(178, 118, 8)`, where `color-mix(in oklch, #808080, red)` is `rgb(198, 99, 86)`: Chrome apparently interpolates the gray's own
  hue (a hue near 90 degrees would explain the midpoint; it is the noise of the gray's conversion) in the gradient, and the hue of red in the mix. PeachPDF's noise hue for the gray differs from Chrome's (`rgb(172, 102, 172)` at the midpoint, up to 90 of 255 off).
- `#6c757d` to `#adb5bd` in `oklch` (two grays) matches Chrome to a few steps, as their noise hues are close.

Applying the missing-hue rule to `oklch` would make it follow the specification text and break the match with Chrome's own behaviour for two grays; matching Chrome for a gray-to-colour gradient needs a measured rule for Chrome's
gradient path that was not found. Revisit with a measurement of that path. Tracked in #1773.

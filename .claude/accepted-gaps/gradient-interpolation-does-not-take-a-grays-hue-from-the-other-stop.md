# A gray gradient stop keeps a noise hue in `lch` and `oklch` interpolation

`ColorSpaceConverter.Interpolate` (gradients in `lch`/`oklch`, and `hsl`/`hwb`) interpolates the hue of a gray stop as converted, which for a gray is whatever angle the conversion leaves behind. A browser takes
the hue of a gray as missing and uses the other stop's (CSS Color 4, interpolating with a missing hue). `color-mix()` does this since the change that made animated colours mix before the gamut clip (`ColorSpaceMath.ResolveMissingHues`
in `Color.MixComponents`); gradients were left alone, because Chrome itself is not consistent here and the measurement is easy to misread:

- `linear-gradient(in lch to right, #808080, red)` has Chrome's midpoint at `rgb(200, 97, 71)`, the same as `color-mix(in lch, #808080, red)`: the gray's hue is missing, as above.
- `linear-gradient(in oklch to right, #808080, red)` has Chrome's midpoint at `rgb(178, 118, 8)`, where `color-mix(in oklch, #808080, red)` is `rgb(198, 99, 86)`: Chrome apparently interpolates the gray's own
  hue (a hue near 90 degrees would explain the midpoint; it is the noise of the gray's conversion) in the gradient, and the hue of red in the mix. PeachPDF's noise hue for the gray differs from Chrome's (`rgb(172, 102, 172)` at the midpoint, up to 90 of 255 off).
- `#6c757d` to `#adb5bd` in `oklch` (two grays) matches Chrome to a few steps, as their noise hues are close.

So applying the missing-hue rule to gradients would fix `lch` and break the `oklch` match with Chrome's own quirk; doing it per space needs a measured rule for Chrome's gradient path that was not found. The
gap is the difference between gradient and `color-mix()` for the same gray, and the up-to-90 error of a gray-to-colour `oklch` gradient. Not tracked as an issue yet: it is a difference from a browser's
behaviour rather than one from the specification text (which asks for the missing hue), and Chrome does not follow the text itself for `oklch`.

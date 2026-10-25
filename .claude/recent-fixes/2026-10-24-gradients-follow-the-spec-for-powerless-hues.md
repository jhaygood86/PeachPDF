# Gradients take an achromatic stop's hue from the other stop in every polar space (#1773)

The `lch`-only version of this (see the gradient premultiplication change) left `oklch` alone because Chrome's `oklch` gradient interpolates a gray's own noise hue (`linear-gradient(in oklch, #808080, red)` is rgb(178, 118, 8) at the midpoint in Chrome, where `color-mix(in oklch, #808080, red)` is rgb(198, 99, 86)). The decision is to follow CSS Color 4 (§4.4.1: a powerless hue is missing; Interpolating with Missing Components: a missing component takes the other colour's value) and accept the difference from Chrome, so a gradient and a `color-mix()` of the same stops now agree.

- `ColorSpaceConverter.Interpolate` calls `ColorSpaceMath.ResolveMissingHues` for every space (it is a no-op for the rectangular ones).
- `IsHuePowerless` gained the two legacy polar spaces: `hsl()` with a saturation of 0.001 or less (§7) and `hwb()` whose whiteness plus blackness is 99.999 or more (§8). This also changes `color-mix(in hsl|hwb, ...)` and every other caller of `ResolveMissingHues`, which is what the specification's own `color-mix(in hsl, white, green)` example needs.
- The `lch`/`oklch` limit stays the 0.02 found by bisection against Chrome for `color-mix()` (the specification's own epsilons, 0.0015 and 0.000004, sit below the noise an 8-bit gray leaves after conversion).
- **Evidence:** `#808080` to red in `oklch` is now rgb(198, 99, 86) at the midpoint, the `color-mix()` value; Engine and PeachPDF suites green in Release.

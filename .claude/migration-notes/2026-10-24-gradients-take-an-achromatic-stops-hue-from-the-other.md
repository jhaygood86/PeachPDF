# A gray gradient stop takes its hue from the other stop in `oklch`, `hsl` and `hwb`

Before: `linear-gradient(in oklch, #808080, red)` (and `in hsl`/`in hwb`) interpolated the gray's own hue, whatever angle the conversion left, so the middle of the gradient could pass through an unrelated hue (up to 90 of 255 off).
Now: an achromatic stop's hue is missing and the other stop's stands in for it, as CSS Color 4 says and as `color-mix()` already did, so a gradient and a `color-mix()` of the same two colours agree. This differs from Chrome's `oklch` gradient, which keeps the gray's own hue. `color-mix(in hsl|hwb, ...)` with an achromatic operand changes the same way.

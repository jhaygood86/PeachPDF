# Colours in a CSS animation mix in Oklab when one of them is not a legacy colour

With `PdfGenerateConfig.AnimationProgress` set, two colours used to be mixed premultiplied in sRGB whatever syntax they were written in. A pair
with a `lab()`, `lch()`, `oklab()`, `oklch()` or `color-mix()` colour now mixes premultiplied in Oklab, as CSS Color 4 and the browsers do:
`lab(50 40 20)` to `lab(70 -30 30)` half way is a different, cleaner colour than the sRGB average of the two, and so are `#f00` to
`oklch(0.5 0.15 260)` and `color-mix(in oklab, red, blue)` to `#0f0` (up to 49 of 255 per channel apart for `#f00` to `oklch(0.5 0.15 260)`, and 75 for `color-mix(in srgb, red, blue)` to `#0f0`, in the pairs measured). Pairs
written only in hex, named colours, `transparent`, `rgb()`, `hsl()` or `hwb()` mix in sRGB as before, and `currentcolor` counts as a legacy
colour whatever the box's `color` is. A colour outside the sRGB gamut (`oklch(0.7 0.3 150)`, `lch(50 60 200)`) mixes from the coordinates it was written with and is clipped once at the end, as in a browser, rather than from its already clipped value. A static `color-mix()` with an operand outside the gamut changes the same way (`color-mix(in lch, oklch(0.7 0.3 150) 80%, red)` is `rgb(32, 186, 0)`, Chrome's value, not the mix of the clipped operand), and an in-gamut `lab()` operand is no longer rounded to 8 bits before it is mixed (at most a step of 1). Two smaller changes to the static colours the same code reads: a `lab()`, `lch()`, `oklab()` or `oklch()` lightness out of its range is clamped, and a negative chroma is 0, before it is mixed (as in a browser; `oklch(0.5 -0.2 100)` is a gray, not a colour with the hue turned half way round), and a gray operand of an `lch` or `oklch` `color-mix()` takes the other operand's hue.

Before this change (`git show v0.9.22:docs/html-css-support.md`) the docs said "the function's own color space is not used for the mix".

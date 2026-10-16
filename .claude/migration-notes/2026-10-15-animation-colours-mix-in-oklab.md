# Colours in a CSS animation mix in Oklab when one of them is not a legacy colour

With `PdfGenerateConfig.AnimationProgress` set, two colours used to be mixed premultiplied in sRGB whatever syntax they were written in. A pair
with a `lab()`, `lch()`, `oklab()`, `oklch()` or `color-mix()` colour now mixes premultiplied in Oklab, as CSS Color 4 and the browsers do:
`lab(50 40 20)` to `lab(70 -30 30)` half way is a different, cleaner colour than the sRGB average of the two, and so are `#f00` to
`oklch(0.5 0.15 260)` and `color-mix(in oklab, red, blue)` to `#0f0` (up to 49 of 255 per channel apart for `#f00` to `oklch(0.5 0.15 260)`, and 75 for `color-mix(in srgb, red, blue)` to `#0f0`, in the pairs measured). Pairs
written only in hex, named colours, `transparent`, `rgb()`, `hsl()` or `hwb()` mix in sRGB as before, and `currentcolor` counts as a legacy
colour whatever the box's `color` is. A colour outside the sRGB gamut (`oklch(0.7 0.3 150)`, `lch(50 60 200)`) mixes from the coordinates it was written with and is clipped once at the end, as in a browser, rather than from its already clipped value.

Before this change (`git show v0.9.22:docs/html-css-support.md`) the docs said "the function's own color space is not used for the mix".

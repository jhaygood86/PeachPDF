# Static `lab()`, `lch()`, `oklab()`, `oklch()` and `color-mix()` colours: clamped when read, mixed before the clip

Found while making animated colours mix from the coordinates they were written with (see the migration note for Oklab mixing in animations); the same colour readers serve every static colour in a
document, so these change a plain stylesheet too. All of them move the colour toward what Chrome renders (`git show v0.9.22:src/PeachPDF/CSS/Extensions/ColorFunctionExtensions.cs` has none of them). Measured
by rendering the TestHarness showcases and a 2,650-colour corpus with the library at `upstream/main` and at this change, against Chrome's canvas readback: 222 showcases, one swatch differs (the
`modern_colors` swatch `color-mix(in oklch, oklch(0.7 0.2 30), oklch(0.7 0.2 260))`, `rgb(207, 111, 219)` before, `rgb(218, 106, 225)` now, Chrome's); in the corpus 1,203 colours were more than 3 of 255 off
Chrome before, 248 are now (38 without the `hsl`/`hwb` mixes, a separate gap).

- **A lightness out of its range is clamped, and a negative chroma is 0, when the colour is read** (CSS Color 4 section 9): `lab(113.6 7.9 -99)` was 55 of 255 off Chrome and is within 1, `lch(53.6 -29.3 287)`
  was 50 off, `oklch(120% .2 30)` 40 off, and `oklch(0.5 -0.2 100)` is a gray, not a colour with its hue turned half way round.
- **A `color-mix()` operand outside the sRGB gamut is mixed before the result is clipped,** as a browser does: `color-mix(in lch, oklch(0.7 0.3 150) 80%, red)` was `rgb(114, 184, 0)` and is
  `rgb(32, 186, 0)`. An in-gamut `lab()`, `lch()`, `oklab()` or `oklch()` operand, or a nested `color-mix()`, is no longer rounded to 8 bits before the mix (a change of at most 1 of 255).
- **A gray operand of an `lch` or `oklch` `color-mix()` takes the other operand's hue** (a gray's hue is missing; Chrome's limit is a chroma of 0.02 or less, so a palette's near-grays count): `color-mix(in oklch, #f8f9fa, red)`
  was `rgb(227, 164, 244)` and is `rgb(255, 158, 142)`; two grays mix at hue 0. A gray written in that same space keeps its own hue in a browser, which is not done. `palette-mix()` shares the code: its
  colours move by up to 6 of 255 for near-gray palette entries.
- **`longer hue` between two equal hues goes half way round:** `color-mix(in lch longer hue, red, red)` was red and is `rgb(0, 163, 251)`, and a gradient `linear-gradient(in oklch longer hue to right, red, red)`
  was flat red and is a full turn of the hue wheel, as in Chrome.

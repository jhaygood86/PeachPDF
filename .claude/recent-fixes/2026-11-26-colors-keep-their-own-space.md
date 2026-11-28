# A canvas color keeps the space it was written in

**Load-bearing idea.** The HTML standard has a canvas read a color back in CSS serialization, not as 8-bit sRGB: `color(display-p3 0.96 0.76 0.79)` stays that, `lab(29% 39 20)` reads `lab(29 39 20)`, `none` stays `none`, and the result of `color-mix(in srgb, red, blue)` or `rgb(from red g r b)` is `color(srgb 0.5 0 0.5)` / `color(srgb 0 1 0)`. Nothing in the engine serialized a color, but it already carried everything needed: `ColorSpaceMath.WrittenCoords` (space, coordinates, a mask of `none`), `Color.MixComponents` (the mix in its interpolation space, before the clip to sRGB) and `RelativeSpec` (a relative color's components before they are built into a `Color`). `AuthoredColor` + `ColorFunctionExtensions.TryResolveAuthored` read those, and `AuthoredColor.ToCssString()` serializes. The legacy forms (names, hex, `rgb()`, `hsl()`, `hwb()`, a mix in `hsl`/`hwb`, a relative `hsl()`/`hwb()`) are not authored colors and keep the `#rrggbb`/`rgba()` path. What is painted is still the 8-bit sRGB color, so out-of-gamut colors are clipped when drawn.

**Traps found by running it.**
- `ParseRelative` returned a built `Color`; the authored path needed the numbers before that. It is split into `TryEvaluateRelative` + `Build`; do not duplicate the evaluation.
- A relative color's `alpha` keyword came from the origin's 8-bit alpha (128/255 = 0.501961) and serialized that. It now reads the origin's authored alpha.
- Float noise: 29% of 100 is 28.999999 as a float. Components serialize at six significant digits (`G6`), not six decimal places.
- Where the shim runs matters: `CSS` is defined late in `InstallWindow`, so `ColorScripts` runs after it in the runtime's constructor (an `Object.defineProperty called on non-object` at startup broke every script, which showed up as every canvas test failing).
- A color given as an object is read when the style is set (a dictionary `{r, g, b, a}` of 0 to 1 numbers, a `CSSRGB`, a `CSSHSL`): changing the object later does not change the style. A dictionary with missing `b`, or a non-number alpha, is not a color and leaves the style alone.

**Verified.** html-canvas 1,616 files / 2,714 subtests (+19 files, +25 subtests); css-color 230 to 238 reftests. 59 canvas color tests (the whole of WPT `color.window.js`'s table, mixes, relative colors with `currentcolor`, object forms, `CSSRGB`/`CSSHSL`). Engine, PDF and Scripting suites pass.

**Follow-up (same day).**
- A gradient with a stop that is not legacy sRGB (`ScriptDocument.InterpolateGradientStops`, reusing `ColorSpaceConverter.Interpolate`) goes from stop to stop in Oklab, as CSS does: the engine's CSS gradients already resampled like this (15 colors a segment). The gradient is flagged when any stop's `TryParseColor` returns a serialization.
- `currentcolor` in a gradient stop: the gradient remembers the canvas element that made it (`CanvasGradient.Owner`).
- `CSSHWB`, `CSSLab`, `CSSLCH`, `CSSOKLab`, `CSSOKLCH` and `CSSColor` stand for the CSS text of their color (`_colorText`, read when the style is set), so they read back serialized with no second color path.
- Two grammar leaks the WPT `invalid.*` tests found: `Color.FromHex` accepted any characters (`#fg00`), now `Color.IsHexColor` is checked where a hash becomes a color; and the CSS lexer swallowed the point of `1.` (`NumberRest` backed up one character, not two), so `rgba(255, 0, 0, 1.)` parsed as alpha 1. A point belongs to a number only when a digit follows (CSS Syntax 4.3.12). One existing tokenizer test pinned the swallowed point as a "pre-existing quirk" and was updated.
- html-canvas 1,624 files / 2,722 subtests.

**Not done.** Painting a wide-gamut color wider than sRGB, and a canvas whose pixels are display-p3 (`colorSpace: 'display-p3'`, `colorType: 'float16'`): that needs a float color through every backend and is Phase 7; interpolating a gradient with straight rather than premultiplied alpha (canvas gradients interpolate non-premultiplied, CSS gradients premultiplied: `2d.gradient.interpolate.coloralpha`); patterns and zero-size gradients (Phase 7).

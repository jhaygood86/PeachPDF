# rgb() components were truncated or misread, and the deprecated system colors were not aliases

## rgb()
Two readers handle a colour string. `CssValueParser.GetColorByRgb`/`GetColorByRgba` (a quick reader) understand whole numbers only: `rgb(0% 50% 0%)` read the percentages as `0, 50, 0` and `rgb(0 128.0 0)` failed. `ValueExtensions.ToRgbComponent` (the token reader) truncated a percentage (`50%` of 255 is 127.5, a browser draws 128) and took a percentage token for an integer. Now:
- `ToRgbComponent` takes a number or a percentage, clamps and rounds to the nearest byte (css-color-4 section 5.1).
- `HasOnlyIntegerRgbComponents` keeps the quick reader to the one shape it understands (three in-range whole numbers, then optionally an alpha with decimals) and everything else goes to the token reader.

## Deprecated system colors
`ActiveBorder`, `Window`, `ThreeDFace`... had values of their own. css-color-4 section 6.2 makes them aliases of the current names (`ButtonBorder`, `Canvas`, `ButtonFace`...), so they compute to the same color, in the dark scheme too. `Colors.GetColor` maps the alias first.

css-color 167 -> 228 (+61: a98rgb, color-mix percents, the whole `deprecated-sameas` set, `rgb-*`, `rgba-*`).

## Not done
`hsl()` negative saturation clamping, `hwb-003..005`, `lab`/`lch`/`oklab`/`oklch` out-of-gamut tests (004..010), `display-p3-linear`, relative colors on `currentcolor`, `color-mix(currentcolor)` and the `light-dark()` ones were not looked at.

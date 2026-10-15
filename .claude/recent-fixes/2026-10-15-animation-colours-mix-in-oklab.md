# Animated colours mix in Oklab when one of the two is not a legacy colour

Tracked in #1770, under #1672 (the "colours mix only in premultiplied sRGB" item; related: #1768, #1769). `CssValueInterpolator.MixColors`
mixed every colour premultiplied in sRGB, because a colour token carries only the parser's 8-bit sRGB value.

## What Chrome does (the oracle: paused animations with a negative delay; the computed colour drawn on a canvas and read back)

Measured (Chrome 155.0.8059.40) on 33 pairs at 0.25, 0.5 and 0.75; 25 of them (75 rows) are in `AnimationColourInterpolationSpaceTests`. 43 of the
rows fail on `main`'s sRGB mix (15 cases; the largest misses are an overshoot row at 94 of 255 per channel and a `color-mix()` pair at 75), and the
32 that pass are controls: the legacy pairs, a few modern pairs where the two spaces coincide within the tolerance (`lab()` to `transparent`, the
`currentcolor` rows) and the milder overshoot rows:

- **Both colours legacy: sRGB, premultiplied.** Legacy means hex, named, `transparent`, `rgb()`, `hsl()` (hue wraps do not matter: it is not mixed
  through the hue) and `hwb()`. The computed value is serialised as `rgb()`/`rgba()`.
- **Any other colour: Oklab, premultiplied, rectangular** (no hue arc, even for `lch()`/`oklch()` pairs: `oklch(0.6 0.2 10)` to `oklch(0.6 0.2 350)`
  half way is a chroma-0.197, hue-0 colour, not the arc through 0 degrees). Non-legacy is `lab()`, `lch()`, `oklab()`, `oklch()`, `color()` (even
  `color(srgb ...)`), `color-mix()` in any space (even `in srgb` or `in hsl`), and relative colour syntax (`rgb(from red r g b)`). One such colour
  in the pair is enough: `#f00` to `color(display-p3 0 0 1)` is an Oklab mix. The computed value is serialised as `oklab(...)`.
- **`currentcolor` is legacy for choosing the space**, whatever it resolves to: white to `currentcolor` on a box with `color: lab(50 40 20)` is an
  sRGB mix (`rgb(220, 172, 171)` half way), where `oklab(...)` to `currentcolor` on a black box is an Oklab one (the other side decides).
- **`transparent` and alpha:** a `lab()` to `transparent` mix keeps the lab colour at half alpha (`oklab(... / 0.5)`), i.e. premultiplied.

## The load-bearing ideas

- **An `InOklab` flag on the token, set by function name** (`TakesAMixIntoOklab`): `lab`, `lch`, `oklab`, `oklch`, `color`, `color-mix`, and a function
  whose first argument is `from` (relative colour). The mix is Oklab when either token has it. The flag is the whole classification; there is no
  need to keep the original colour space, because Oklab is the only non-sRGB space an animation uses.
- **`MixInOklab` converts the 8-bit sRGB endpoints with `ColorSpaceMath.ToSpace(..., Space.Oklab)`, premultiplies each channel by its alpha, mixes,
  divides by the mixed alpha, and writes `oklab(L a b / alpha)`.** Writing `oklab()` text rather than `rgb()` lets the property setter map an
  out-of-gamut result back into sRGB the way it does a static `oklab()`. It does not reuse `ColorSpaceConverter.Interpolate`: that one works
  on 8-bit `PaintColor`s and does not premultiply (it is the gradient code).
- **`currentcolor` goes through the same token path** (`CurrentColorToken` reads the box's `color` as a token), so it must clear the flag or a
  `lab()` text colour would make the mix modern; the test row M20 pins it.

## Not done

- `color()`, relative colour syntax and `rgb(none ...)` are not read by the colour parser at all (static or animated; #1772), so a keyframe holding one is
  dropped; the colour-space part is ready for them once the parser reads them (their rows were measured and left out of the tests, and
  `TakesAMixIntoOklab`'s `color` and `from` cases are dead code until then). `color-mix(in hsl)` and `in hwb` are read but resolve to black, static too (#1769).
- The endpoints are the parser's 8-bit sRGB, so a `lab()`/`oklab()`/`oklch()` outside the sRGB gamut is mapped into it before the mix. For a vivid
  pair that is up to about 50 of 255 off Chrome mid-animation (a review measured `oklch(.6 .2 100)` to `oklch(.6 .2 300)` at 52 a quarter of the way in, which is *worse*
  than the old sRGB mix, 14, for that pair); row M19 was left out for the same reason, and the overshoot rows use in-gamut ends. Fixing it needs
  the unclamped components in the token.
- **The lightness is clamped to 0..1** (`MixInOklab`): with an easing that overshoots, Chrome holds L at the end of its range and keeps a and b, where
  the colour parser takes `oklab(1.2 ...)` as it comes and washes out to white (or black). The O* rows pin it (10 fail without it).
- A gradient `background-image` is not interpolated by Chrome at all (it flips at 50%); PeachPDF mixes the stops, as before this change.
- `hsl(none ...)` and `hwb(none ...)` are mixed in Oklab by Chrome and count as legacy here.

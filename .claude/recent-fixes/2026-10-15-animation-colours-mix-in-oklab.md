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
- **An endpoint written as `lab()`, `lch()`, `oklab()`, `oklch()` or `color-mix()` is mixed from the Oklab coordinates it was written with, not from its
  clipped 8-bit value** (token field `Oklab`, from `CssValueParser.TryGetOklab` over `ColorFunctionExtensions.TryResolveOklab`). The first version converted
  the parser's 8-bit sRGB, which for a colour outside the sRGB gamut is the clipped colour: against Chrome over 640 points (0.1 to 0.9, 128 series with eight legacy
  partners and out-of-gamut colours both ways) that was up to 118 of 255 off with 440 points more than 3 off, and worse than `main`'s sRGB mix for 147 of
  them; reading the coordinates leaves 5 points more than 3 off (max 41, all one pair whose `color-mix()` operand was itself out of gamut: see the next point) and none worse than `main`.
  The grammar is not repeated: `ParseRect`/`ParsePolar`/`ParseColorMix` were split into component readers (`TryRectComponents`, `TryPolarComponents`,
  `TryColorMixOperands`) that the colour parse and the Oklab read share, and `Color.Mix` into `Color.MixComponents` (the mix before the sRGB clip) plus the clip.
  `ColorSpaceMath.ToOklab` converts through XYZ for the Lab family, so the out-of-range coordinates survive. The in-gamut cases improve too: they no longer go
  through 8-bit rounding.
- **Found by comparing 207 static `color-mix()` colours with Chrome** (the animation path shares their parser, so each applies to both):
  - **A lightness out of its range and a negative chroma are clamped when the colour is read** (`TryRectComponents`/`TryPolarComponents`: L to 0..reference, chroma to at least 0), as CSS Color 4
    section 9 does. The 8-bit route had done it by accident (the clip), so the first Oklab-coordinates version made `color-mix(in oklab, lab(150 0 0), black)` 64 of 255 lighter than Chrome. The clamp is in
    the shared readers, so a static `oklch(0.5 -0.2 100)` is a gray now, not the hue turned half way round.
  - **A nested `color-mix()` is read once per level.** `ParseMixOperand` used to resolve the colour and then the Oklab, each re-reading the operands: the work doubled with each level (a depth-18 chain took
    430 ms, 0 before). `ResolveColor` returns both in one pass; `ToResolvedColor` and `TryResolveOklab` are views of it, and the Oklab is only built when asked for.
  - **`ToOklab` and `ToSpaceFromOklab` hold an out-of-range colour in every space a mix result can leave the gamut in** (sRGB, linear sRGB, Display P3, XYZ; hsl and hwb get the clipped colour).
    `Linearize`/`Delinearize` clamp, so these use an odd-symmetric transfer function (CSS Color 4 section 10.2). A nested `color-mix(in xyz, <wide>, blue)` had been 23 of 255 off and a `color-mix(in srgb, <wide>, ...)` 5.
  - **A gray's hue is missing in an `lch`/`oklch` mix** (`ColorSpaceMath.IsHuePowerless`, used by `Color.MixComponents`): the other operand's hue stands in, as in a browser for a gray from another space. The limit is
    Chrome's, found by bisection: a chroma of 0.02 or less is missing (in lab for lch, in oklab for oklch), 0.021 is not, where the specification's own epsilons are 0.0015 and 0.000004. Limits set from the
    conversion noise alone (0.05 and 0.0004) left every Bootstrap-style near-gray (`#f8f9fa`, `#6c757d`) with a noise hue, 30 to 105 of 255 off in an oklch mix with red. The noise was measured (8-bit gray
    3e-5 lch and 9e-5 oklch; an Oklab gray read in lch 0.033, since the Oklab matrices do not quite share lab's white), so an Oklab chroma under 5e-5 is snapped to a lab gray in `ToSpaceFromOklab`.
    A gray written in the same space keeps its written hue in a browser; that stays a gap.
  - **`longer hue` between equal hues goes half way round** (`InterpolateHue`: the difference range is `-180 < d <= 0`, which adds a turn, not `< 0`). It had been wrong for `color-mix(in oklch longer hue, red, red)`
    all along, and a gray's substituted hue is always equal to the other's, so every gray with `longer hue` hit it.
  - **`currentcolor` from a `color` outside the gamut mixes unclipped against a legacy colour.** The token's `Rgba` holds the unclipped sRGB (a channel below 0 or above 255; `MixColors` clips the result), as
    Chrome does: `color: lch(50 60 200)`, `red` to `currentcolor` was 64 of 255 off, now 0 to 1 (K rows, 18 series).
- **`MixInOklab` converts any other endpoint's 8-bit sRGB with `ColorSpaceMath.ToSpace(..., Space.Oklab)`, premultiplies each channel by its alpha, mixes,
  divides by the mixed alpha, and writes `oklab(L a b / alpha)`.** Writing `oklab()` text rather than `rgb()` lets the property setter map an
  out-of-gamut result back into sRGB the way it does a static `oklab()`. It does not reuse `ColorSpaceConverter.Interpolate`: that one works
  on 8-bit `PaintColor`s and does not premultiply (it is the gradient code).
- **`currentcolor` goes through the same token path** (`CurrentColorToken` reads the box's `color` as a token), so it must clear the flag or a
  `lab()` text colour would make the mix modern; the test row M20 pins it.

## Not done

- `color()`, relative colour syntax and `rgb(none ...)` are not read by the colour parser at all (static or animated; #1772), so a keyframe holding one is
  dropped; the colour-space part is ready for them once the parser reads them (their rows were measured and left out of the tests, and
  `TakesAMixIntoOklab`'s `color` and `from` cases are dead code until then). `color-mix(in hsl)` and `in hwb` are read but give a wrong colour for many operand pairs (black for `red` and `blue`), static too (#1769).
- **A `color-mix()` operand outside the sRGB gamut reaches the mix unclipped too** (`ParseMixOperand` returns its Oklab beside the `Color`, `Color.MixComponents` takes
  it through `ColorSpaceMath.ToSpaceFromOklab`, which converts via XYZ and uses an odd-symmetric sRGB transfer function; `Linearize`/`Delinearize` clamp, so `ToSpace` could not
  hold it). The first version resolved operands to 8-bit `Color`s first: a first review measured 460 more points (a heavy-weight operand, 50 to 95%, in every mix space, both
  orders) and it was up to 100 of 255 off (273 of 400 points over 3), against 5 (6 points over 3) now; of the ten `N*` rows in the tests, seven were more than 50 off before (1, 7 and 19 at the mild points). It also changes the static `color-mix()` of
  an out-of-gamut operand (Chrome's own values are in `CssValueParserOklabTests`), and an in-gamut `lab()` operand is no longer rounded to 8 bits before the mix. The maintainer's review found the general case (up to 97 of 255
  off, 3,350 points over 670 series at 0.1, 0.25, 0.5, 0.75 and 0.9, `oklch(0.7 0.3 150)` to `rgba(230,60,70,.6)` at 97 a quarter of the way in against
  `main`'s 33); a first measure at 0.25, 0.5 and 0.75 only had seen 52, which is why the ends of the run matter. The `G*` rows pin the fix.
- **The lightness is clamped to 0..1** (`MixInOklab`): with an easing that overshoots, Chrome holds L at the end of its range and keeps a and b, where
  the colour parser takes `oklab(1.2 ...)` as it comes and washes out to white (or black). The O* rows pin it (10 fail without it).
- A gradient `background-image` is not interpolated by Chrome at all (it flips at 50%); PeachPDF mixes the stops, as before this change.
- `hsl(none ...)` and `hwb(none ...)` are mixed in Oklab by Chrome and count as legacy here.

# CSS animation snapshot: what is deliberately not modelled

`PdfGenerateConfig.AnimationProgress` renders every CSS animation as one frame (see
[the recent fix](../recent-fixes/2026-10-09-css-animations-as-a-still-frame.md) for the design). These are left out on
purpose; each is an addition to the sampler, not a rewrite. The genuine spec deviations among them are tracked in
#1672 (the feature itself is #1671).

- **Per-animation progress and an absolute time.** One global fraction, taken of each animation's *own* run. Two animations
  with different durations land at the same fraction, not the same time. A `@keyframes`-name-keyed override and an
  "at 3s on the timeline" option are the natural next steps.
- **`animation-delay`, `animation-fill-mode`, `animation-play-state` do not change the result** (the frame is inside the
  run). Only an animation with no run (zero duration / iteration count) consults the fill mode.
- **`transition` never runs.** A transition starts from a style *change*; a freshly loaded document has none. Nothing to
  render, so nothing was built. The properties are still parsed.
- **Interpolation is structural, on text.** Colours mix in premultiplied sRGB only (no `color-mix`/oklab interpolation
  space), and only the colour syntaxes `CssValueParser.TryGetColor` understands are mixed - any other flips at 50%.
- **The matrix part of a `transform` mix cannot run before layout for some inputs.** Lists are paired function by function
  (CSS Transforms 1 §11-12) and what is left is mixed as matrices (§13, `TransformMatrixInterpolator`). That last step
  flips at 50% (tracked in #1753) for:
  - a `%` argument or a container query unit (`cqw`, `cqi`, ...) in the matrix part (they resolve against a size no layout has
    given yet);
  - `perspective()` against another function, or a `matrix3d()` with perspective terms (the decomposition has no
    perspective part);
  - a collapsed dimension (a zero scale, a `matrix()` with a zero determinant - the spec makes a non-invertible matrix
    discrete).

  A list with a `calc()` argument is not read as plain numbers, so it is not paired at all: when the two do not
  match function for function, each is composed whole and mixed as a matrix, which loses a rotation of a full turn or more.
- **Two `perspective()` functions mix their lengths, not their reciprocals** (#1753). CSS Transforms 2 mixes `1/d`, so
  `perspective(100px)` against `perspective(200px)` is `perspective(133.3px)` there and `perspective(150px)` here; equal
  arguments (the usual spinning card) are unaffected.
- **The matrix mix differs from the spec's algorithm in the corners** (#1753). The 2D decomposition is the 3D one's
  Gram-Schmidt form (a skew ratio and an orthogonal remainder) where §13.2 interpolates the four entries of the
  de-rotated 2x2 matrix, so a pair with a skew on a side mixes the same ends and slightly different middles; a rotation
  exactly 180 degrees from the other takes the positive way where the spec sets the angle to 0-360 first; the three skews
  recompose as one matrix, not three elementary ones; the quaternion takes the short arc (`Quaternion.Slerp` negates a
  negative dot product) where the spec's formula would go the long way, which was not checked against a browser.
- **Font-relative lengths in a matrix-mixed transform are resolved when the animation is applied** (#1753). `em`, `ex`, `ch` and
  `rem` in the matrix part of a `transform` mix resolve against the box's font at the animation step, before the
  `!important` phase and before a `font-size` the same animation sets later; a pair mixed per function keeps the text
  and resolves at paint. They differ only when the font size changes after the animation step.
- **Not animated:** custom properties registered with `@property`, `animation-composition` (`add`/`accumulate`),
  `animation-timeline`/`animation-range` (scroll-driven), `@keyframes` `timeline-range` percentages, and the declarative
  document-building API (its cascade is a different entry point and does not call the applier).
- **`!important` custom properties are not seen by `var()` in an animated property.** The animation step sits before the
  author-`!important` phase, so for a property the animation owns - or an `animation-*` property - `var(--x)` resolves
  against the custom properties as the normal phases left them. `#a { --w: 100pt; width: var(--w) }` plus
  `#a { --w: 300pt !important }` therefore gives the implicit keyframe 100pt, not 300pt. Properties the animation does not
  touch resolve at the very end of the cascade and are unaffected - except that a shorthand holding `var()` is settled as a
  whole when any one of its longhands is animated (`margin: var(--m)` with only `margin-left` animated settles all four
  margins early). Fixing it means resolving custom properties' important declarations ahead of the animation step.
- **Overshoot is clamped only for a whole-value, non-negative property.** An easing that overshoots is followed past the end
  value, and a width, padding, border width, radius, `font-size` and the like stop at 0 (a fixed list in
  `CssValueInterpolator`). A negative argument inside a function (`blur(-5px)`) or in a `calc()` written for lengths of
  different units is not clamped.
- **Other colour functions are mixed in sRGB, not in their own space.** `hwb()`, `lab()`, `oklch()`, `color-mix()` and the
  like resolve to sRGB first (a colour the parser cannot resolve flips half way), where CSS Color 4 §12 mixes in the
  function's space.
- **`currentcolor` mixes as the box's `color` at the animation step, and only then** (#1757). The mix substitutes the box's `color` as
  the box holds it when the animation is applied: a `color` animated in the same keyframes is sampled first and followed, but
  one set by a *later* animation in the `animation-name` list, or by an `!important` declaration (an `!important` custom property that the colour's `var()` names counts), is not seen. A bevelled border
  side (`inset`, `outset`, `groove`, `ridge`) shows the fixed bevel base at progress 0 and 1, where the keyword is kept, and a
  literal mixed colour in between.
  Near the start of the run that stale colour can be farther from a browser's result than the old flip was (`color: red !important`
  with `background-color: currentcolor` to white at progress 0.1: the browser gives 255,26,26, this gives 26,26,26, the flip gave
  255,0,0), from half way on it is no worse.
- **Some shadow colours still flip.** A shadow that omits its colour (`0 0 4px` against `0 0 8px red`, where the omitted colour means
  `currentcolor`), shadow lists of different length, and `none` against a shadow flip where a browser mixes; `hwb()` is not mixed
  (it flips), and `#rrggbbaa` inside a shadow list does not mix even without `currentcolor`.
- **CSS animations on the elements of an inline `<svg>` are not applied.** `rect { animation: ... }` (fill, opacity,
  transform, stroke-width, `stroke-dashoffset`, `r`, a `<g>`'s transform) is parsed and ignored; the SVG tree builder has its
  own cascade entry point that does not call the animation step. Only HTML boxes (and their pseudo-elements) are animated.
- **Gradients, `calc()` of another shape and vendor prefixes.** A gradient is mixed stop by stop where a browser flips half
  way; `calc()` against a value of another shape flips; `@-webkit-keyframes` and `-webkit-animation` are ignored.
- **`prefers-reduced-motion` is reported as `reduce`** (a static PDF has no motion), so the common
  `@media (prefers-reduced-motion: reduce) { * { animation: none !important } }` reset switches every animation off even
  when a progress is set. That is the browser-faithful answer for the media feature; a caller who wants a frame despite such a
  reset has no option for it.
- **SMIL** (`<animate>`, `<animateTransform>`) is separate and out of scope - see
  [svg-features-out-of-scope](svg-features-out-of-scope.md).

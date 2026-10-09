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
- **Interpolation is structural, on text.** Two `transform` lists with different functions or a different number of them
  are not mixed through matrix decomposition (CSS Transforms 1 §9.3 mismatched case); they flip at 50%. `linear()` with
  stops is read as plain `linear`. Colours mix in premultiplied sRGB only (no `color-mix`/oklab interpolation space), and
  only the colour syntaxes `CssValueParser.TryGetColor` understands are mixed - any other flips at 50%.
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
- **`currentcolor` is not mixed.** A keyframe colour of `currentcolor` against a colour flips half way instead of mixing with
  the box's `color`. Resolving it at sample time is wrong when `color` is itself animated in the same animation.
- **Transform functions with a different number of arguments are not mixed.** `scale(1)` against `scale(2, .5)` and
  `translate(10px)` against `translate(10px, 50px)` flip half way; the documented case is two lists of different functions.
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

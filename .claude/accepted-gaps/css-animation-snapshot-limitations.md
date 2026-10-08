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
  `animation-timeline`/`animation-range` (scroll-driven), `@keyframes` `timeline-range` percentages, `animation-*` values
  that arrive through `var()` (they are resolved after the animation step), and the declarative document-building API
  (its cascade is a different entry point and does not call the applier).
- **SMIL** (`<animate>`, `<animateTransform>`) is separate and out of scope - see
  [svg-features-out-of-scope](svg-features-out-of-scope.md).

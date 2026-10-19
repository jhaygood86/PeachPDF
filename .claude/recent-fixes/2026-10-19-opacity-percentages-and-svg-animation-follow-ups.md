# Opacity percentages in keyframes, and what the SVG animation PR's approval left open

Follow-up to [the CSS animations on inline SVG elements change](2026-10-19-css-animations-on-inline-svg-elements.md), from the maintainer's approval comment.

- **`50%` in an opacity keyframe.** `CssValueInterpolator.Interpolate` mixed a number and a percentage as two units (a flip), for `opacity` of an HTML box too, so the fix is in the shared interpolator
  (`PercentageAsNumber` for `opacity`, `fill-opacity`, `stroke-opacity`, `stop-opacity`), not in the SVG sampler: a first attempt that normalised in `SampleElement` fixed only the SVG half. `FillOpacityProperty` and
  `StrokeOpacityProperty` also took a number only, so the CSS-OM dropped `fill-opacity: 50%` before any sampler saw it; they now use the converter `StopOpacityProperty` has.
- **Tests that pin the survivors the maintainer's mutation run found:** text lengths from a bare-number attribute (`font-size="20"` to `60px`), the omitted `r` and `display` initial values, and a zero-duration
  animation with and without `forwards`. Each was re-checked by removing the code under test.
- **Docs:** `width`/`height`/`x`/`y` on a nested `svg`/`use`/`symbol` are read as CSS geometry (unlike a browser), and a `.svg` given to the CLI is animated.
- Not done: the stack margin of an animated `inherit` chain (#1808).

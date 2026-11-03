# `stroke-dasharray` items of two units are mixed as a `calc()`

`CssValueInterpolator.TryMixDashLists` used to give up (flip at 50%) when an item pair had two units, because the dash list
was split on spaces and a `calc()` holds spaces. Now a mismatched pair is written `calc(a*(1-t)unit + b*t unit)` (clamped
with `max(0px, ...)` when an easing overshoots) and the list goes on to `SvgValueParsers.ParseDashArray`, which splits with
the new paren-aware `SplitDashItems` and resolves each `calc()` through the shared length parser; a percentage is of the
viewport diagonal like any other. Equal-length lists with matching units still go to the generic mix (the method returns
false for them), so their text output is unchanged. A `calc()` that the keyframe itself contains is still not read and flips.
Evidence: dash-list tests in `SvgCssAnimationInstanceAndListTests` (values against the 200x100 diagonal) and
`CssAnimationSnapshotTests`.

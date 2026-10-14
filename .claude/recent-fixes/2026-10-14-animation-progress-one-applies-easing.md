# `AnimationProgress = 1` goes through the last interval's easing

`AnimationApplier.Sample` returned the last keyframe's value as soon as `progress >= lastOffset`, skipping the easing. Every
easing up to `cubic-bezier()` and `steps()` maps 1 to 1, so it never showed; `linear()` (see
[the stops entry](2026-10-14-linear-easing-stops.md)) can end anywhere, so `linear(0, 1, 0)` at the end of the run showed the
end value where a browser shows the start. Found by the review of that change.

- Only *past* the last keyframe (`>`) does the nearest value hold now; *at* it is the end of the last interval and is eased
  (the mirror of "exactly at the first keyframe is the start of its interval").
- Two keyframes at the same offset leave a zero-width last interval; that case keeps returning the last one's value instead of
  dividing by zero.
- `EvaluatePoints` returns a point's output exactly when the progress equals its input. The interpolation formula can be one
  rounding off (`0.1 + (0.3 - 0.1)`), and the end value of a curve that does finish at its last point should stay the
  keyframe's literal text.

Evidence: `ProgressOne_*` tests (the `linear(0, 1, 0)`, `linear(1, 0)` and overshooting cases fail on the old code), plus the
four stop-resolution witnesses the review supplied.

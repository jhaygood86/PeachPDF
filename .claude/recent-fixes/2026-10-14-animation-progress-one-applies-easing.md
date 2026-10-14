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

- It is the *last keyframe's offset*, not progress 1, that is now eased: a property with keyframes at 0 and 50% and no
  underlying value to fill a 100% keyframe eases at exactly 50% too (css-animations-1: the last keyframe's interval ends there).
  The hold branch (`progress >` the last offset) is only reachable for such a property; no test builds one.
- No migration note: `git show v0.9.22:docs/html-css-support.md` says `linear()` with stops is treated as `linear`, so a curve
  that does not end at 1 is itself unreleased and every released easing maps 1 to 1.

Evidence: `ProgressOne_*` tests (the `linear(0, 1, 0)`, `linear(1, 0)` and overshooting cases fail on the old code), plus the
four stop-resolution witnesses the review supplied.

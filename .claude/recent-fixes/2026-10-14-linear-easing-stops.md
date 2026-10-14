# `linear(...)` easing evaluates its stops

`EasingFunction` used to read any `linear(...)` as plain `linear`. It now keeps the stops (`Kind.Points`) and evaluates them as
css-easing-2 §2.1.2 / §2.1.4 specify. Tracked as #1747, part of #1672.

- **Parsing** is the spec's: one number and up to two percentages per stop (two make two points of the same output); first input
  defaults to 0, last to `max(1, largest so far)`; an explicit input is raised to the largest before it; a run of stops with no
  input is spread evenly between its neighbours.
- **Evaluation** takes the last point whose input is `<=` the progress, steps back one if that is the final point, and returns
  the next point's output when the two inputs are equal (a jump). There is no clamping: outside the range the nearest two
  points' line is continued, as the spec's own note says, so the value can leave 0 to 1 like an overshooting `cubic-bezier()`.
- **One grammar, two callers.** `LinearEasingGrammar` (`CSS/`) reads the stops out of tokens; the CSS-OM's
  `LinearStopsValueConverter` validates with it and `EasingFunction` builds the curve from it, so they cannot disagree about
  what is a valid `linear()` (the first version had two hand-written checks, one of which let `NaN`/`1e999` through).
  Only the §2.1.2 point resolution (defaults, monotonic clamp, spreading) lives in `EasingFunction`.
- `TryParse` now returns false for a malformed `linear(...)`; it used to return true for anything.
- Trap: the old integration tests used `linear(0, 0.25 25%, 1)` at 0.5, which *is* 0.5 on the real curve too, so they could not
  tell stops from plain `linear`. They now use `linear(0, 0.8 25%, 1)` (0.8667 at 0.5).

Evidence: `CssAnimationSnapshotTests` (stop placement, plateau, jump, extrapolation, malformed).
- A segment's span can overflow even though every stop is finite (`linear(1e308 0%, -1e308 50%, 0)` gives `-Infinity`, then NaN
  at an input equal to a point); `EvaluatePoints` falls back to the segment's end rather than hand NaN to the interpolator.
- The animation showcase gained a `linear(0, 1.25 40%, 0.9 70%, 1)` row. Rasterized with PDFium and MuPDF at 0.5: both put the box
  at 1.133 x 240px, about 152px ahead of the same animation on plain `linear`.
- Full net8.0 suite: 17199 passed, 9 skipped, 0 failed; PeachPDF, Cli and TestHarness build with 0 warnings.
- Supersedes the line in [2026-10-09-css-animations-as-a-still-frame](2026-10-09-css-animations-as-a-still-frame.md) that says `linear()` is "still read as plain `linear`": it is not any more.

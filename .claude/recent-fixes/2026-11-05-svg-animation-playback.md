# SVG animation on a clock: SMIL and CSS animations of inline `<svg>` elements

**What it is.**
- `Svg/Smil/`: `SmilAnimation` (one animation element, read once: timing lists, values, `animateTransform`/`animateMotion` fields), `SmilSet` (read from the boxes of one `<svg>`,
  begin/end resolved against each other into intervals - sync-bases by recursion with a cycle guard, `restart`, `end` cutting an interval, `fill="freeze"` - and sampled at a time),
  `SmilValues` (mixing text: numbers with the same text between them mix one by one, colours go through `CssValueInterpolator`, the rest switches at the half way point).
- The seam is `CssBoxSvgSourceNode`: `GetAttribute` returns the animated value of an attribute an animation names, `GetAnimatedValue` (the CSS animation origin) lets SMIL sit above the CSS
  animation's value, and `IsAnimated` is true for a node SMIL targets. The builder needed no change; the time is `HtmlContainerInt.SvgAnimationSeconds` (a running clock) or, for a still
  frame, `AnimationProgress * SmilSet.StillFrameLength`.
- CSS animations on SVG elements got a time-based path: `PlanElement` uses `AnimationTimeline.LiveProgress` when `SvgAnimationSeconds` is set (delay, fill and direction apply), and the plan cached
  on `BoxRareState` is keyed by that time.
- `CssBoxSvg` keeps its children (`_retained`) when a clock plays it and builds the scene graph again in `StepAnimation(seconds)`; `AnimationClock` registers a `BoxState` for such a box (no tracks,
  `state.Svg`), steps it every frame and adds the box to `_changed`, so `AddDirtyRegions` reports exactly the `<svg>`'s rectangle. The `SvgDocument` is flagged `IsTimeVarying`, which makes
  `SvgRenderer.RenderCachedInto` paint it directly: the reusable-form cache is keyed by document identity and would fill with artwork nobody paints twice.

**Load-bearing ideas.**
- A graphic is rebuilt only for a moment its animations change in: `changed = seconds != built && (PlayingAt(seconds) || PlayingAt(built))`, so the step that crosses an end still repaints, and a
  finished graphic costs nothing. `ChangesAfter(t)` is true while any interval ends after `t`.
- A freeze at a whole number of iterations holds the end of the last one (progress 1), not the start of the next: `TryPhase` steps back one iteration.
- `AdvanceAnimations(now)` is a monotonic clock whose **first** call is time zero: a test that starts at 0.5 gets SMIL time 0.

**Found by running it.** The svg box is `width * 0.75` points (`px` is 1/96in), so a pixel assertion in user units misses; all the pixel tests are written in points.

**Not done.** See the SMIL section of [the SVG guide](../../docs/supported-svg-features.md#smil-animation) and [the live playback gaps](../accepted-gaps/css-animation-live-playback-limitations.md):
events and access keys in `begin`/`end`, a standalone SVG, the root's own size, changed-element dirty regions.

**Found by widening the tests.** A presentation property (`opacity`, `fill`...) was animated twice: `GetAttribute` returned the animated value, which the builder then took as the
declared value and gave to `GetAnimatedValue` as the underlying one (an additive `0.2 + 0.25` read `0.7`). `CssBoxSvgSourceNode.PresentationProperties` now splits them: those are animated only
in `GetAnimatedValue` (over the cascade), every other attribute only in `GetAttribute`. And a `min` longer than the simple duration, with no repeat, now holds the last value instead of repeating.

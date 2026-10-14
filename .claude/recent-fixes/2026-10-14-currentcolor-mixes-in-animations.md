# `currentcolor` mixes as the box's `color` in the animation still frame

Tracked in #1756, under #1672 (remaining gaps: #1757). `CssValueInterpolator` scans a value into tokens, and `currentcolor`
is a plain word to it, so a colour against `currentcolor` was a shape mismatch and flipped at 50%.

## The load-bearing ideas

- **Substitute only when the keyword is why the shapes differ.** `Interpolate` swaps `currentcolor` tokens for the box's `color`
  only after `SameShape` has failed (and only when `box` is known). Two values that both say `currentcolor` (`box-shadow: 0 0 2pt
  currentcolor` to `0 0 10pt currentcolor`) already match, mix their lengths and keep the keyword, which keeps following `color`
  at paint; substituting there would have frozen it. That guard has its own test - the mutation that drops it survived the first
  set of tests.
- **`color` is sampled first.** `currentcolor` in another property stands for the `color` of the same moment, so
  `AnimationApplier.Apply` moves `color` to the front of the animated list. With keyframes `{ background-color: currentcolor; color: red }`
  to `{ background-color: white; color: blue }` the box's `color` is still the author's when `background-color` comes first in the
  keyframe; the test that pins this fails without the reorder.
- **The box's own `color` is read through `CssUtils.GetPropertyValue(box, "color")`**, the same read `ApplyCurrentColor` makes when
  it resolves the keyword at the end of the cascade, so an inherited colour works (tested). That read happens *before* the
  cascade's last step settles `var()`, so a `color: var(--c)` would still be the parent's colour: `Apply` settles a deferred `color`
  before it samples (the first review caught it; tested) - but only when an animated non-`color` property can name `currentcolor`
  (`ReferencesCurrentColor`: a keyframe says so, or the property's underlying value does). The first version settled it for every
  animated box, which broke an `!important` custom property that sets the text colour (`@keyframes` on `opacity` only, `color: var(--c)`
  and `--c: blue !important`): the early settle ran before the `!important` phase and saw the normal `--c`. When `color` itself is
  animated the overlap rule settles it, as it always did. Even when it is needed, the settle is for the sampling only: `Apply` puts the
  `color` declaration back in the pending set afterwards, so the cascade's last var() pass still decides the box's own text colour
  (a keyframe naming `currentcolor` next to `color: var(--c)` and `--c: blue !important` must still paint blue text - Chrome
  agrees; found by a Chrome differential run). The mix itself still uses the normal-phase colour: the `!important` gap.
- **The endpoints keep the keyword.** The applier returns the specified text at exactly 0 and 1, so `currentcolor` still resolves at
  the end of the cascade there; only the in-between values are substituted.

## Not done

A `color` set by a later animation or by `!important` is not seen (the animation step runs before the `!important` phase). See [the accepted-gap note](../accepted-gaps/css-animation-snapshot-limitations.md).

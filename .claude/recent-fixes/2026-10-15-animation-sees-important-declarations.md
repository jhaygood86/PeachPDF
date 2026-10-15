# The animation step sees `!important` custom properties and `color`, and sets `color` for every animation first

Tracked in #1762, under #1672 (the `var()` and `!important` item) and #1757 (a `color` that is `!important`, or set by a later animation). All three
shared one cause: the cascade runs the animation step between the author-normal and the author-`!important` phases (the animation origin's
slot, CSS Cascade 4 §6.1), so a `var()` or a `currentcolor` read the box as the normal phases left it.

## The load-bearing ideas

- **Pre-apply, then apply again.** `DomParser.ApplyImportantAnimationProperties` already put the `!important` `animation-*` declarations on the
  box ahead of the step (they decide which animations exist). It now also does the custom properties (`--*`) and `color`; phase 6 applies the
  same declarations again to the same result. A custom property is only a dictionary store (`AssignCustomPropertyDeclaration`, resolved later
  by `ResolveDeferredVarProperties`), so pre-applying it is cheap and idempotent. `revert` / `revert-layer` are left to phase 6 on purpose: the
  pre-apply has no revert snapshot, and applying them with none would *remove* a custom property the real phase would roll back to a value.
  It iterates `authorImportant` in list order, which is already the descending layer order phase 6 uses, then the inline rule.
- **An important `color` is not animated.** It outranks the animation origin, so the box's `color` is the important one at every progress.
  If the animation still wrote its mid-animation `color` first (and it does, before phase 6 puts the important one back), every
  `currentcolor` mixed in between would use the animated colour - the 255,26,26 against 26,26,26 mismatch a reviewer measured. So the
  cascade tells the applier (`colorIsImportant`) and `AnimationApplier` drops `color` from what it samples.
- **`color` first across the whole list.** `currentcolor` in an animation's keyframes stands for the `color` the box has at that moment, which
  another animation may set. With two or more animations and some animating `color`, `Apply` runs two passes over the list: `color` for
  every animation, then everything else (`ColorPass`). One animation, or none animating `color`, is a single pass as before. Both passes keep
  list order, so "a later animation wins, and an implicit keyframe sees the earlier one's result" still holds within each property.

## What Chrome does (the oracle: paused animations with a negative delay, `getComputedStyle()`)

Measured on 19 cases at progress 0.1, 0.5 and 0.9 (later widened to 33 by the reviews, 66 rows at 0.1 and 0.5 in
`AnimationImportantDeclarationTests`, generated from the harness; the harness itself lives only in a scratch directory):

- An `!important` custom property reaches a `var()` in the animated property (the implicit keyframe) and in a keyframe: width 175px at 0.5 for
  50px to an important 300px. Inline `!important`, an ancestor's `!important`, and `@layer`s (important reverses layer order: with the
  unlayered `!important` losing to a layered one and the *earliest* layer winning) all behave that way. An `!important` loses to nothing
  of the normal origin, whatever its specificity.
- An `!important` `color` is the `currentcolor` of a mix and stays the used `color` even when the keyframes animate `color`
  (`color: #f00 !important`, keyframes `color: #0f0` to `#00f` and `background-color: currentcolor` to white: `color` is red throughout and
  the background mixes from red). The same for `color: var(--c) !important` with `--c: blue !important` against a normal `--c: red`, and for a
  normal `color: var(--c)` against an `!important` `--c`.
- A `color` that a later animation in the list animates is the `currentcolor` of an earlier one (`background-color: #fff` to `currentcolor`
  in the first animation, `color: #f00` to `#00f` in the second: 191,128,191 at 0.5, whichever comes first). Two animations that both animate
  `color`: the later one wins, and the earlier one's `currentcolor` mixes with that final colour.

With `main`'s code the first 19 cases had 42 mismatched reads (14 cases: the custom-property cases A1-A3, A5 and A6, the `color` cases B1-B5
and E1, and the several-animation cases D1, D3 and D4); with this change none, apart from the parent's own block width in one case (the two
renderers' viewports differ). Of the final 66 test rows 56 fail on `main`; the 10 that pass are controls (A4, D2, E2, Z1, Z2: nothing in the
input that this change affects).

This supersedes the `!important` limits recorded in `2026-10-14-currentcolor-mixes-in-animations.md` ("Not done") and in
`2026-10-09-css-animations-as-a-still-frame.md` (custom properties that are `!important` are not seen by `var()`).

## Not done

- Custom properties do not animate at all, registered or not (the keyframes drop every `--*` declaration), so an `!important` custom property
  cannot be overwritten by a keyframe for the sampling; a future discrete animation of custom properties would have to skip one that is
  `!important`.
- `color: revert !important` and `--x: revert !important` are left to phase 6, so a mix that reads them sees the value the normal phases
  left. UA `!important` declarations (phase 8) are not pre-applied; the built-in UA sheet has none.
- A shorthand holding `var()` is still settled as a whole when any longhand it covers is animated; that settle now sees the important custom
  properties, so the result is correct (it was only ever early).
- The `!colorIsImportant` in the `colorPasses` condition is an optimisation: an important `color` is dropped from every pass anyway.
- **Found by the first review, not caused by this change (same result on `main`):** `color: currentcolor !important` after an earlier normal
  `color` keeps the earlier colour (Chrome: the inherited one), even without an animation; `from { color: currentcolor }` inside a keyframe
  is read as the box's own colour where Chrome reads the inherited one; `all: unset` inside keyframes is not animated; and
  `animation: var(--n) 1s ...` (the shorthand holding the deferred entry) followed by `animation-name: none !important` still runs the
  animation, because removing the entry by the longhand's name does not clear the shorthand's key.

## A trap this found

A plain `!important` value has to remove an earlier deferred `var()` entry for the same property from `pendingVarProperties` (the cascade proper
does, in `AssignCssBlock`). The first version of the pre-apply did not, and `AnimationApplier` then settled the stale `color: var(--c)` over the
important colour: the final `color` was right (phase 6 cleans up) but every `currentcolor` mixed in between used the stale one. Cases F1-F6 of
`AnimationImportantDeclarationTests` pin it; so does F7 for `animation-name: var(--n)` followed by `animation-name: none !important`.

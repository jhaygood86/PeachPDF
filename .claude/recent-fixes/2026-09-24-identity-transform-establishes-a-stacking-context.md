# An identity `transform` (and any `perspective`) establishes a stacking context

Closes #1330.

## What was wrong

`DomUtils.IsStackingContextBox` keyed its transform arm on `CssBox.IsTransformed`, which is
`!ActualTransformMatrix.IsIdentity`. CSS Transforms 1 §2 makes any `transform` other than `none` a stacking
context, so `rotate(0deg)`, `scale(1)` and `translateZ(0)` (the "force a layer" hacks) were not one. A
`z-index: -1` child of such a box escaped to the enclosing context and painted *under* the box's background;
`translate(0.01px, 0)` behaved correctly, which is what gave it away. `perspective` other than `none`
(CSS Transforms 2) had no arm at all.

## The fix

The arm reads `CssBox.HasTransform` (computed value is not `none`), and a new arm reads
`ActualPerspective > 0`. The same split #1319 made for the containing-block chain now holds here:

- **`HasTransform`** answers "does the property establish something" (containing block, stacking context).
- **`IsTransformed`** answers "is there a matrix to apply at paint time" and stays on the paint push
  (`FragmentPainter.cs`), backdrop-root coordinate-space checks, flatten and outlines, where an identity
  matrix really is a no-op. An identity stacking context therefore paints as an atomic block and pushes no matrix.

## Traps

- `IsTransformed` lazily computes and permanently caches the matrix against the box's border-box size, which is
  why `ComputeFlowFlags(includeStackingHoistCandidates: false)` runs before layout. `HasTransform` reads style
  only, so the stacking arm no longer has that hazard; the flag stays deferred because it is only consumed post-layout.
- `rotate(0)` reaches this path only since #1327 made a unitless zero angle parse.

## Evidence

`IsStackingContextBox` and paint-order tests for `rotate(0deg)`, `rotate(0)`, `scale(1)`, `translateZ(0)` and
`perspective: 500px` (all fail on the merge base), a `transform: none` control, and an assertion that no
`PushTransform` is recorded for the identity box.

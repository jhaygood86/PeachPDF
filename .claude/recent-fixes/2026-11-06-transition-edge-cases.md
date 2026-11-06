# Transition edge cases from the review

- A transition with a `0s` duration and a positive delay now runs (it holds the old value through the delay, then snaps): `InteractionRestyler.PropertyFactoryHasNoTransition` skipped on a zero duration alone.
- A transition of a layout property that starts before the clock's first step is assumed to be laid out for (a screen document with no size containers), instead of being dropped; if the first step has no
  canvas it ends at once on its final value (`AnimationClock.Advance`), because without a layout pass it could only be left on the value it started from.
- `visibility: collapse` (either side of a change) is not paint-only: the in-place restyle gives up for it, and the clock does not transition it, so a table row that collapses is laid out again.
- `StartingStyles()` restores every box's own style in a `finally`, and `StartTransitionsFrom` reads the before-snapshot without removing it, so a second box of one element (a split inline) compares
  against the same snapshot and no longer looks new (and takes its `@starting-style`).
- `ShadowLayerCache` tags each layer with a serial, so the stale queue entry of a key that was dropped and made again cannot evict the new layer.

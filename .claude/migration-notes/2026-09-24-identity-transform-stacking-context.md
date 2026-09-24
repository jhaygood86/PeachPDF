# An identity `transform` and any `perspective` now establish a stacking context

Before: `transform: rotate(0deg)`, `scale(1)`, `translateZ(0)` (any transform whose matrix is the identity) did
not create a stacking context, so a `position: relative; z-index: -1` descendant painted *behind* the box's own
background. `perspective` never created one.

Now: any `transform` other than `none`, and any `perspective` other than `none`, creates a stacking context, as
in browsers (CSS Transforms 1 §2, CSS Transforms 2). Negative-`z-index` descendants stay inside the box and paint
above its background. Documents that used `translateZ(0)` as a layer hint and relied on a negative-`z-index`
child showing *behind* the box will see it move in front of the background.

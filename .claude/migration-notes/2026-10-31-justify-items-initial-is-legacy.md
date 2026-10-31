# `justify-items` now defaults to `legacy`, and accepts `legacy [left|right|center]`

**Before:** `justify-items` was `normal` by default, and `justify-items: legacy` (or `legacy left|right|center`) was an invalid value that the declaration dropped.

**Now:** the initial value is `legacy`, as CSS Box Alignment 3 specifies, and the `legacy` forms parse. Rendering does not change: in a grid `legacy` lays out exactly as `normal` did, and flex containers ignore `justify-items`. What changes is the computed initial value (`justify-items: initial` resolves to `legacy`) and that stylesheets using the `legacy` keywords are no longer discarded.

The `legacy` companion keyword's inheritance rule (a block container passing its alignment to children's `justify-self: auto`) is not implemented, because `justify-self` is not applied to block-level boxes.

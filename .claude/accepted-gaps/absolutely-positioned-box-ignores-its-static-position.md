# An absolutely positioned box with `auto` offsets ignores its static position under `direction: rtl`

The block-flow (#1303) and inline-content (#2069) cases for a left-to-right parent are done: `CssBox.StaticPositionIn` reads the box's
place on its final line (`CssBox.InlineStaticPosition`, set by `CssLayoutEngine.LayoutAbsolutelyPositioned` from `PlaceOnFinalLine`)
or, in block flow, the previous sibling's edge.

**Gap:** with `left`/`right` both `auto` in a `direction: rtl` parent, CSS 2.1 section 10.3.7 measures the static position from the
right edge (the box's right margin edge sits at the static position). `StaticPositionIn` returns null for such a parent in block
flow, so the box keeps the containing block's padding corner; among inline content the place's left edge is used rather than the
right one. Resolving it needs the box's shrink-to-fit width, which is not known when the static position is read.

**Why out of scope:** the rtl case needs the used width before positioning, a change to the absolute-offset resolution order, and is
rare in practice.

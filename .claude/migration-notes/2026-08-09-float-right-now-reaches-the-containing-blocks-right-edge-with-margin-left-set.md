# `float:right` now reaches the containing block's right edge when `margin-left` is set

**Landed:** 2026-08-09 (9d66a066, fd3759a2) — Fix float:right sitting short of containing block by its own margin-left; Fix stray margin-left term in float:right ancestor-intersection check
**Doc section:** none — this behavior was never separately documented, so there's no callout to remove
**Verified against v0.9.8:** the buggy formula was already present at the `v0.9.8` tag, so this is a genuine fix relative to the last release, in scope for the next release notes.

A `float:right` element with `margin-left` set used to land short of the containing block's right
edge (or the nearest intersecting float) by exactly its own `margin-left`, even with `margin-right:
0`. A box's left margin is space to its own left; it must not shift the box's own right edge inward.
Such elements now render flush against the containing block's right edge as expected — no document
change is needed to pick up the fix.

A second, related bug in the same area is fixed alongside it: a `float:right` box nested inside a
narrower, non-floated block used to sometimes fail to extend out and align with a wider ancestor
`float:right` sibling when the nested box itself had `margin-left` set — an intersection check that
used to (accidentally) cancel out the first bug's error term instead inflated its own threshold by
that same `margin-left` once the first bug was fixed. Both bugs shared one root cause and are fixed
together here.

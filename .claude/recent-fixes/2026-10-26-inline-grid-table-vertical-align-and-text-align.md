# `vertical-align` and `text-align` on atomic inline-flex/grid/table boxes (#1678, #1679)

**vertical-align.** `CssLayoutEngine.OffsetBoxWithinLine` translated a box's subtree only when `LastOwnLineBaselineOf`
found a baseline, with `inline-flex` special-cased. A text-less `inline-grid`/`inline-table` therefore moved its line
rectangle but not its `Location`, items or absolute child. The predicate is now "display is inline-flex/grid/table, or it
has a baseline".

**text-align.** Alignment measured the line from `line.Words` only, and returned early when there were none, so an atomic
box alone on a centered/right line never moved and one after text was aligned as if absent. `LineContentEnd` now takes the
rightmost of the last word (with its closing decoration) and each atomic unit's margin-box right edge;
`ShiftLineContentLeft` (shared by center and right) moves words, rectangles and each unit's subtree (`OffsetLeft` plus
`MoveStaticallyPlacedDescendants`, whose `isVertical: true` is the "X is the axis" mode).

Traps: a test's absolute child needs `position: relative` on the box, or it is placed by an outer containing block and
rightly does not move. `justify` only changes its leftover computation (words no longer stretch into a trailing box); the
box itself is not shifted by expanded spaces before it. `inline-block` with its own lines was not touched.

Evidence: `InlineGridTableVerticalAlignIntegrationTests`, `InlineAtomicBoxTextAlignIntegrationTests`; engine suite
16421/16421 (net8.0 Release).

# `vertical-align` does not move the contents of a text-less `inline-grid` / `inline-table`

[CSS 2.1 §10.8.1](https://www.w3.org/TR/CSS21/visudet.html#propdef-vertical-align): `vertical-align` moves
an inline-level box on its line, and its content moves with it.

`CssLayoutEngine.OffsetBoxWithinLine` translates an atomic box's whole subtree only when
`LastOwnLineBaselineOf(box)` finds a baseline in it. A text-less `inline-grid`/`inline-table` has none, so
only its line rectangle moves: probed with `vertical-align: bottom`, the rectangle goes from Y=20 to
Y=47.8 while the box's `Location`, its in-flow item and its absolute child stay at 20/20/25.
`inline-flex` had the same hole and is closed by special-casing its display in that method
([recent fix](../recent-fixes/2026-10-09-inline-flex-out-of-flow-children.md)); the grid and table cases were
left because they affect in-flow items too, which is wider than the out-of-flow-children bug that change fixed.
A shared "laid out as an atomic box" predicate would cover all three.

Tracked as [issue #1678](https://github.com/jhaygood86/PeachPDF/issues/1678).

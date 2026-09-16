# `height`/`min-height` on a `writing-mode: vertical-*` `<table>`/`<tr>` has no row-axis effect

[Issue #1116](https://github.com/jhaygood86/PeachPDF/issues/1116) made `height`/`min-height` on a
`<table>`/`<tr>` establish a real CSS 2.1 §17.5.3 minimum for `writing-mode: horizontal-tb` tables, with
surplus distributed proportionally across rows. That fix does not extend to `writing-mode: vertical-rl`/
`vertical-lr` tables — `CssLayoutEngineTable.TryComputeRowHeightRedistribution` and `RowHeightFloor` both
bail out immediately when `_isVertical` is true.

`height`/`min-height` are physical CSS properties. On a horizontal table the row axis (where §17.5.3's
rows stack) is physical height, so `height` is exactly the right property. On a vertical table, rows
still stack along the block axis, but for `vertical-rl`/`vertical-lr` the block axis is physical
**width** — `CssLayoutEngineTable`'s own `_isVertical` Step 7 branch already routes genuine CSS `height`
to the table's *column* axis in that orientation, not its row axis. A vertical table's own
row-axis-minimum analog would need to be driven by `width`/`min-width` instead, entangled with this
engine's existing, separately-working column-width algorithm (`DetermineMissingColumnWidths`/
`SpreadSurplusProportionally`) rather than the row-height mechanism #1116 added — a distinct, symmetric
gap, not this one. Filed as [issue #1131](https://github.com/jhaygood86/PeachPDF/issues/1131).

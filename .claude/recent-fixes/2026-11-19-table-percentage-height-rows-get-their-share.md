# Percentage-height cells get their share of a definite-height table (#2429)

#2428 made a cell's own percentage `height` act as `auto` (`CssLayoutEngine.GetBoxHeight`), which stopped rows
inflating past the table but left `TryComputeRowHeightRedistribution` spreading surplus purely by natural height.
`TryDistributeToPercentageRows` now runs first: each percentage row takes `min(pct * tableHeight, budget - assigned
- natural height of the rows after it)`, floored at its natural height, in row order; the leftover goes to the auto
rows proportionally. Only horizontal tables, single-row cells.

Trap: test cells need `padding:0` (default cell padding made 52/197/52). Evidence: new tests in
`TableCellPercentageHeightIntegrationTests`; all 961 `*Table*` engine tests pass.

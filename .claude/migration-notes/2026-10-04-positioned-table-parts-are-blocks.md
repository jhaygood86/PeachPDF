# An absolute/fixed table part is a block

Before: an `position: absolute|fixed` element with `display: table-row`, `table-row-group`,
`table-header-group`, `table-footer-group`, `table-cell`, `table-column` or `table-column-group`
kept that display; a cell-less `table-row` (positioned or not) threw `Exception in box layout`.

Now: an out-of-flow box with a table-internal display is blockified to `block` (CSS Display 3 §2.7),
as browsers do, and a `display: table-row` element with no cells lays out instead of throwing.

# An absolute/fixed table part is blockified, so it no longer crashes layout (#1239)

**Symptom.** `<div style="position:fixed; display:table-row">` (or `absolute`) threw
`InvalidOperationException: Sequence contains no elements` out of `CssLayoutEngineTable.LayoutBodyRows`.

**Cause.** `DomParser.BlockifyPositionedBox` coerced only the inline-level displays. A table-internal
display on an out-of-flow box stayed as it was, so the box reached the table engine with no table
around it and no cells.

**Fix.** CSS Display 3 §2.7: a layout-internal display (`table-row`, the row groups, `table-cell`,
`table-column`, `table-column-group`) blockifies to `block` on an absolute/fixed box. `table-caption`
is left alone (it is already block-level). A second defect sat behind it: a *static* `display:table-row` element with no children also threw,
from the same unguarded `Min`/`Max` over the row's cells (found when the first test for the fix, a
static control, crashed). `RowOriginFromCells` and the `Boxes.Count` guards in `AssignRowActualBounds`
and the vertical-table bottoms keep a cell-less row at its own location instead.

**Evidence.** Tests in `AbsolutePositioningIntegrationTests` (display value for every part, the
repro laying out as a block with its declared size, a static `table-row` untouched).

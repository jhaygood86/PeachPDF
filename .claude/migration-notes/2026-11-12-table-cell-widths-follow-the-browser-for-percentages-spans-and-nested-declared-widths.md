# Table cell widths follow the browser for percentages, spanning cells and nested declared widths

**Before.** Three cases came out wider than in a browser when the cell had padding (an author `padding`, `cellpadding`, and now every `td` and `th` through the default padding):

- A cell with a percentage `width` (`<td style="width:50%">`) had its padding added outside the share, so beside an unsized cell it took more than half the table.
- A cell with `colspan` greater than 1 was widened by the `border-spacing` between the columns it spans, as if the spacing were more room it needed. The table came out one spacing wider.
- A table in a cell with a declared `width` (`<td width="80">`) could not make that cell narrower than the nested table's own declared cell widths, so the outer cell grew past the 80px it asked for.

**Now.** A percentage cell width is the cell's whole (border-box) share of the grid, `colspan` columns count the spacing between them as part of the cell's width, and a declared cell width is the width its column prefers, not a minimum for a table nested in it. All three match headless Chrome. Cells with a length `width` still have their padding outside it, and content that cannot shrink (a 120px box in the nested cell) still widens the outer cell.

**Checked.** `git show v0.9.26:src/engine/PeachBrackets/Html/Core/Dom/CssLayoutEngineTable.cs` adds `CellInlineSizeExcludes` to every declared cell width (percentages included) and has `shortfall = required - total` with no interior spacing; `CssBox.cs` at the tag floors a table cell's minimum by its declared width.

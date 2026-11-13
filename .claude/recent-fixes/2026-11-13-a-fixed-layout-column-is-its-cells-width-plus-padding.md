# Fixed table layout sized a column from the cell's content width

CSS 2.1 section 17.5.2.1 (and every browser) makes a column's width in `table-layout: fixed` that of the first row's cell with a `width`, and that width is the cell's content box: the column is it plus the cell's padding and border. `CalculateFixedColumnWidths` used the parsed length alone, so `td { width: 80px; padding-left: 120px }` made a 80px column instead of 200px, and the columns after it shifted. It now adds `cell.ActualBoxSizeIncludedWidth` (0 under `box-sizing: border-box`, where the width already is the border box).

css2-tables 109 -> 116 (`fixed-table-layout-025..028` and others). The rest of `fixed-table-layout-003*` differs by a glyph descender of the fallback `serif` font, not by layout.

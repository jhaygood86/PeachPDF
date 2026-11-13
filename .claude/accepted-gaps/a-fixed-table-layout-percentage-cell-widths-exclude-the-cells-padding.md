# Fixed table layout percentage cell widths exclude the cell's padding

In `table-layout: fixed` a browser sizes a cell's percentage width as its content box, so its padding is added on top (`width:50%;padding:20px` in a 400px table is 180pt, the unsized cell 120pt). `CalculateFixedColumnWidths` stores the bare length, so the cell is 150pt and its neighbour 150pt, which is what auto layout gives for the same markup (a percentage is the whole share there). Tracking issue #2398.

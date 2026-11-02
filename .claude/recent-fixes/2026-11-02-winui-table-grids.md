# WinUI: a table is a grid

`HtmlNodeAutomationPeer` now implements `IGridProvider`/`ITableProvider` for a table, grid or tree grid and `IGridItemProvider`/`ITableItemProvider` for its cells (`Cell`, `GridCell`, `ColumnHeader`, `RowHeader`). The numbers come from the engine's tree, which already numbered every cell the way HTML lays a table out (spans included); the host only looks them up (`HtmlViewLogic.CellsOf`/`CellAt`/`HeadersOf`/`TableOf`).

- **`GetItem` follows spans.** A cell that spans reaches every place it covers, so `GetItem(2, 1)` on a row that is one cell wide across two columns returns that cell, not nothing; a place no cell covers returns null.
- **Headers are by role, not by position.** `GetColumnHeaders`/`GetRowHeaders` list the cells whose role is `columnheader`/`rowheader`; a data cell's own `GetColumnHeaderItems` uses the engine's resolved `ColumnHeaders` (scope, layout or its `headers` attribute), so a cell that names its headers is read with those.
- **Providers come from peers.** A provider is `ProviderFromPeer` of the node's peer, found by id in the current tree of peers (`FindPeer`); a node outside the tree gives null instead of a stale peer.
- **Not done:** `ITableItemProvider` is not offered on a cell outside any table (a stray `td`), and a table inside a table counts only its own cells.

Checked by a test in a real window (`ATableIsAGridWhoseCellsKnowTheirPlaceSpanAndHeaders`: counts, `GetItem` with a span and past the edge, header counts, a spanning cell's span, `ContainingGrid`).

# Tables, sets and references in the accessibility tree

Second slice of the accessibility tree: where a cell is in its table, where an item is in its set, and which nodes others point at.

- **The table grid is HTML's.** `NumberTable` places cells left to right per row, skipping places a row above reaches down into with `rowspan`, each cell taking its `colspan` and `rowspan` of places (spans over 1000 are clamped). A cell's row is its **row's** index, which is where `aria-rowindex` on a row has to be read from; reading it only off the cell passed the first test and failed the grid one.
- **A repeating `thead`/`tfoot` is ordered, not just visited.** The detached source is visited from the table's `Children(box)` helper, header before the body rows and footer after, because HTML renders them that way wherever the source has them. Visiting the source at its proxy's place (the end of the table's children) numbered the header as the last row.
- **Sets continue.** In a partly numbered set an item without `aria-posinset` follows the one before it, and the set size is the first `aria-setsize` given, else the count. Radio buttons are not grouped by `name` yet.
- **References resolve to nodes, not elements.** An element the tree leaves out (a bare `div`/`span`, anything not shown) cannot be pointed at, so a test must point at a paragraph or a role. `aria-owns` is reported but the tree is not rearranged.
- **Hosts use what they can now:** WinUI reports `PositionInSet`/`SizeOfSet`; Android sets `CollectionInfo` and `CollectionItemInfo` (`SetCollectionInfo`/`SetCollectionItemInfo`: the property names clash with the nested types in the binding).
- **Not done:** the UI Automation Grid/Table patterns and Android's rich collection actions, which need the host to answer queries about cells.

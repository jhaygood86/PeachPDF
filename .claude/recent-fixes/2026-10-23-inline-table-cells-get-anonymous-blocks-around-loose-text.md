# An inline-table's cell wraps loose text beside a block, like an inline-block's content (#1820)

`lead<div>dd</div>tail` in the cell of a `display: inline-table` drew only `dd`. The pass that wraps each run of inline
content in an anonymous block (CSS 2.1 section 9.2.1.1) when a box mixes inline and block children has a special walk for
an inlines-only ancestor chain (`<body>` holding `A[`, the inline-table and `]B`): `CorrectBlockInsideInlineBlockFormattingContexts`
and `CorrectInlineParentsInsideInlineBlockFormattingContexts` in `DomParser`. Both opened an atomic inline only when it was
an `inline-block`, on the reading that flex, grid and table items generate their own contents; but the cells of a table are
block containers whose loose text nothing else wraps. A block-level table never hit it because its ancestors are not
inlines-only, so the ordinary per-child recursion reached the cell.

Both gates now include `inline-table`; the walk reaches the cells through the rows as it does for a block-level table.
**Not** widened to inline-flex or inline-grid: [2026-09-16-inline-block-mixed-content-and-atomic-wrap.md](2026-09-16-inline-block-mixed-content-and-atomic-wrap.md)
records that opening an inline-flex item regressed one holding a block.

Found on the way and left to its own change (open PR for #1794): an inline-table whose only children are bare text, with
no row or cell at all, draws nothing, where `display: table` draws the text.

Evidence: `InlineTableCellMixedContentTests` (explicit and `<table>` spellings; text before, after and between blocks; a
nested table; inside an inline-block), each asserting every word is drawn once with `PaintedWords`.

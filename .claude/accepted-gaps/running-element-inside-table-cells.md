# `position: running()` not honored for table-cell/row descendants

css-gcpm-3's `position: running(<custom-ident>)` is excluded from normal layout and registered as the
current occupant of its name from `CssBox.LayoutBlockChildren` (plain block flow) and, additionally,
from `CssLayoutEngineFlex`/`CssLayoutEngineGrid`/`CssLayoutEngineColumns`'s own item-collection
filtering — each of those three engines excludes a running child from its own algorithm and registers
it the same way the block-flow hook does. `CssLayoutEngineTable` is deliberately not among them: its
row/column/cell bookkeeping (header/footer repetition via `CssProxyBox`, rowspan continuation, whole-table
relocation) is substantially more invasive to audit safely than the other three engines' comparatively
small item-collection filters. A `position: running(name)` declared on a table row or cell is not
currently excluded from the table's own layout, and never registers as a running element — `content:
element(name)` referencing it resolves to nothing (the same "referenced zero times" fallback an
unmatched name already degrades to).

This mirrors the existing, already-accepted gap in
[named-page-reversion-outside-block-flow.md](named-page-reversion-outside-block-flow.md) (issue #166) —
the same "table's own layout engine doesn't route through the generic child-walk" family. Filed as
[issue #691](https://github.com/jhaygood86/PeachPDF/issues/691). See
`RunningElementLayoutIntegrationTests`' flex/multicol coverage for the three engines this gap does
*not* apply to.

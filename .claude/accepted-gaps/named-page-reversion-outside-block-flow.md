# Named-page activation and reversion outside normal block flow (flex/multicol only)

Tracked as [issue #902](https://github.com/jhaygood86/PeachPDF/issues/902), narrowed from #166 (closed -
the table case is fixed).

Named-page (`page: <name>`) activation and **reversion** are honored only for normal block-flow content
and, since issue #166's table fix, for table rows/cells too. The used value of `page` is tree-based (CSS
Paged Media Level 3 §3) and reverts for content leaving a named subtree — fixed for the block-flow
forced-break path in `CssBox.PerformLayoutImp` (issue #126) and for `CssLayoutEngineTable` (issue #166:
`CssLayoutEngineTable.ForcedBreakFallsBeforeRow` now forces a break on a row's own used-name transition,
and `LayoutBodyRow` now sets each row's `UsedPageName` from its own ancestor chain before any cell lays
out — a `<tr>` is never itself given a `PerformLayoutPrologue` call the way an ordinary block box is, so
without this a cell inheriting from its row's never-set `UsedPageName` registered a spurious reversion
mid-table, corrupting `ActivePageName` for whatever ordinary content followed the table even when that
content never touched the table engine at all) — but *not* for children positioned independently by
`CssLayoutEngineFlex`/`CssLayoutEngineColumns`, which don't route through either fixed path (same
engine-independence family as the CSS Fragmentation §5.2 margin-truncation gap, see
[Margins adjoining an unforced break](margin-truncation-remaining-gaps.md)). A `page` change/reversion
among flex items or multicol children may not begin/revert on a fresh page, and — mirroring the table
symptom above before its fix — a flex/multicol container's own registration bypassing the correct
inheritance/withdraw path could plausibly leak a name past its own subtree the same way. No equivalent
per-child break hook exists in either engine at all yet (`CssLayoutEngineFlex`'s
`RelocateLinesAcrossFragmentainers` only handles `break-inside`/monolithic content), so closing this
needs new plumbing built from scratch in each, not a small extension of an existing hook the way the
table fix was. See `NamedPageLayoutIntegrationTests`/`NamedPageGeometryAttributionTests` for the fixed
block-flow and table behavior (used-value reversion, band restoration, margin-box suppression no longer
leaking, mid-table name transitions, and reversion past a table's own subtree).

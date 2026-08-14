# Collapsed table borders now resolve per CSS 2.1 §17.6.2, closing issue #735

[Issue #735](https://github.com/jhaygood86/PeachPDF/issues/735) reported that a `border-collapse: collapse`
table lost an earlier row's `border-bottom` whenever the next row's cells also had a `background-color`.
Root cause: `CssLayoutEngineTable` hardcoded a flat `-1pt` row/column overlap for collapsed tables regardless
of actual border width, and every participating box (table/row/cell) painted its own border independently -
so a later row's opaque background, nudged into the row above by that overlap, painted over and erased the
shared border. Below that symptom was a bigger gap: PeachPDF had no real CSS 2.1 §17.6.2 border-conflict
resolution at all. This was a full implementation of that resolution, landed in seven phases (pure
resolver/grid types → wiring to real tables → the paint-order fix that actually closes #735 → replacing the
`-1` spacing hack with resolved widths → a used-border-width override for correct box-model insets →
`<col>`/`<colgroup>` border and background participation → repeated `<thead>`/`<tfoot>` correctness),
test-first throughout.

New types: `CollapsedBorderResolver` (pure, static - width wins, then style priority, then origin priority
cell > row > row-group > column > column-group > table, then position), `TableGrid` (topology - which real
`CssBox` occupies each row×column cell, honoring colspan/rowspan), `CollapsedBorderModel` (resolves the
whole table once, holds a `CollapsedBorder` per grid-line segment). Resolved borders paint once, late
(`FragmentPainter`'s `PaintCollapsedTableBorders`, after every table-internal background), via
`CssBox.CollapsedBorderSegments` - the same paint-time-consumed-geometry-carried-on-CssBox pattern this
codebase already uses for `ColumnRuleSegments`/`PageBreakBottoms`.

## Load-bearing lessons

**Hand-tracing exact numeric residuals against an independently-computed ground truth is what caught two
rounds of geometry double-counting** that visual/coarse testing missed during the spacing-rework phase - a
table's own border box and its first cell's own border box are the *same* edge under the collapsing model,
not offset by half a border width as first assumed; `GetWidthSum()`'s own formula gave the right total while
actual cell positioning was off by exactly `firstColumnBorderWidth / 2` until this was worked out by hand.

**A collapsed table's own reported "does it visually work" during manual testing can be a false positive.**
Empirically testing a multi-page table with a repeating `<thead>` looked correct - the border under the
repeated header matched on every page - but this was coincidental: the fixture used uniform border styling
everywhere, so a completely wrong resolution (reusing page 1's DOM-order-adjacent-row answer on every later
page, via `CssProxyBox`'s *shared* row objects being repositioned live per page) was indistinguishable from
a correct one. The regression test that actually proves this (`RepeatedThead_BoundaryToBody_ResolvesFreshPerPage_NotReusedFromPage1`)
needed *non-uniform* per-page styling - only row 1 (page 1's real DOM neighbor) gets a distinctive border,
so a stale/reused resolution and a correct fresh-per-page one produce visibly different output.

**A cached "actual border width" property can silently mean two different things depending on when it's
read.** `ApplyCollapsedUsedBorderWidths()` overwrites every participant's `ActualBorder*Width` with the
box-model *used* half-width (needed for `ClientLeft`/content insets to agree with the spacing model), and
that overwrite is destructive - there is no way to recover the original declared width from that property
once it has run. `CollapsedBorderModel.Resolve` (the whole-table static resolution) runs *before* the
override and reads the cached property safely; `ResolveRepeatedGroupBoundary` (added in this phase, run at
the very end of layout once final per-page row geometry exists) runs *after* it, and initially read the same
cached property - producing a uniformly-wrong resolved width (half of an unrelated single resolution) on
every page rather than the intended per-page-varying one. Fixed by adding `DerivedStyle.NaturalBorder*Width`
(and `CssBox` forwarders), which recompute from the declared CSS value directly rather than through the
cache, for exactly this one late-reading call site.

**A repeated `<thead>`/`<tfoot>` is not one box with N positions - it is one *shared* box repositioned live,
N times, by whichever `CssProxyBox` laid out last.** `CssProxyBox.PerformLayoutImp` mutates the shared
source row-group's own `Location`/its rows' positions to match *that* proxy's page every time any proxy for
it lays out - so by the time layout finishes, the source's live geometry reflects only the last page it was
positioned at. Reading `TableGrid.RowAt(...)`'s live `Location`/`ActualBottom` for a header/footer row (as
the main per-table `EmitCollapsedBorderSegments` correctly does for an ordinary, never-repositioned body row)
is wrong for a repeated group on every page but the one whose proxy happened to run last. The fix reads
`CssProxyBox.SourceGeometry` (`BoxGeometrySnapshot`, already the sanctioned mechanism `FragmentEmitter` uses
to place a repeat's own content) instead, and accumulates segments from *every* proxy of the same source
into that source's single `CollapsedBorderSegments` list - `FragmentEmitter.ChildrenOf` gives a repeated
group's fragment the *source* box's identity on every page (not the proxy's own), so paint's
`box.CollapsedBorderSegments` check reads the shared box regardless of which proxy is being painted; each
page's own fragment `OriginY`/clip then naturally shows only the segments that belong on that page, exactly
like the main table's own multi-page segment list already works.

**A row whose own border overlaps the boundary it sits on can start *before* that boundary's Y, not after
it.** Finding "which body row is actually adjacent to this header/footer proxy" by filtering
`row.Location.Y >= proxyBottom` silently skips the true neighbor whenever that row's own border-collapse
overlap (up to the resolved line width) pulls its top above the header's own bottom - found via a debug
trace showing a ~73pt gap between `proxyBottom` and the "adjacent" row that predicate found, when the real
neighbor sat only 9pt before it. Fixed by filtering on the row's *span* reaching the boundary
(`row.ActualBottom >= proxyBottom`) instead of its start position.

## Post-change review pass caught three real bugs, all fixed before landing

A review agent run against the repeated-group-boundary phase per this repo's own convention found three
genuine correctness issues in `CollapsedBorderModel.ResolveRepeatedGroupBoundary`, all fixed:

- It read cell occupancy via a hand-rolled `Boxes`-list scan (`FindCellAtColumn`) instead of
  `TableGrid.CellAt`, which independently re-derived (and could disagree with) the grid's own
  rowspan/colspan accounting - replaced by passing grid row indices straight through and calling
  `TableGrid.CellAt`/`RowGroupAt`, which also deleted the duplicate, less-defensive `colspan` parsing
  `FindCellAtColumn` had.
- It never added the adjacent row's own row-group (e.g. an explicit `<tbody>`'s own border) as a
  candidate - `CollapsedBorderOrigin.RowGroup` is a real, independently-competing priority tier
  (`CollapsedBorder.cs`), not merely a tiebreak, so a wider `<tbody>` border was silently never even
  considered. Fixed by adding `grid.RowGroupAt(adjacentRow)` as a candidate alongside the group side's own.
- A repeated group with no opposing row at all (a `<thead>`-only/`<tfoot>`-only table, or a header
  immediately followed by a footer with zero body rows between them) silently dropped that boundary
  entirely. Fixed with a fallback to the whole-table static resolution's own value for that line, which
  already models exactly this case correctly (`CollectHorizontal`'s `line == 0`/`line == grid.RowCount`
  gating already includes Column/ColumnGroup/Table origins there).

Chasing down a regression test for the first fix surfaced a second, genuinely pre-existing bug one level
deeper: `TableGrid.Build`'s own column-index computation can silently drop a cell from the grid entirely
when a `rowspan` inside a multi-row `<thead>`/`<tfoot>` reaches from an earlier row into a later one, since
`CssLayoutEngineTable.InsertEmptyBoxes` (which pads exactly this gap with a placeholder for an ordinary
body row) never touches a detached header's/footer's own rows. This predates this change and affects every
`TableGrid` consumer, not just the new boundary resolution - tracked as
[#736](https://github.com/jhaygood86/PeachPDF/issues/736) and left out of scope (see
`.claude/accepted-gaps/rowspan-inside-a-multi-row-thead-tfoot-grid-occupancy.md`), since fixing it touches
shared grid-construction code every other `TableGrid` consumer depends on and warrants its own change and
test pass.

## Evidence

Full suite green throughout (8825 tests, net8.0), zero warnings on `dotnet build -t:Rebuild`. The final
repeated-header fix was verified two ways beyond the unit tests: a purpose-built regression fixture with
non-uniform per-page border styling (see above), and an actual multi-page PDF rendered through the CLI and
rasterized with both PDFium and MuPDF - page 1 correctly shows a distinctive 6px border under the header
(the real DOM-adjacent row's own border winning), every later page correctly shows an ordinary 1px border
instead of the page-1 value, confirmed identically by both renderers.

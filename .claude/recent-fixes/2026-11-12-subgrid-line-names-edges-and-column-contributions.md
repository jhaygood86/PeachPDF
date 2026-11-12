# Subgrid: inherited line names, edge insets and column contributions (#276)

The #276 list was partly stale. Probing first: `grid`/`grid-template` already accepted `subgrid` in the `<'grid-template-rows'>`/`-columns` slots
(`TryTemplateAxis` goes through `GridTrackListGrammar`, which handles the keyword), so the only real shorthand defect was the areas form accepting
`/ subgrid`, which css-grid-2 does not allow (its columns are an `<explicit-track-list>`).

**Line names (§9.2).** The parent's name table (own `[names]` plus the `-start`/`-end` lines of `grid-template-areas`) is sliced for the lines
the subgrid spans (`SpannedLineNames`) and carried on `GridSubgridContext.ColumnLineNames`/`RowLineNames`; the subgrid adds them to its own table
(`AddInheritedNames`). Nested subgrids inherit through their parent's already-merged table. Three places build a context (the placement loop, the resumed
`BuildSubgridContextMap`, the measuring `MeasureSubgridItem`), and all three had to carry the names or the measure pass would place items differently
from the final one. Chromium (`GridLineResolver` with a parent resolver) and Gecko (`LineNameMap::mParentLineNameMap`) do the same.

**Edge insets (§9.2).** The adopted tracks used to start at the subgrid's client origin, so a padded subgrid's tracks sat off the parent's lines. They now start
at the margin edge (`InlineOrigin - _colStartInset`), on the lines, and the items in the first and last track are inset by the margin, border and padding
there (`ItemColumnWidth`, the cell origin in the placement loop). The rows the subgrid reports to its parent carry the edges too, or a parent's auto row
would squeeze the items. Horizontal, non-`rtl` grids only.

**Column contributions (§9.3).** A column subgrid used to be skipped when the parent measured its auto columns. Its items are now placed over its span
(`CollectSubgridColumnContributions`, with the inherited names) and each contributes to the parent column it sits over, spanning ones through the existing
`DistributeSpanContribution`; nested column subgrids flatten into the outermost columns. A subgrid with `grid-template-areas` or column auto-flow returns
false and contributes nothing, as before.

**Trap.** The line-name and edge tests only discriminate with the right fixture: the nested-subgrid name test passed without the change until the inner
subgrid spanned two columns (auto placement then put the item in the same place), and a spanning-item contribution test needed `justify-content: start`
so the auto columns were not stretched to the container.

**Evidence.** `GridSubgridShorthandTests`, `GridSubgridLineNameTests` (5 of 6 fail without the change), `GridSubgridInsetTests` (4 of 5),
`GridSubgridColumnContributionTests` (4 of 5). Engine suite 19726 and PeachPDF.Tests 1931 pass; the 252-showcase SVG sweep is unchanged and the
`css_grid_subgrid` showcase gained a section, checked by rasterizing.

**Not done.** Vertical-writing-mode and `rtl` edge insets; the area-overlap rule for partially overlapped parent areas (names go on every spanned
line); the indefinite-height contribution of row subgrids to column sizing.

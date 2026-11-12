# A grid container and its rows follow each page's measure (#1516)

The grid analog of [the flex fix](2026-09-29-flex-container-and-items-follow-each-pages-measure.md), scoped down. A block-level grid that continues onto a
page of a different width (`@page :first { margin-left: 0 }`, a per-page `size`) sized every row to the page it started on: the container stayed 562pt on
a 512pt page and `1fr` columns stayed 187.3pt instead of 170.7pt. css-break-3 §5.1: each fragment uses its own fragmentainer's size.

**What the spec and the engines do.** The spec asks for per-fragment recalculation. Chromium (`grid_layout_algorithm.cc` reuses the first fragment's geometry, with a
TODO for varying inline sizes) and Gecko (`SharedGridData` columns reused by continuations) do not; the flex fix compared against Prince, which does. This follows
Prince for rows that start on a page and Chromium for items that continue onto another one.

**Load-bearing idea.** Everything column sizing reads (definitions, collapsed auto-fit columns, the items' intrinsic contributions) does not depend on the width,
so it is gathered once and `SizeColumnsAt(width, gap)` sizes the same columns for any measure. Sizing, placement and fragmentation relocation then run in a loop
(`MaxPerPageAttempts`), each row taking the columns of the measure it was last found to land on (`ColumnsOf`, `SpanWidthOf`); `LandingMeasures` reads where relocation
put each row (`SlotStartingAt` of its top) and the loop ends when no row's page measure differs from the one it was sized against. A grid whose rows all land on one measure
runs once, as before. The container's own frame is stated per slot, not derived by the emitter, as for flex (`StateContainerFrame`, `StateSpannedFrames`,
`RecordInlineFrame`), and read at materialization. Items are unpinned on a fresh pass (`ItemContentCommit.UnpinIfPinned`).

**Found by running it.**
- `grid-template-columns: repeat(auto-fill, ...)` makes the column *count* depend on the measure, so those grids (and subgrids, whose lines the parent fixes, and
  vertical grids) stay on the one measure; the loop is only entered for a grid that cannot change shape.
- The first, hoisted version of the column inputs had to keep `contentWidth` mutation for the vertical indefinite case; `SizeColumnsAt` takes the width as an argument
  rather than closing over it, so a second measure cannot read the first's.
- The stale-pin guard changed nothing observable here (a repeated-layout probe was already stable); it is there because the loop measures items against several widths.

**Not done.** A row whose items continue onto a page of another measure keeps its starting column sizes (an item does not change width mid-way, as in Chromium);
a row-spanning item takes the measure of its first row.

**Evidence.** `GridPerPageReflowIntegrationTests` (container frame per page, rows on a later page sized to its columns, fixed columns unchanged, single page unchanged, no
words lost or repeated across 3+ pages of a straddling grid); the first two fail on the old engine. Engine suite 19709 and PeachPDF.Tests 1931 pass; the 252-showcase
SVG sweep is unchanged; the new `paged_media_grid_per_page_reflow` showcase was rasterized through both MuPDF and PDFium.

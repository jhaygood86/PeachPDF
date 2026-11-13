# Grid rows honor `repeat(auto-fit | auto-fill, ...)`

`grid-template-rows` ignored an auto repeat (only columns resolved one), so the rows of `repeat(auto-fit, 20px)` never existed.

- The row template now goes through `ResolveColumnTemplate` against the container's definite block size (0 when it has none), vertical writing modes excluded.
- An `auto-fit` row no item occupies collapses to size 0 after placement, as the column case already did; `auto-fill` keeps its empty rows.
- Evidence: WPT css-grid `grid-content-distribution-with-collapsed-tracks-*` +12, nothing lost; public-API tests in `LayoutPaintCoverageTests`.

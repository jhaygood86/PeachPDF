# Column flex items and grid items have no automatic minimum size

`min-width` now has the spec's initial value `auto`, and a flex item in a **row** container is floored at its automatic minimum size (css-flexbox-1 §4.5: min-content, or the definite `width` when smaller; zero for a scroll container). Two cases are still not covered, tracked in #1916:

- a flex item in a **column** container (`min-height: auto`) can still shrink below its content height. `min-height` keeps the initial value `0`: it is read in table rows, block layout and fragmentation as well, and a column's main size comes from laying the item out, not from a min-content measure.
- a **grid** item can still be sized below its min-content width by its track.

Don't "fix" either by flooring in `DistributeFlex` for a column: the column path never calls `ResolveAutoMinimums`, on purpose, and its sizes feed the page-fragmentation code.

A third case: the floor is applied only when a row **overflows** (`ResolveAutoMinimums` returns early otherwise), so an item that would be *grown* below its min-content size is not floored either. `flex: 1` items (basis 0) with one unbreakable long word still split the row equally instead of giving that item its min-content width. Closing it means measuring min-content for every item of every row, not just overflowing ones, and running the §9.7 freeze loop for grow too; that changes far more layouts than the shrink case, so it was left for its own change.

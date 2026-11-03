# The automatic minimum size is applied only when a line overflows (shrink), not to grown items

A flex item in a row or column container is floored at its automatic minimum size (css-flexbox-1 §4.5) and a grid item in a bare `fr` track
keeps its min-content width (css-grid-1 §6.6), see [the #1916 fix](../recent-fixes/2026-11-03-automatic-minimum-size-for-column-flex-items-and-grid-fr-tracks.md).
What is left:

- **The floor is applied only when a line overflows** (`ResolveAutoMinimums` returns early otherwise), so an item that would be *grown* below its
  min-content size is not floored: `flex: 1` items (basis 0) with one unbreakable long word still split the row (or column) equally instead of
  giving that item its min-content size. Closing it means measuring min-content for every item of every line and running the §9.7 freeze loop for
  grow too; that changes far more layouts than the shrink case.
- **A replaced item in a column container** (an `<img>` stretched across the cross axis) has no floor: its main size comes from its aspect ratio and
  stretched width, which the specification gives a transferred minimum of its own.
- **A grid item spanning several `fr` tracks** contributes nothing to their bases (the simplified span distribution has no flexible-track step), and
  a grid *row* has no automatic minimum (rows are never `fr`-expanded here).
- **An `auto` (not `fr`) grid track** still uses the item's min-content, ignoring a scroll container's or an authored `min-width`'s exemption.

Don't "fix" the first by flooring in `DistributeFlex`: the grow path has no min-content measure, and its sizes feed the page-fragmentation code.

# The automatic minimum size is not applied to every case

A flex item in a row or column container is floored at its automatic minimum size (css-flexbox-1 §4.5), a grid item in a track whose minimum
sizing function is `auto` (a bare `fr`, `auto`, `minmax(auto, …)`) keeps its automatic minimum (css-grid-1 §6.6, with a scroll container's and an
authored `min-width`'s/`min-height`'s exemptions), and `fr` rows are expanded against a definite container height, see
[the #1916 fix](../recent-fixes/2026-11-03-automatic-minimum-size-for-column-flex-items-and-grid-fr-tracks.md) and
[the #2174 fix](../recent-fixes/2026-11-12-grid-auto-tracks-and-flexible-rows-follow-the-automatic-minimum.md). What is left (tracked by #2174):

- **A replaced item in a column container** (an `<img>` stretched across the cross axis) has no floor: css-flexbox-1 §4.5 gives it the smaller of a
  content size suggestion (its intrinsic height) and a transferred size suggestion (its cross size through the aspect ratio, only when that cross size is
  definite - a stretched one is not), so the rule differs from a non-replaced item's and the existing pinned test
  (`StretchedBorderBoxColumnItem_WithPadding_FillsLineCrossSize`, an image shrunk to its column) is what browsers do for a stretched image. Left until a
  document needs the definite-cross-size case.
- **Flexible rows against an indefinite height** are content-sized per row. §12.7.1 sizes them by the largest content/flex-factor ratio across the rows
  (`1fr 2fr` with equal content is 10 and 20, not 10 and 10).

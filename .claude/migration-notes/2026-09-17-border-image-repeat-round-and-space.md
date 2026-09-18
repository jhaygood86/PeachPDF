# border-image-repeat: round now resizes tiles to fit evenly, and space is newly supported

`border-image-repeat` (CSS Backgrounds and Borders 3 §13.5) accepted only `stretch`/`repeat`/`round` as
keywords, and `round` rendered identically to `repeat` (plain edge-to-edge tiling of each edge/center
region, with the final partial tile simply clipped off).

Two things change:

- **`round` now actually rounds.** Per spec, the tile count along an axis is
  `round(destExtent / naturalTileExtent)` (minimum 1), and each tile is resized to
  `destExtent / count` so that count fits exactly with no partial tile at the boundary. A document that
  declared `border-image-repeat: round` (or `round` as either component of the two-value form) previously
  got `repeat`'s tiling and will now see each tile resized to close evenly - a visible change for any
  edge/center whose natural tile size didn't already divide its destination extent evenly.
- **`space` is a newly recognized keyword.** Previously `border-image-repeat: space` (or `space` as
  either component) failed to parse at all, so the whole declaration was dropped and the property kept
  its inherited/initial value (`stretch`). It now parses and renders per spec: tiles keep their natural
  size, `count = floor(destExtent / naturalTileExtent)` (minimum 1), and - when at least two tiles fit -
  an equal gap is inserted between each pair so the first and last tiles touch the edges of the
  destination extent. When fewer than one whole tile fits, a single tile is painted at its natural size
  rather than clipped or resized.

Confirmed against `git show d3231134:docs/html-css-support.md` (the previous tip's known-limitations
list for `border-image-repeat`, which explicitly called out both behaviors as gaps) that this is a
genuine behavior change relative to the last release, not a pre-existing difference.

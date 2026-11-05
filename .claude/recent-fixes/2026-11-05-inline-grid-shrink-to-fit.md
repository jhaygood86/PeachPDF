# An auto-width inline-grid shrinks to fit (#2207)

`CssLayoutEngine.GetBoxWidth` now gives an auto-width, in-flow `inline-grid` the fit-content width,
`min(max-content, max(min-content, available))` (CSS Grid 1 section 3, css-sizing-3), instead of the stretch width.
Before, the grid engine sized its auto tracks against the full line and only afterwards narrowed the box.

- The issue's premise that a grid has no max-content measure was stale: `CssLayoutEngineGrid.TryMeasureIntrinsicWidth`
  already existed and `CssBox.GetMinMaxWidth` already used it. Only the width resolution was missing.
- Deliberately not `GetFitContentWidth`: its `GetLargestChildWidth` walk calls `GetBoxWidth` on block-level grid items,
  which return the stretch width and would re-inflate the result.
- A grid `TryMeasureIntrinsicWidth` declines (subgrid, auto-repeat, `grid-template-areas`, column auto-flow) keeps the
  old stretch-then-shrink path.
- Checked against Chromium on a 400px container: text-only 46.3px, two columns with gap/padding/border = tracks+gap+decoration, long content clamps to the line.
- Not changed: the post-commit shrink in `CssLayoutEngineGrid` still sets the right edge from the tracks, so a `max-width` smaller than the tracks' min-content is not honoured (pre-existing).
- Tests: `InlineGridShrinkToFitTests`.

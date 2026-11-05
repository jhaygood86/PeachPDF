# An auto-width `inline-grid` fills the available width instead of shrinking to fit

Tracked in #2207. CSS Grid 1 section 3 (`inline-grid`) with CSS Sizing 3 (fit-content) is the rule it deviates from.

**What.** `<span style="display: inline-grid">Badge</span>` (no `width`, no `grid-template-columns`) comes out as wide as the line, and the text after it wraps below, where a browser makes the
box as wide as its content (CSS Grid 1 section 3, "inline-grid": the box's auto inline size is fit-content like an inline-block's, css-sizing-3). An `inline-flex` and an `inline-block`
shrink correctly; a grid with an explicit `width`, or with fixed tracks, is unaffected.

**Why.** `CssLayoutEngine.GetBoxWidth` gives an auto-width box the stretch width unless it is floated, and the grid engine sizes its auto track against that width before its own
"shrink to the content extent" step (`CssLayoutEngineGrid`, after the commit pass) narrows the box: the track has already been stretched across the line by then. Giving an inline-grid
the shrink-to-fit width up front needs the max-content width of a grid container, which `GetMaxContentWidth` does not compute from the tracks (it stacks the children), so a multi-column
inline-grid would be narrowed wrongly.

**What would close it.** A max-content size for a grid container from its track definitions (the sum of the max-content contributions of its columns plus gaps), used by `GetBoxWidth` for
`inline-grid` as it is for a float. Not done with the baseline work: it changes track sizing for every auto-width inline-grid.

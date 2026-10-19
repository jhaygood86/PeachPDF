# Replaced grid item sizing: area percentages, default object size, inline-axis baseline (#1674, #1675, #1676, #1292)

**Percentage basis (#1676).** A grid item's percentage `width`/`min-width`/`max-width` resolved against the grid
container because the box tree resolves them against `ContainingBlock`. Changing that basis in the box tree would
touch every layout mode, so the grid engine instead rewrites the three values to absolute lengths against the
item's grid-area content width for the duration of `MeasureItemHeight`/`PlaceItemInCell` and restores them
(`ResolveAreaPercentages`, the same poke-layout-restore idiom the engine already uses for `Width`). With that, the
deliberate clamp of a stretched replaced item's ratio width to its area is gone: `max-width: 100%` clamps it
correctly, and the plain case overflows to 160pt as in Chrome.

**Trap found by running the suite.** `LineTopFollowsItsWordsTests.AnInlineBlockInAnEngineItem_IsDrawnOnceAtItsLine`
used `width:60%` and passed because the old container basis gave one particular geometry. With the area basis the
same document double-draws a word in the grid (the old logic with the equivalent `30%` double-draws too), so the
stale-line double paint it guards is still latent and only hidden by that geometry. The test now uses `120%`
for the grid so it keeps the geometry it was minimized with. Not fixed here.

**Default object size (#1674, #1675).** 300x150 CSS px lives once, in `CssLayoutEngine.DefaultObjectWidth/Height`.
`CssBoxFrame` measures an image-less iframe with it and `hasIntrinsicRatio: false` (an iframe has a default size,
not a ratio: stretching its width must not change its height), and an iframe is now skipped by the line-height
strut like images. A one-sided svg gets it from `SvgIntrinsicSize.ResolveForBox` (a new method; `Resolve` is also
read by painters and CSS images and was left alone). `HasNaturalSize` now says a one-sided svg has a natural size in
both axes. A poster-less `<video>` is not a replaced box here, so the grid pins it to the default size
(`ReplacedNaturalSize.DefaultSize`). A replaced item keeps its natural width even where it overflows the area
(`GetFitContentWidth` was given an unbounded space for it).

**Not done.** The iframe's 2px default border: `CssBoxFrame.SetErrorBorder` sets one but the laid-out box has
border width 0, so an iframe is 225pt wide where Chrome has 228pt. A video with a poster is still unsized by this.

**Inline-axis baseline (#1292).** `justify-self`/`justify-items: baseline` was dropped by the keyword maps (the
value came out as `auto`), so the layout never saw it. `JustifyItem` gained `Baseline`/`LastBaseline` (spelled
`baseline`, `first baseline`, `last baseline`). Chrome, measured: in a horizontal writing mode the items of a
column that ask for it line their border boxes up at the largest start margin among them (a margin-0 item beside a
`margin-left:30px` one moves 30px), `last baseline` sits at the end edge, and a lone item stays where it is.
`AlignColumnBaselines` does that for single-column items. Spanning items and vertical writing modes are not covered
(the grid engine is horizontal-only).
